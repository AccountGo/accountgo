import { Container, StopParams } from "@cloudflare/containers";
import { constantTimeEqual, parseSyncSqlHeaders } from "./sync-utils";

export { constantTimeEqual, parseSyncSqlHeaders };

const CONTAINER_PORT = 8001;

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

export class AccountGoApiContainer extends Container<Env> {
  restored = false;
  private initPromise: Promise<void> | null = null;
  private loggedMismatch = false;

  constructor(ctx: DurableObjectState, env: Env) {
    super(ctx as any, env);
    this.defaultPort = CONTAINER_PORT;
    this.sleepAfter = "10m";
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

  async restoreD1ToContainer(): Promise<boolean> {
    const db = (this.env as any).DB;
    if (!db) return true;
    try {
      const tableRes = await db
        .prepare(
          "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '_cf_%' AND name != 'd1_migrations' AND name != '__SchemaVersion'"
        )
        .all();

      const syncTables = (tableRes.results || []).map((r: any) => r.name);

      const statements: string[] = [];

      for (const table of syncTables) {
        try {
          const res = await db.prepare(`SELECT * FROM "${table}"`).all();
          if (res.results && res.results.length > 0) {
            for (const row of res.results) {
              const keys = Object.keys(row);
              const cols = keys.map((k) => `"${k}"`).join(", ");
              const vals = keys
                .map((k) => {
                  const v = row[k];
                  if (v === null || v === undefined) return "NULL";
                  if (typeof v === "number") return String(v);
                  if (typeof v === "boolean") return v ? "1" : "0";
                  return `'${String(v).replace(/'/g, "''")}'`;
                })
                .join(", ");
              statements.push(`INSERT OR REPLACE INTO "${table}" (${cols}) VALUES (${vals});`);
            }
          }
        } catch (tableErr) {
          // Table might not exist or empty
        }
      }

      const syncKey = resolveInternalSyncKey(this.env);
      if (!syncKey) {
        console.error("Cold start D1 restore skipped: INTERNAL_SYNC_KEY (or SECRET_KEY fallback) is not configured.");
        return false;
      }

      const batchSize = 500;
      if (statements.length === 0) {
        if (syncTables.length > 0) {
          const res = await this.containerFetch("http://127.0.0.1:8001/api/internal/d1-restore", {
            method: "POST",
            headers: {
              "content-type": "application/json",
              "x-internal-key": syncKey,
            },
            body: JSON.stringify({ tablesToClear: syncTables, statements: [] }),
          });
          if (!res.ok) {
            const errText = await res.text();
            throw new Error(`Clear tables failed (${res.status}): ${errText}`);
          }
        }
      } else {
        for (let i = 0; i < statements.length; i += batchSize) {
          const batch = statements.slice(i, i + batchSize);
          const isFirstBatch = i === 0;
          const res = await this.containerFetch("http://127.0.0.1:8001/api/internal/d1-restore", {
            method: "POST",
            headers: {
              "content-type": "application/json",
              "x-internal-key": syncKey,
            },
            body: JSON.stringify({
              tablesToClear: isFirstBatch ? syncTables : undefined,
              statements: batch,
            }),
          });
          if (!res.ok) {
            const errText = await res.text();
            throw new Error(`Restore batch ${i / batchSize} failed (${res.status}): ${errText}`);
          }
          const result = (await res.json()) as any;
          if (result.restored !== batch.length) {
            throw new Error(`Batch count mismatch: restored ${result.restored} of ${batch.length}`);
          }
        }
      }
      return true;
    } catch (err) {
      console.error("Cold start D1 restore failed:", err);
      return false;
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

      const buildRes = await this.containerFetch("http://127.0.0.1:8001/api/internal/build", {
        method: "GET",
        headers: { "x-internal-key": syncKey },
      });

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

    // D1 restore
    const restoredOk = await this.restoreD1ToContainer();
    if (!restoredOk) {
      throw new RestoreError("Database restore from Cloudflare D1 failed, please retry in a few seconds");
    }

    this.restored = true;
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

    // 1. Wait for container to be ready
    try {
      await this.startAndWaitForPorts({
        ports: CONTAINER_PORT,
        cancellationOptions: { portReadyTimeoutMS: 90_000 },
      });
    } catch (err) {
      console.error("[worker] Container startup timed out or failed:", err);
      return new Response(
        JSON.stringify({
          title: "Service Unavailable",
          status: 503,
          detail: "Service is starting up, please retry in a few seconds",
        }),
        {
          status: 503,
          headers: {
            "content-type": "application/problem+json",
            "Retry-After": "5",
          },
        }
      );
    }

    // 2. Build check and D1 restore (single-flight across concurrent requests)
    if (!this.restored) {
      const p = (this.initPromise ??= this.initialize());

      try {
        await p;
      } catch (prepErr: any) {
        if (this.initPromise === p) {
          this.initPromise = null;
        }
        this.restored = false;

        if (prepErr instanceof BuildMismatchError) {
          return new Response(
            JSON.stringify({
              title: "Service Unavailable",
              status: 503,
              detail: prepErr.message,
            }),
            {
              status: 503,
              headers: {
                "content-type": "application/problem+json",
                "Retry-After": "5",
              },
            }
          );
        }

        if (prepErr instanceof RestoreError) {
          return new Response(
            JSON.stringify({
              title: "Service Unavailable",
              status: 503,
              detail: prepErr.message,
            }),
            {
              status: 503,
              headers: {
                "content-type": "application/problem+json",
                "Retry-After": "5",
              },
            }
          );
        }

        console.error("[worker] Container initialization failed:", prepErr);
        return new Response(
          JSON.stringify({
            title: "Service Unavailable",
            status: 503,
            detail: "Service is starting up, please retry in a few seconds",
          }),
          {
            status: 503,
            headers: {
              "content-type": "application/problem+json",
              "Retry-After": "5",
            },
          }
        );
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
      const containerRes = await this.containerFetch(containerReq);

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
        return new Response(
          JSON.stringify({
            title: "Change not saved",
            status: 503,
            detail: "Your change could not be saved. Please retry.",
          }),
          {
            status: 503,
            headers: {
              "content-type": "application/problem+json",
              "Retry-After": "5",
            },
          }
        );
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
          return new Response(
            JSON.stringify({
              title: "Change not saved",
              status: 503,
              detail: "Your change could not be saved. Please retry.",
            }),
            {
              status: 503,
              headers: {
                "content-type": "application/problem+json",
                "Retry-After": "5",
              },
            }
          );
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
      this.restored = false;
      this.initPromise = null;
      throw err;
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
    const stub = env.CONTAINER.get(id);
    return stub.fetch(request);
  },
};
