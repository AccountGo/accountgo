import { DurableObject } from "cloudflare:workers";

function constantTimeEqual(a: string, b: string): boolean {
  const enc = new TextEncoder();
  const aBuf = enc.encode(a);
  const bBuf = enc.encode(b);
  if (aBuf.byteLength !== bBuf.byteLength) {
    return false;
  }
  return crypto.subtle.timingSafeEqual(aBuf, bBuf);
}

const CONTAINER_PORT = 8001;
const MAX_READY_ATTEMPTS = 30;
const RETRY_DELAY_MS = 1000;
const KEEPALIVE_HOLD_MS = 15000;

export class AccountGoApiContainer extends DurableObject {
  containerReady = false;
  lastActivityAt?: number;
  alarmArmed = false;

  async touchActivity(now: number) {
    this.lastActivityAt = now;
    if (!this.alarmArmed) {
      this.alarmArmed = true;
      if ((await this.ctx.storage.getAlarm()) === null) {
        await this.ctx.storage.setAlarm(now + KEEPALIVE_HOLD_MS);
      }
    }
  }

  async alarm() {
    const container = (this.ctx as any).container;
    const sleepAfterMs = 15 * 60 * 1000; // 15 mins keepalive
    const running = Boolean(container?.running);

    if (running && this.lastActivityAt && Date.now() - this.lastActivityAt < sleepAfterMs) {
      this.alarmArmed = true;
      await new Promise((resolve) => setTimeout(resolve, KEEPALIVE_HOLD_MS));
      await this.ctx.storage.setAlarm(Date.now() + 1000);
    } else {
      this.alarmArmed = false;
      this.containerReady = false;
    }
  }

  async restoreD1ToContainer(portObj: any): Promise<boolean> {
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
          const res = await portObj.fetch(
            new Request("http://127.0.0.1:8001/api/internal/d1-restore", {
              method: "POST",
              headers: {
                "content-type": "application/json",
                "x-internal-key": secret,
              },
              body: JSON.stringify({ tablesToClear: syncTables, statements: [] }),
            })
          );
          if (!res.ok) {
            const errText = await res.text();
            throw new Error(`Clear tables failed (${res.status}): ${errText}`);
          }
        }
      } else {
        for (let i = 0; i < statements.length; i += batchSize) {
          const batch = statements.slice(i, i + batchSize);
          const isFirstBatch = i === 0;
          const res = await portObj.fetch(
            new Request("http://127.0.0.1:8001/api/internal/d1-restore", {
              method: "POST",
              headers: {
                "content-type": "application/json",
                "x-internal-key": secret,
              },
              body: JSON.stringify({
                tablesToClear: isFirstBatch ? syncTables : undefined,
                statements: batch,
              }),
            })
          );
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

  async fetch(request: Request): Promise<Response> {
    this.ctx.waitUntil(this.touchActivity(Date.now()));
    try {
      const container = (this.ctx as any).container;
      if (!container) {
        return new Response(
          JSON.stringify({
            title: "Internal Server Error",
            status: 500,
            detail: "Container context missing",
          }),
          { status: 500, headers: { "content-type": "application/problem+json" } }
        );
      }

      const url = new URL(request.url);
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
          if (typeof container.destroy === "function") {
            await container.destroy();
          } else if (typeof container.stop === "function") {
            await container.stop();
          }
        } catch (e) {}
        this.containerReady = false;
        return new Response(JSON.stringify({ status: "container restarted" }), {
          status: 200,
          headers: { "content-type": "application/json" }
        });
      }

      // Collect environment variables from worker env
      const env: Record<string, string> = {
        ASPNETCORE_URLS: `http://+:${CONTAINER_PORT}`,
        ASPNETCORE_ENVIRONMENT: "Production",
        DatabaseProvider: "Sqlite",
      };
      for (const [key, value] of Object.entries(this.env)) {
        if (typeof value === "string") {
          env[key] = value;
        }
      }

      // Start container if not yet running
      if (!container.running) {
        try {
          await container.start({ env, enableInternet: true });
        } catch (err: any) {
          // If already in-flight or started, continue
        }
      }

      const httpUrl = request.url.replace(/^https:/i, "http:");
      const portObj = container.getTcpPort(CONTAINER_PORT);

      // Readiness probe on cold start
      if (!this.containerReady) {
        let lastErr: any;
        let ready = false;
        for (let attempt = 1; attempt <= MAX_READY_ATTEMPTS; attempt++) {
          try {
            await portObj.fetch(new Request(httpUrl, { method: "HEAD", redirect: "manual" }));
            ready = true;
            break;
          } catch (err) {
            lastErr = err;
            await new Promise((resolve) => setTimeout(resolve, RETRY_DELAY_MS));
          }
        }
        if (!ready) {
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
        const restoredOk = await this.restoreD1ToContainer(portObj);
        if (!restoredOk) {
          this.containerReady = false;
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
        this.containerReady = true;
      }

      // Forward incoming request
      const containerReq = new Request(httpUrl, {
        method: request.method,
        headers: request.headers,
        body: request.body,
        redirect: "manual",
        duplex: "half",
      } as any);

      try {
        const containerRes = await portObj.fetch(containerReq);

        // Check if .NET emitted real-time D1 mutation SQL in response header
        const syncSqlBase64 = containerRes.headers.get("x-d1-sync-sql");
        if (syncSqlBase64 && (this.env as any).DB) {
          try {
            const sql = atob(syncSqlBase64);
            this.ctx.waitUntil((this.env as any).DB.exec(sql));
          } catch (syncErr: any) {
            console.error("Failed executing real-time D1 mutation:", syncErr);
          }
        }

        // Strip internal header before returning to client
        const headers = new Headers(containerRes.headers);
        headers.delete("x-d1-sync-sql");

        return new Response(containerRes.body, {
          status: containerRes.status,
          statusText: containerRes.statusText,
          headers,
        });
      } catch (err) {
        this.containerReady = false;
        throw err;
      }
    } catch (err: any) {
      return new Response(
        JSON.stringify({
          title: "Internal Server Error",
          status: 500,
          detail: "An unexpected error occurred in container orchestration.",
        }),
        { status: 500, headers: { "content-type": "application/problem+json" } }
      );
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
}

export default {
  async fetch(request: Request, env: Env, ctx: ExecutionContext): Promise<Response> {
    const url = new URL(request.url);

    if (url.pathname === "/healthz" || url.pathname === "/readyz") {
      return new Response(JSON.stringify({ status: "healthy", runtime: "cloudflare-container-dotnet10" }), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      });
    }

    const containerName = request.headers.get("x-container-name") || env.CONTAINER_NAME || "default";
    const id = env.CONTAINER.idFromName(containerName);
    const stub = env.CONTAINER.get(id);
    return stub.fetch(request);
  },
};
