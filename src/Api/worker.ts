import { Container, StopParams } from "@cloudflare/containers";
import { constantTimeEqual, parseSyncSqlHeaders } from "./sync-utils";

export { constantTimeEqual, parseSyncSqlHeaders };

const CONTAINER_PORT = 8001;

export class AccountGoApiContainer extends Container<Env> {
  restored = false;

  constructor(ctx: DurableObject["ctx"], env: Env) {
    super(ctx, env);
    this.defaultPort = CONTAINER_PORT;
    this.sleepAfter = "10m";
    this.enableInternet = true;

    const envVars: Record<string, string> = {
      ASPNETCORE_URLS: `http://+:${CONTAINER_PORT}`,
      ASPNETCORE_ENVIRONMENT: "Production",
      DatabaseProvider: "Sqlite",
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

      const secret = this.env.SECRET_KEY || (this.env as any).SECRET__key;
      if (!secret) {
        console.error("Cold start D1 restore skipped: SECRET_KEY is not configured.");
        return false;
      }

      const batchSize = 500;
      if (statements.length === 0) {
        if (syncTables.length > 0) {
          const res = await this.containerFetch("http://127.0.0.1:8001/api/internal/d1-restore", {
            method: "POST",
            headers: {
              "content-type": "application/json",
              "x-internal-key": secret,
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
              "x-internal-key": secret,
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
      } catch (e) {}
      this.restored = false;
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

    // 2. Build check and D1 restore (if not yet restored in this container's lifetime)
    if (!this.restored) {
      try {
        const secret = this.env.SECRET_KEY || (this.env as any).SECRET__key;

        // Build check (1.2.4)
        if (this.env.BUILD_SHA) {
          if (!secret) {
            console.error("Cold start check failed: SECRET_KEY is not configured.");
            return new Response(
              JSON.stringify({
                title: "Service Unavailable",
                status: 503,
                detail: "Service is starting up, please retry in a few seconds",
              }),
              { status: 503, headers: { "content-type": "application/problem+json", "Retry-After": "5" } }
            );
          }

          const buildRes = await this.containerFetch("http://127.0.0.1:8001/api/internal/build", {
            method: "GET",
            headers: { "x-internal-key": secret },
          });

          if (!buildRes.ok) {
            throw new Error(`Build check failed with status ${buildRes.status}`);
          }

          const buildData = (await buildRes.json()) as { sha?: string };
          const containerSha = buildData?.sha?.trim();
          const expectedSha = this.env.BUILD_SHA.trim();

          if (containerSha && expectedSha && containerSha !== expectedSha) {
            console.warn(
              `[worker] BUILD_SHA mismatch: container=${containerSha}, expected=${expectedSha}. Destroying container.`
            );
            await this.destroy();
            this.restored = false;
            return new Response(
              JSON.stringify({
                title: "Service Unavailable",
                status: 503,
                detail: "Container build version mismatch, reloading...",
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

        // D1 restore
        const restoredOk = await this.restoreD1ToContainer();
        if (!restoredOk) {
          this.restored = false;
          return new Response(
            JSON.stringify({
              title: "Service Unavailable",
              status: 503,
              detail: "Database restore from Cloudflare D1 failed, please retry in a few seconds",
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
        this.restored = true;
      } catch (prepErr) {
        console.error("[worker] Container initialization failed:", prepErr);
        this.restored = false;
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
      const syncStatements = parseSyncSqlHeaders(containerRes.headers);
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
        if (key.startsWith("x-d1-sync-sql")) {
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
      throw err;
    }
  }
}

export interface Env {
  DB: D1Database;
  CONTAINER: DurableObjectNamespace<AccountGoApiContainer>;
  ENVIRONMENT?: string;
  CONTAINER_NAME?: string;
  SECRET_KEY?: string;
  SECRET__key?: string;
  BUILD_SHA?: string;
  CF_VERSION_METADATA?: {
    id: string;
    tag: string;
  };
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
