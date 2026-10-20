import { DurableObject } from "cloudflare:workers";

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

  async fetch(request: Request): Promise<Response> {
    this.ctx.waitUntil(this.touchActivity(Date.now()));
    try {
      const container = (this.ctx as any).container;
      if (!container) {
        return new Response("Container context missing", { status: 500 });
      }

      const url = new URL(request.url);
      if (url.pathname === "/restart-container" || request.headers.get("x-restart-container") === "true") {
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
              error: "Container startup timeout",
              lastError: lastErr?.message || "container did not become ready",
            }),
            { status: 504, headers: { "content-type": "application/json" } }
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
        return await portObj.fetch(containerReq);
      } catch (err) {
        this.containerReady = false;
        throw err;
      }
    } catch (err: any) {
      return new Response(
        JSON.stringify({ scope: "AccountGoApiContainer", error: err.message, stack: err.stack }),
        { status: 500, headers: { "content-type": "application/json" } }
      );
    }
  }
}

export interface Env {
  CONTAINER: DurableObjectNamespace<AccountGoApiContainer>;
  ENVIRONMENT?: string;
  CONTAINER_NAME?: string;
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

    const containerName = env.CONTAINER_NAME || "default";
    const id = env.CONTAINER.idFromName(containerName);
    const stub = env.CONTAINER.get(id);
    return stub.fetch(request);
  },
};
