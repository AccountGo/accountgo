import { Container, StopParams } from "@cloudflare/containers";
import {
  isTieredCapacityEnabled,
  resolveWriterContainerName,
} from "./container-capacity";
import {
  BuildMismatchError,
  ContainerConfigError,
  StartupInterruptedError,
  buildContainerEnvVars,
  errorText,
  isStartupTransient,
  isTransientContainerError,
  requirePersistentDatabase,
  resolveInternalSyncKey,
  serviceUnavailable,
  sleep,
} from "./container-shared";
import { shouldSkipPortsReady, type ReadyTiming } from "./ports-ready";
import {
  ContainerUnavailableError,
  RestoreState,
  bufferRequest,
  fetchFromContainer,
  toRequest,
  unavailableResponse,
  watchContainerExit,
  type ContainerHost,
} from "./container-proxy";
import { constantTimeEqual } from "./constant-time";
import {
  isServerTimingEnabled,
  logSlowWorkerRequest,
  scrubPathForLog,
  withServerTiming,
  type TimingSegment,
} from "./server-timing";
import type { Env } from "./worker-env";
import { AccountGoApiContainerV2 } from "./worker-container-v2";

export { constantTimeEqual };
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
/** Retries for transient container proxy/start races during cold start. */
const CONTAINER_RETRY_ATTEMPTS = 4;
const CONTAINER_RETRY_BASE_DELAY_MS = 750;

export class AccountGoApiContainer extends Container<Env> {
  private readonly restoreState = new RestoreState();
  private readonly host: ContainerHost;
  private initPromise: Promise<void> | null = null;
  private loggedMismatch = false;

  /** True only while the running container is the one the last build check passed against. */
  get restored(): boolean {
    return this.restoreState.restored;
  }

  constructor(ctx: DurableObjectState, env: Env) {
    super(ctx as DurableObjectState, env);
    this.defaultPort = CONTAINER_PORT;
    // Idle sleep (not forever keep-warm); 15m balances cost vs cold starts.
    this.sleepAfter = "15m";
    this.enableInternet = true;
    this.envVars = buildContainerEnvVars(
      this.env as unknown as Record<string, unknown>,
      CONTAINER_PORT
    );
    const self = this;
    this.host = {
      get restored() {
        return self.restored;
      },
      isRunning: () => this.container.running,
      ensureReady: () => this.ensurePortsReady(),
      ensureRestored: (readyTiming) => this.ensureInitialized(readyTiming),
      invalidate: (reason) => this.invalidate(reason),
      dispatch: (request) => this.dispatch(request),
      log: (message) => console.warn(`[worker] ${message}`),
    };
  }

  override onStop(_params: StopParams) {
    this.invalidate("container stopped");
    this.loggedMismatch = false;
  }

  override onError(error: unknown) {
    // A rollout exit arrives here, not in onStop.
    if (!this.container.running) this.invalidate(`container error: ${errorText(error)}`);
    return super.onError(error);
  }

  private invalidate(reason: string): void {
    if (this.restoreState.invalidate()) {
      console.warn(`[worker] restore required: ${reason}`);
    }
  }

  private async ensurePortsReady(): Promise<void> {
    // A container that is not running comes back empty; it must be restored before it serves anything.
    if (!this.container.running) this.invalidate("container is starting");
    await this.startAndWaitForPorts({
      ports: CONTAINER_PORT,
      cancellationOptions: { portReadyTimeoutMS: PORT_READY_TIMEOUT_MS },
    });
  }

  /**
   * Raw port fetch. Unlike the library's containerFetch it never starts a container, so a request can
   * only reach an instance that went through ensurePortsReady and the build check.
   */
  private async dispatch(request: Request): Promise<Response> {
    // Keeps sleepAfter from expiring while a request is in flight (containerFetch did this for us).
    this.renewActivityTimeout();
    try {
      return await this.ctx.container!.getTcpPort(CONTAINER_PORT).fetch(request);
    } finally {
      this.renewActivityTimeout();
    }
  }

  /** Internal build-check / restore calls; replayable because a failed restore is redone in full. */
  private containerFetchWithRetry(
    input: string,
    init: RequestInit | undefined,
    label: string,
    readyTiming?: ReadyTiming
  ): Promise<Response> {
    return fetchFromContainer(this.host, () => new Request(input, init), {
      label,
      method: init?.method ?? "GET",
      requireRestored: false,
      replayable: true,
      readyTiming,
      attempts: CONTAINER_RETRY_ATTEMPTS,
      baseDelayMs: CONTAINER_RETRY_BASE_DELAY_MS,
      sleep,
    });
  }

  private async initialize(readyTiming?: ReadyTiming): Promise<void> {
    const token = this.restoreState.current();
    watchContainerExit(this.ctx.container, this.restoreState, token, (m) => console.warn(`[worker] ${m}`));
    requirePersistentDatabase(this.env);
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
        "build-check",
        readyTiming
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

    // Commit only if the build check ran against the container that is running now.
    if (!this.container.running || !this.restoreState.commit(token)) {
      throw new StartupInterruptedError();
    }
  }

  /** Single-flight init with one full retry after a transient container death. */
  private async ensureInitialized(readyTiming?: ReadyTiming): Promise<void> {
    if (this.restored) return;

    const runOnce = async (): Promise<void> => {
      const p = (this.initPromise ??= this.initialize(readyTiming));
      try {
        await p;
      } finally {
        // Settled either way; a later invalidation must start a fresh restore.
        if (this.initPromise === p) {
          this.initPromise = null;
        }
      }
    };

    try {
      await runOnce();
    } catch (err) {
      if (!isStartupTransient(err)) {
        throw err;
      }
      console.warn(`[worker] initialize retry after transient failure: ${errorText(err)}`);
      await sleep(CONTAINER_RETRY_BASE_DELAY_MS);
      const readyStarted = Date.now();
      await this.ensurePortsReady();
      if (readyTiming) readyTiming.readyMs += Date.now() - readyStarted;
      await runOnce();
    }
  }

  override async fetch(request: Request): Promise<Response> {
    const url = new URL(request.url);
    const timingEnabled = isServerTimingEnabled(this.env);
    const workerStarted = Date.now();
    const cold = !this.restored;
    const scrubbedRoute = scrubPathForLog(url.pathname);
    const readyTiming: ReadyTiming = { readyMs: 0 };

    const finish = (response: Response, extra: TimingSegment[] = []): Response => {
      const workerMs = Date.now() - workerStarted;
      const segments: TimingSegment[] = [];
      if (readyTiming.readyMs > 0) {
        segments.push({ name: "ready", dur: readyTiming.readyMs });
      }
      segments.push(...extra, { name: "worker", dur: workerMs });
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
      this.invalidate("container restarted");
      this.loggedMismatch = false;
      return finish(
        new Response(JSON.stringify({ status: "container restarted" }), {
          status: 200,
          headers: { "content-type": "application/json" },
        })
      );
    }

    // 1. Wait for container to be ready (skip when already running + restored)
    if (!shouldSkipPortsReady(this.container.running, this.restored)) {
      try {
        const readyStarted = Date.now();
        await this.ensurePortsReady();
        readyTiming.readyMs += Date.now() - readyStarted;
      } catch (err) {
        console.error("[worker] Container startup timed out or failed:", err);
        return finish(serviceUnavailable("Service is starting up, please retry in a few seconds", 5));
      }
    }

    // 2. Build check (single-flight across concurrent requests)
    if (!this.restored) {
      try {
        await this.ensureInitialized(readyTiming);
      } catch (prepErr: unknown) {
        if (prepErr instanceof BuildMismatchError) {
          return finish(serviceUnavailable(prepErr.message, 5));
        }

        if (prepErr instanceof ContainerConfigError) {
          console.error(`[worker] ${prepErr.message}`);
          return finish(serviceUnavailable("Service is not configured, please try again later", 60));
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

    try {
      // Buffered so a retry resends the same bytes.
      const buffered = await bufferRequest(httpUrl, request, cleanHeaders);
      const containerStarted = Date.now();
      const containerRes = await fetchFromContainer(this.host, () => toRequest(buffered), {
        label: `proxy ${request.method} ${scrubbedRoute}`,
        method: request.method,
        requireRestored: true,
        readyTiming,
        attempts: CONTAINER_RETRY_ATTEMPTS,
        baseDelayMs: CONTAINER_RETRY_BASE_DELAY_MS,
        sleep,
      });
      const containerMs = Date.now() - containerStarted;

      if (containerRes.status === 101) {
        return containerRes;
      }

      return finish(containerRes, [{ name: "container", dur: containerMs }]);
    } catch (err) {
      // Never rethrow: uncaught DO exceptions become Issues "failed invocation" with no Retry-After.
      console.error(
        `[worker] Proxy failed method=${request.method} path=${scrubbedRoute}: ${errorText(err)}`
      );
      if (err instanceof ContainerUnavailableError) {
        return finish(unavailableResponse(err));
      }
      if (err instanceof BuildMismatchError) {
        return finish(serviceUnavailable(err.message, 5));
      }
      this.invalidate("proxy failed");
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
