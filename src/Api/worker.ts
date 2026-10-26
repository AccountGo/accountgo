import { Container, StopParams } from "@cloudflare/containers";
import { errorText, isTransientContainerError } from "./container-errors";
import { constantTimeEqual, parseSyncSqlHeaders } from "./sync-utils";

export { constantTimeEqual, parseSyncSqlHeaders };
export { errorText, isTransientContainerError } from "./container-errors";

const CONTAINER_PORT = 8001;
const PORT_READY_TIMEOUT_MS = 90_000;
/** Retries for transient container proxy/start races during cold start and D1 restore. */
const CONTAINER_RETRY_ATTEMPTS = 4;
const CONTAINER_RETRY_BASE_DELAY_MS = 750;

class BuildMismatchError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "BuildMismatchError";
  }
}

class RestoreError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "RestoreError";
  }
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function serviceUnavailable(detail: string, retryAfterSec = 5): Response {
  return new Response(
    JSON.stringify({
      title: "Service Unavailable",
      status: 503,
      detail,
    }),
    {
      status: 503,
      headers: {
        "content-type": "application/problem+json",
        "Retry-After": String(retryAfterSec),
      },
    }
  );
}

export class AccountGoApiContainer extends Container<Env> {
  restored = false;
  private initPromise: Promise<void> | null = null;
  private loggedMismatch = false;

  constructor(ctx: DurableObjectState, env: Env) {
    super(ctx as any, env);
    this.defaultPort = CONTAINER_PORT;
    // Keep warm longer: cold start + D1 restore is 30–80s and was surfacing as 503 on /api/auth/refresh.
    this.sleepAfter = "30m";
    this.enableInternet = true;

    const envVars: Record<string, string> = {
      ASPNETCORE_URLS: `http://+:${CONTAINER_PORT}`,
      ASPNETCORE_ENVIRONMENT: "Production",
      DatabaseProvider: "Sqlite",
      // The data is restored from D1 once the container is up, so it must not seed its own demo data first.
      RESTORE_FROM_D1: "true",
    };
    for (const [key, value] of Object.entries(this.env)) {
      if (typeof value === "string") {
        envVars[key] = value;
      }
    }
    this.envVars = envVars;
  }

  override onStop(params: StopParams) {
    this.restored = false;
    this.initPromise = null;
    this.loggedMismatch = false;
  }

  private async ensurePortsReady(): Promise<void> {
    await this.startAndWaitForPorts({
      ports: CONTAINER_PORT,
      cancellationOptions: { portReadyTimeoutMS: PORT_READY_TIMEOUT_MS },
    });
  }

  /**
   * containerFetch after ensuring the port is listening. Retries transient
   * "not running" / "just exited" races that show up during cold start and D1 restore.
   */
  private async containerFetchWithRetry(
    input: string | Request,
    init: RequestInit | undefined,
    label: string
  ): Promise<Response> {
    let lastErr: unknown;
    for (let attempt = 1; attempt <= CONTAINER_RETRY_ATTEMPTS; attempt++) {
      try {
        await this.ensurePortsReady();
        if (typeof input === "string") {
          return await this.containerFetch(input, init);
        }
        return await this.containerFetch(input);
      } catch (err) {
        lastErr = err;
        const transient = isTransientContainerError(err);
        console.warn(
          `[worker] ${label} attempt ${attempt}/${CONTAINER_RETRY_ATTEMPTS} failed transient=${transient}: ${errorText(err)}`
        );
        if (!transient || attempt === CONTAINER_RETRY_ATTEMPTS) {
          break;
        }
        await sleep(CONTAINER_RETRY_BASE_DELAY_MS * attempt);
      }
    }
    throw lastErr instanceof Error ? lastErr : new Error(String(lastErr));
  }

  async restoreD1ToContainer(): Promise<void> {
    const db = (this.env as any).DB;
    if (!db) return;
    try {
      const tableRes = await db
        .prepare(
          "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '_cf_%' AND name != 'd1_migrations' AND name != '__SchemaVersion'"
        )
        .all();

      const syncTables = (tableRes.results || []).map((r: any) => r.name as string);

      // Structured rows — never embed cell text into SQL (semicolons/keywords broke allowlists).
      type RestoreRow = { table: string; data: Record<string, unknown> };
      const rows: RestoreRow[] = [];

      for (const table of syncTables) {
        try {
          const res = await db.prepare(`SELECT * FROM "${table}"`).all();
          if (res.results && res.results.length > 0) {
            for (const row of res.results) {
              const data: Record<string, unknown> = {};
              for (const [key, value] of Object.entries(row as Record<string, unknown>)) {
                data[key] = normalizeD1Cell(value);
              }
              rows.push({ table, data });
            }
          }
        } catch {
          // Table might not exist or be unreadable
        }
      }

      const syncKey = resolveInternalSyncKey(this.env);
      if (!syncKey) {
        console.error("Cold start D1 restore skipped: INTERNAL_SYNC_KEY (or SECRET_KEY fallback) is not configured.");
        throw new RestoreError(
          "Database restore skipped: INTERNAL_SYNC_KEY (or SECRET_KEY) is not configured on the Worker."
        );
      }

      const batchSize = 200;
      if (rows.length === 0) {
        if (syncTables.length > 0) {
          const res = await this.containerFetchWithRetry(
            "http://127.0.0.1:8001/api/internal/d1-restore",
            {
              method: "POST",
              headers: {
                "content-type": "application/json",
                "x-internal-key": syncKey,
              },
              body: JSON.stringify({ tablesToClear: syncTables, rows: [] }),
            },
            "d1-restore-clear"
          );
          if (!res.ok) {
            const errText = await res.text();
            throw new Error(`Clear tables failed (${res.status}): ${errText}`);
          }
        }
        return;
      }

      for (let i = 0; i < rows.length; i += batchSize) {
        const batch = rows.slice(i, i + batchSize);
        const isFirstBatch = i === 0;
        const res = await this.containerFetchWithRetry(
          "http://127.0.0.1:8001/api/internal/d1-restore",
          {
            method: "POST",
            headers: {
              "content-type": "application/json",
              "x-internal-key": syncKey,
            },
            body: JSON.stringify({
              tablesToClear: isFirstBatch ? syncTables : undefined,
              rows: batch,
            }),
          },
          `d1-restore-batch-${i / batchSize}`
        );
        if (!res.ok) {
          const errText = await res.text();
          throw new Error(`Restore batch ${i / batchSize} failed (${res.status}): ${errText}`);
        }
        const result = (await res.json()) as { restored?: number };
        if (result.restored !== batch.length) {
          throw new Error(`Batch count mismatch: restored ${result.restored} of ${batch.length}`);
        }
      }
    } catch (err) {
      console.error("Cold start D1 restore failed:", err);
      if (err instanceof RestoreError) throw err;
      throw new RestoreError(
        `Database restore from Cloudflare D1 failed: ${err instanceof Error ? err.message : String(err)}`
      );
    }
  }

  private async initialize(): Promise<void> {
    const syncKey = resolveInternalSyncKey(this.env);

    // Build check (1.2.4)
    if (this.env.BUILD_SHA) {
      if (!syncKey) {
        console.error("Cold start check failed: INTERNAL_SYNC_KEY (or SECRET_KEY fallback) is not configured.");
        throw new Error("INTERNAL_SYNC_KEY is not configured.");
      }

      const buildRes = await this.containerFetchWithRetry(
        "http://127.0.0.1:8001/api/internal/build",
        {
          method: "GET",
          headers: { "x-internal-key": syncKey },
        },
        "build-check"
      );

      if (!buildRes.ok) {
        const errText = await buildRes.text().catch(() => "");
        console.error(
          `[worker] Build check failed: status=${buildRes.status} body=${errText.slice(0, 300)}`
        );
        throw new Error(`Build check failed with status ${buildRes.status}`);
      }

      const buildData = (await buildRes.json()) as { sha?: string };
      const containerSha = buildData?.sha?.trim();
      const expectedSha = this.env.BUILD_SHA.trim();

      if (containerSha && expectedSha && containerSha !== expectedSha) {
        if (!this.loggedMismatch) {
          this.loggedMismatch = true;
          console.warn(
            `[worker] BUILD_SHA mismatch: container=${containerSha}, expected=${expectedSha}. Destroying container.`
          );
        }
        await this.destroy();
        throw new BuildMismatchError("Container build version mismatch, reloading...");
      }
    }

    // D1 restore (throws RestoreError with detail on failure)
    await this.restoreD1ToContainer();

    this.restored = true;
  }

  /** Single-flight init with one full retry after a transient container death. */
  private async ensureInitialized(): Promise<void> {
    if (this.restored) return;

    const runOnce = async (): Promise<void> => {
      const p = (this.initPromise ??= this.initialize());
      try {
        await p;
      } catch (err) {
        if (this.initPromise === p) {
          this.initPromise = null;
        }
        this.restored = false;
        throw err;
      }
    };

    try {
      await runOnce();
    } catch (err) {
      // RestoreError wraps the underlying proxy message; match either form.
      const transient =
        isTransientContainerError(err) ||
        (err instanceof RestoreError && isTransientContainerError(err.message));
      if (!transient) {
        throw err;
      }
      console.warn(`[worker] initialize retry after transient failure: ${errorText(err)}`);
      this.initPromise = null;
      this.restored = false;
      await sleep(CONTAINER_RETRY_BASE_DELAY_MS);
      await this.ensurePortsReady();
      await runOnce();
    }
  }

  override async fetch(request: Request): Promise<Response> {
    const url = new URL(request.url);

    // Handle restart endpoint
    if (url.pathname === "/restart-container" || request.headers.get("x-restart-container") === "true") {
      const secret = this.env.SECRET_KEY || (this.env as any).SECRET__key;
      const providedKey = request.headers.get("x-internal-key");
      if (!secret || !providedKey || !constantTimeEqual(secret, providedKey)) {
        return new Response(
          JSON.stringify({
            title: "Not Found",
            status: 404,
            detail: "Not Found",
          }),
          { status: 404, headers: { "content-type": "application/problem+json" } }
        );
      }

      try {
        await this.destroy();
      } catch (err: any) {
        console.error("[worker] Failed to destroy container on restart:", err);
        return new Response(
          JSON.stringify({
            title: "Internal Server Error",
            status: 500,
            detail: "Failed to restart container",
          }),
          { status: 500, headers: { "content-type": "application/problem+json" } }
        );
      }
      this.restored = false;
      this.initPromise = null;
      this.loggedMismatch = false;
      return new Response(JSON.stringify({ status: "container restarted" }), {
        status: 200,
        headers: { "content-type": "application/json" },
      });
    }

    // 1. Wait for container to be ready (with retries for start races)
    try {
      await this.ensurePortsReady();
    } catch (err) {
      console.error("[worker] Container startup timed out or failed:", err);
      return serviceUnavailable("Service is starting up, please retry in a few seconds", 5);
    }

    // 2. Build check and D1 restore (single-flight across concurrent requests)
    if (!this.restored) {
      try {
        await this.ensureInitialized();
      } catch (prepErr: any) {
        if (prepErr instanceof BuildMismatchError) {
          return serviceUnavailable(prepErr.message, 5);
        }

        if (prepErr instanceof RestoreError) {
          // Transient restore races should ask the client to retry, not look like a permanent outage.
          const retryAfter = isTransientContainerError(prepErr) || isTransientContainerError(prepErr.message) ? 5 : 8;
          return serviceUnavailable(prepErr.message, retryAfter);
        }

        console.error("[worker] Container initialization failed:", prepErr);
        return serviceUnavailable("Service is starting up, please retry in a few seconds", 5);
      }
    }

    // 3. Strip internal headers before forwarding to container
    const cleanHeaders = new Headers(request.headers);
    cleanHeaders.delete("x-container-name");
    cleanHeaders.delete("x-internal-key");
    cleanHeaders.delete("x-restart-container");

    const httpUrl = request.url.replace(/^https:/i, "http:");
    const containerReq = new Request(httpUrl, {
      method: request.method,
      headers: cleanHeaders,
      body: request.body,
      redirect: "manual",
      duplex: "half",
    } as any);

    try {
      const containerRes = await this.containerFetchWithRetry(containerReq, undefined, `proxy ${request.method} ${url.pathname}`);

      // Handle mutation SQL sync
      let syncStatements: string[] = [];
      try {
        syncStatements = await parseSyncSqlHeaders(containerRes.headers);
      } catch (headerErr: any) {
        console.error(
          `[worker] Failed to parse D1 sync headers: method=${request.method} path=${url.pathname} error=${headerErr?.message ?? headerErr}`
        );
        try {
          await this.destroy();
        } catch (destroyErr) {
          console.error("[worker] Failed to destroy container after D1 header error:", destroyErr);
        }
        this.restored = false;
        this.initPromise = null;
        return serviceUnavailable("Your change could not be saved. Please retry.", 5);
      }

      if (syncStatements.length > 0 && (this.env as any).DB) {
        const db = (this.env as any).DB;
        try {
          const prepared = [
            db.prepare("PRAGMA defer_foreign_keys = on"),
            ...syncStatements.map((s) => db.prepare(s)),
          ];
          await db.batch(prepared);
        } catch (syncErr: any) {
          console.error(
            `[worker] D1 sync batch failed: method=${request.method} path=${url.pathname} error=${syncErr?.message ?? syncErr} statementCount=${syncStatements.length}`
          );
          try {
            await this.destroy();
          } catch (destroyErr) {
            console.error("[worker] Failed to destroy container after D1 sync failure:", destroyErr);
          }
          this.restored = false;
          this.initPromise = null;
          return serviceUnavailable("Your change could not be saved. Please retry.", 5);
        }
      }

      // Strip sync headers before returning response
      const responseHeaders = new Headers(containerRes.headers);
      for (const key of Array.from(responseHeaders.keys())) {
        if (key.startsWith("x-d1-sync-sql") || key === "x-d1-sync-encoding") {
          responseHeaders.delete(key);
        }
      }

      return new Response(containerRes.body, {
        status: containerRes.status,
        statusText: containerRes.statusText,
        headers: responseHeaders,
      });
    } catch (err) {
      // Never rethrow: uncaught DO exceptions become Issues "failed invocation" with no Retry-After.
      console.error(
        `[worker] Proxy failed method=${request.method} path=${url.pathname}: ${errorText(err)}`
      );
      this.restored = false;
      this.initPromise = null;
      return serviceUnavailable(
        isTransientContainerError(err)
          ? "Service is starting up, please retry in a few seconds"
          : "Upstream container error, please retry",
        5
      );
    }
  }
}

export interface Env {
  DB: D1Database;
  CONTAINER: DurableObjectNamespace<AccountGoApiContainer>;
  ENVIRONMENT?: string;
  CONTAINER_NAME?: string;
  /** Dedicated key for container-loopback internal sync; prefer over SECRET_KEY. */
  INTERNAL_SYNC_KEY?: string;
  SECRET_KEY?: string;
  SECRET__key?: string;
  BUILD_SHA?: string;
  CF_VERSION_METADATA?: {
    id: string;
    tag: string;
  };
}

/** Prefer INTERNAL_SYNC_KEY so JWT signing material is not the DB restore credential. */
function resolveInternalSyncKey(env: Env): string | undefined {
  return env.INTERNAL_SYNC_KEY || env.SECRET_KEY || (env as any).SECRET__key || undefined;
}

/** JSON-safe cell values for structured restore (no SQL string embedding). */
function normalizeD1Cell(value: unknown): unknown {
  if (value === null || value === undefined) return null;
  if (typeof value === "string" || typeof value === "number" || typeof value === "boolean") return value;
  if (typeof ArrayBuffer !== "undefined" && value instanceof ArrayBuffer) {
    const bytes = new Uint8Array(value);
    let binary = "";
    for (let i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]!);
    return { $b64: btoa(binary) };
  }
  if (typeof Uint8Array !== "undefined" && value instanceof Uint8Array) {
    let binary = "";
    for (let i = 0; i < value.length; i++) binary += String.fromCharCode(value[i]!);
    return { $b64: btoa(binary) };
  }
  // Fallback: stringify unknown shapes
  return String(value);
}

let loggedContainerName = false;

export default {
  async fetch(request: Request, env: Env, ctx: ExecutionContext): Promise<Response> {
    const url = new URL(request.url);

    if (url.pathname === "/healthz" || url.pathname === "/readyz") {
      return new Response(JSON.stringify({ status: "healthy", runtime: "cloudflare-container-dotnet10" }), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      });
    }

    const isAdminRequest =
      url.pathname === "/restart-container" || request.headers.get("x-restart-container") === "true";

    let containerName: string;

    if (isAdminRequest) {
      const secret = env.SECRET_KEY || (env as any).SECRET__key;
      const providedKey = request.headers.get("x-internal-key");
      if (!secret || !providedKey || !constantTimeEqual(secret, providedKey)) {
        return new Response(
          JSON.stringify({
            title: "Not Found",
            status: 404,
            detail: "Not Found",
          }),
          { status: 404, headers: { "content-type": "application/problem+json" } }
        );
      }

      const versionId = env.CF_VERSION_METADATA?.id || "dev";
      const defaultName = `${env.CONTAINER_NAME || "default"}-${versionId}`;
      containerName = request.headers.get("x-container-name") || defaultName;
    } else {
      const versionId = env.CF_VERSION_METADATA?.id || "dev";
      containerName = `${env.CONTAINER_NAME || "default"}-${versionId}`;

      const isDev = !env.CF_VERSION_METADATA?.id || env.CF_VERSION_METADATA.id === "dev" || env.ENVIRONMENT !== "Production";
      if (!loggedContainerName && isDev) {
        loggedContainerName = true;
        console.warn(`[worker] Routing public request to container: ${containerName}`);
      }
    }

    const id = env.CONTAINER.idFromName(containerName);
    // Prefer US placement for new Durable Objects (existing DO names keep their original region).
    const stub = env.CONTAINER.get(id, { locationHint: "enam" });
    return stub.fetch(request);
  },
};
