import { Container, StopParams } from "@cloudflare/containers";
import {
  isTieredCapacityEnabled,
  resolveWriterContainerName,
} from "./container-capacity";
import {
  BuildMismatchError,
  RestoreError,
  applyD1SyncFromResponse,
  buildContainerEnvVars,
  errorText,
  isRestoreTransient,
  isTransientContainerError,
  resolveInternalSyncKey,
  restoreD1ToContainer,
  serviceUnavailable,
  sleep,
} from "./container-shared";
import { constantTimeEqual, parseSyncSqlHeaders } from "./sync-utils";
import {
  isServerTimingEnabled,
  logSlowWorkerRequest,
  scrubPathForLog,
  stripD1SyncHeaders,
  withServerTiming,
  type TimingSegment,
} from "./server-timing";
import type { Env } from "./worker-env";
import { AccountGoApiContainerV2 } from "./worker-container-v2";

export { constantTimeEqual, parseSyncSqlHeaders };
export { errorText, isTransientContainerError } from "./container-errors";
export {
  isTieredCapacityEnabled,
  resolveContainerInstanceType,
  resolveWriterContainerName,
} from "./container-capacity";
export type { Env } from "./worker-env";
export { AccountGoApiContainerV2 };

const CONTAINER_PORT = 8001;
const PORT_READY_TIMEOUT_MS = 90_000;
/** Retries for transient container proxy/start races during cold start and D1 restore. */
const CONTAINER_RETRY_ATTEMPTS = 4;
const CONTAINER_RETRY_BASE_DELAY_MS = 750;

export class AccountGoApiContainer extends Container<Env> {
  restored = false;
  private initPromise: Promise<void> | null = null;
  private loggedMismatch = false;

  constructor(ctx: DurableObjectState, env: Env) {
    super(ctx as DurableObjectState, env);
    this.defaultPort = CONTAINER_PORT;
    // Idle sleep (not forever keep-warm). Cold start + D1 restore is 30–80s; 15m balances cost vs 503 thrash.
    this.sleepAfter = "15m";
    this.enableInternet = true;
    this.envVars = buildContainerEnvVars(
      this.env as unknown as Record<string, unknown>,
      CONTAINER_PORT
    );
  }

  override onStop(_params: StopParams) {
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
    await restoreD1ToContainer({
      db: this.env.DB,
      env: this.env,
      containerFetchWithRetry: (input, init, label) =>
        this.containerFetchWithRetry(input, init, label),
    });
  }

  private async initialize(): Promise<void> {
    const syncKey = resolveInternalSyncKey(this.env);

    // Build check (1.2.4)
    if (this.env.BUILD_SHA) {
      if (!syncKey) {
        console.error(
          "Cold start check failed: INTERNAL_SYNC_KEY (or SECRET_KEY fallback) is not configured."
        );
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
      if (!isRestoreTransient(err)) {
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
    const timingEnabled = isServerTimingEnabled(this.env);
    const workerStarted = Date.now();
    const cold = !this.restored;
    const scrubbedRoute = scrubPathForLog(url.pathname);

    const finish = (response: Response, extra: TimingSegment[] = []): Response => {
      const workerMs = Date.now() - workerStarted;
      const segments: TimingSegment[] = [...extra, { name: "worker", dur: workerMs }];
      if (cold) segments.push({ name: "cold", desc: "1" });
      const timed = withServerTiming(response, segments, timingEnabled);
      logSlowWorkerRequest({
        route: scrubbedRoute,
        status: timed.status,
        segments,
        workerMs,
      });
      return timed;
    };

    // Handle restart endpoint
    if (url.pathname === "/restart-container" || request.headers.get("x-restart-container") === "true") {
      const secret = this.env.SECRET_KEY || this.env.SECRET__key;
      const providedKey = request.headers.get("x-internal-key");
      if (!secret || !providedKey || !constantTimeEqual(secret, providedKey)) {
        return finish(
          new Response(
            JSON.stringify({
              title: "Not Found",
              status: 404,
              detail: "Not Found",
            }),
            { status: 404, headers: { "content-type": "application/problem+json" } }
          )
        );
      }

      try {
        await this.destroy();
      } catch (err: unknown) {
        console.error("[worker] Failed to destroy container on restart:", err);
        return finish(
          new Response(
            JSON.stringify({
              title: "Internal Server Error",
              status: 500,
              detail: "Failed to restart container",
            }),
            { status: 500, headers: { "content-type": "application/problem+json" } }
          )
        );
      }
      this.restored = false;
      this.initPromise = null;
      this.loggedMismatch = false;
      return finish(
        new Response(JSON.stringify({ status: "container restarted" }), {
          status: 200,
          headers: { "content-type": "application/json" },
        })
      );
    }

    // 1. Wait for container to be ready (with retries for start races)
    try {
      await this.ensurePortsReady();
    } catch (err) {
      console.error("[worker] Container startup timed out or failed:", err);
      return finish(serviceUnavailable("Service is starting up, please retry in a few seconds", 5));
    }

    // 2. Build check and D1 restore (single-flight across concurrent requests)
    if (!this.restored) {
      try {
        await this.ensureInitialized();
      } catch (prepErr: unknown) {
        if (prepErr instanceof BuildMismatchError) {
          return finish(serviceUnavailable(prepErr.message, 5));
        }

        if (prepErr instanceof RestoreError) {
          const retryAfter =
            isTransientContainerError(prepErr) || isTransientContainerError(prepErr.message)
              ? 5
              : 8;
          return finish(serviceUnavailable(prepErr.message, retryAfter));
        }

        console.error("[worker] Container initialization failed:", prepErr);
        return finish(serviceUnavailable("Service is starting up, please retry in a few seconds", 5));
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
    } as RequestInit);

    try {
      const containerStarted = Date.now();
      const containerRes = await this.containerFetchWithRetry(
        containerReq,
        undefined,
        `proxy ${request.method} ${scrubbedRoute}`
      );
      const containerMs = Date.now() - containerStarted;

      if (containerRes.status === 101) {
        return containerRes;
      }

      // Handle mutation SQL sync
      let syncStatements: string[] = [];
      try {
        syncStatements = await parseSyncSqlHeaders(containerRes.headers);
      } catch (headerErr: unknown) {
        const message = headerErr instanceof Error ? headerErr.message : String(headerErr);
        console.error(
          `[worker] Failed to parse D1 sync headers: method=${request.method} path=${scrubbedRoute} error=${message}`
        );
        try {
          await this.destroy();
        } catch (destroyErr) {
          console.error("[worker] Failed to destroy container after D1 header error:", destroyErr);
        }
        this.restored = false;
        this.initPromise = null;
        return finish(serviceUnavailable("Your change could not be saved. Please retry.", 5), [
          { name: "container", dur: containerMs },
        ]);
      }

      let d1Ms: number | undefined;
      try {
        const d1Started = Date.now();
        await applyD1SyncFromResponse({
          db: this.env.DB,
          syncStatements,
          method: request.method,
          pathname: url.pathname,
        });
        if (syncStatements.length > 0) {
          d1Ms = Date.now() - d1Started;
        }
      } catch {
        try {
          await this.destroy();
        } catch (destroyErr) {
          console.error("[worker] Failed to destroy container after D1 sync failure:", destroyErr);
        }
        this.restored = false;
        this.initPromise = null;
        return finish(serviceUnavailable("Your change could not be saved. Please retry.", 5), [
          { name: "container", dur: containerMs },
        ]);
      }

      const responseHeaders = stripD1SyncHeaders(containerRes.headers);
      const base = new Response(containerRes.body, {
        status: containerRes.status,
        statusText: containerRes.statusText,
        headers: responseHeaders,
      });

      const extra: TimingSegment[] = [{ name: "container", dur: containerMs }];
      if (d1Ms !== undefined) {
        extra.push({
          name: "d1",
          dur: d1Ms,
          desc: syncStatements.length === 1 ? "1 stmt" : `${syncStatements.length} stmts`,
        });
      }
      return finish(base, extra);
    } catch (err) {
      // Never rethrow: uncaught DO exceptions become Issues "failed invocation" with no Retry-After.
      console.error(
        `[worker] Proxy failed method=${request.method} path=${scrubbedRoute}: ${errorText(err)}`
      );
      this.restored = false;
      this.initPromise = null;
      return finish(
        serviceUnavailable(
          isTransientContainerError(err)
            ? "Service is starting up, please retry in a few seconds"
            : "Upstream container error, please retry",
          5
        )
      );
    }
  }
}

let loggedContainerName = false;

interface TurnstileVerifyResponse {
  success: boolean;
  action?: string;
  hostname?: string;
  "error-codes"?: string[];
}

async function verifyTurnstile(
  token: unknown,
  expectedAction: string,
  secret: string | undefined,
  hostnamesConfig: string | undefined,
  clientIp: string | null
): Promise<boolean> {
  // If no secret configured, skip gating (allows running when Turnstile is disabled)
  if (!secret) return true;

  const expectedHostnames = new Set(
    (hostnamesConfig ?? "localhost, 127.0.0.1, accountgo.dev, app.accountgo.dev")
      .split(",")
      .map((hostname) => hostname.trim())
      .filter(Boolean)
  );

  if (
    typeof token !== "string" ||
    token.length === 0 ||
    token.length > 2048 ||
    expectedHostnames.size === 0
  ) {
    return false;
  }

  let result: TurnstileVerifyResponse;
  try {
    const body = new URLSearchParams({
      secret,
      response: token,
    });
    if (clientIp) {
      body.set("remoteip", clientIp);
    }

    const r = await fetch("https://challenges.cloudflare.com/turnstile/v0/siteverify", {
      method: "POST",
      headers: { "Content-Type": "application/x-www-form-urlencoded" },
      signal: AbortSignal.timeout(10_000),
      body,
    });
    if (!r.ok) throw new Error(`siteverify ${r.status}`);
    result = (await r.json()) as TurnstileVerifyResponse;
  } catch {
    return false;
  }

  if (
    !result.success ||
    result.action !== expectedAction ||
    !result.hostname ||
    !expectedHostnames.has(result.hostname)
  ) {
    return false;
  }

  return true;
}

export default {
  async fetch(request: Request, env: Env, _ctx: ExecutionContext): Promise<Response> {
    const url = new URL(request.url);

    if (url.pathname === "/healthz" || url.pathname === "/readyz") {
      return new Response(
        JSON.stringify({ status: "healthy", runtime: "cloudflare-container-dotnet10" }),
        {
          status: 200,
          headers: { "Content-Type": "application/json" },
        }
      );
    }

    const authActions: Record<string, string> = {
      "/api/auth/send-otp": "login",
      "/api/auth/signup": "signup",
      "/api/auth/verify-otp": "verify_otp",
    };

    if (request.method === "POST" && authActions[url.pathname]) {
      const expectedAction = authActions[url.pathname];
      let bodyText = "";
      let token: string | null = request.headers.get("cf-turnstile-response");

      try {
        bodyText = await request.text();
        if (bodyText) {
          const parsed = JSON.parse(bodyText);
          if (parsed && typeof parsed["cf-turnstile-response"] === "string") {
            token = parsed["cf-turnstile-response"];
          }
        }
      } catch {
        // Fall back to header
      }

      const clientIp = request.headers.get("cf-connecting-ip") || request.headers.get("x-forwarded-for");
      const isValid = await verifyTurnstile(
        token,
        expectedAction,
        env.TURNSTILE_SECRET,
        env.TURNSTILE_HOSTNAMES,
        clientIp
      );

      if (!isValid) {
        return new Response(
          JSON.stringify({
            title: "Forbidden",
            status: 403,
            detail: "Turnstile verification failed",
          }),
          { status: 403, headers: { "content-type": "application/problem+json" } }
        );
      }

      request = new Request(request.url, {
        method: request.method,
        headers: request.headers,
        body: bodyText,
      });
    }

    const isAdminRequest =
      url.pathname === "/restart-container" || request.headers.get("x-restart-container") === "true";

    let containerName: string;

    if (isAdminRequest) {
      const secret = env.SECRET_KEY || env.SECRET__key;
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
      const defaultName = resolveWriterContainerName(env.CONTAINER_NAME, versionId);
      containerName = request.headers.get("x-container-name") || defaultName;
    } else {
      const versionId = env.CF_VERSION_METADATA?.id || "dev";
      containerName = resolveWriterContainerName(env.CONTAINER_NAME, versionId);

      const isDev =
        !env.CF_VERSION_METADATA?.id ||
        env.CF_VERSION_METADATA.id === "dev" ||
        env.ENVIRONMENT !== "Production";
      if (!loggedContainerName && isDev) {
        loggedContainerName = true;
        console.warn(`[worker] Routing public request to container: ${containerName}`);
      }
    }

    const useV2 = isTieredCapacityEnabled(env.TIERED_CAPACITY_ENABLED);
    if (useV2) {
      const binding = env.CONTAINER_V2;
      if (!binding) {
        console.error(
          "[worker] TIERED_CAPACITY_ENABLED=true but CONTAINER_V2 binding is missing"
        );
        return serviceUnavailable("Tiered capacity is misconfigured", 30);
      }
      const id = binding.idFromName(containerName);
      const stub = binding.get(id, { locationHint: "enam" });
      return stub.fetch(request);
    }

    const id = env.CONTAINER.idFromName(containerName);
    // Prefer US placement for new Durable Objects (existing DO names keep their original region).
    const stub = env.CONTAINER.get(id, { locationHint: "enam" });
    return stub.fetch(request);
  },
};
