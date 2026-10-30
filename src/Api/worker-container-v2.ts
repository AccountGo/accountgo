import { DurableObject } from "cloudflare:workers";
import { resolveContainerInstanceType } from "./container-capacity";
import { constantTimeEqual, parseSyncSqlHeaders } from "./sync-utils";
import {
  BuildMismatchError,
  RestoreError,
  RestoreInterruptedError,
  applyD1SyncFromResponse,
  buildContainerEnvVars,
  errorText,
  isRestoreTransient,
  isTransientContainerError,
  resolveInternalSyncKey,
  restoreD1ToContainer,
  usesD1Copy,
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
import {
  isServerTimingEnabled,
  logSlowWorkerRequest,
  scrubPathForLog,
  stripD1SyncHeaders,
  withServerTiming,
  type TimingSegment,
} from "./server-timing";
import type { Env } from "./worker-env";

const CONTAINER_PORT = 8001;
const PORT_READY_TIMEOUT_MS = 90_000;
const CONTAINER_RETRY_ATTEMPTS = 4;
const CONTAINER_RETRY_BASE_DELAY_MS = 750;
/** Match V1 sleepAfter = 15m so cold start + D1 restore does not thrash. */
const INACTIVITY_TIMEOUT_MS = 15 * 60 * 1000;
const PORT_POLL_INTERVAL_MS = 500;

/**
 * Phase 0 V2 writer: durable_object scheduling policy, instance size at start().
 * Still one shared writer per deploy version when routed from the Worker flag.
 * Does not extend @cloudflare/containers Container (unsupported for this policy).
 */
export class AccountGoApiContainerV2 extends DurableObject<Env> {
  private readonly restoreState = new RestoreState();
  private readonly host: ContainerHost;
  private initPromise: Promise<void> | null = null;
  private loggedMismatch = false;

  /** True only while the running container is the one the last D1 restore went into. */
  get restored(): boolean {
    return this.restoreState.restored;
  }

  constructor(ctx: DurableObjectState, env: Env) {
    super(ctx, env);
    const container = ctx.container;
    if (container?.running) {
      void ctx.blockConcurrencyWhile(() =>
        container.setInactivityTimeout(INACTIVITY_TIMEOUT_MS)
      );
    }
    const self = this;
    this.host = {
      get restored() {
        return self.restored;
      },
      isRunning: () => this.isContainerRunning(),
      ensureReady: () => this.ensurePortsReady(),
      ensureRestored: (readyTiming) => this.ensureInitialized(readyTiming),
      invalidate: (reason) => this.invalidate(reason),
      dispatch: (request) => this.dispatch(request),
      log: (message) => console.warn(`[worker-v2] ${message}`),
    };
  }

  private invalidate(reason: string): void {
    if (this.restoreState.invalidate()) {
      console.warn(`[worker-v2] restore required: ${reason}`);
    }
  }

  private buildEnvVars(): Record<string, string> {
    return buildContainerEnvVars(this.env as unknown as Record<string, unknown>, CONTAINER_PORT);
  }

  private async destroyContainer(reason?: string): Promise<void> {
    const container = this.ctx.container;
    if (!container) return;
    try {
      if (container.running) {
        await container.destroy(reason ?? "destroyed");
      }
    } catch (err) {
      console.error("[worker-v2] destroy failed:", errorText(err));
    }
    this.invalidate(reason ?? "destroyed");
    this.loggedMismatch = false;
  }

  private async ensureContainerStarted(): Promise<void> {
    const container = this.ctx.container;
    if (!container) {
      throw new Error("No container is configured for AccountGoApiContainerV2");
    }

    if (!container.running) {
      // A container that is not running comes back empty; it must be restored before it serves anything.
      this.invalidate("container is starting");
      const images = container.images;
      if (!images?.base) {
        throw new Error("Container image 'base' is not configured for AccountGoApiContainerV2");
      }
      const instance = resolveContainerInstanceType(this.env.CONTAINER_INSTANCE_TYPE);
      container.start({
        image: images.base,
        instance,
        enableInternet: true,
        env: this.buildEnvVars(),
      });
      await container.setInactivityTimeout(INACTIVITY_TIMEOUT_MS);
    }
  }

  private async waitForPortReady(): Promise<void> {
    const container = this.ctx.container;
    if (!container) {
      throw new Error("No container is configured for AccountGoApiContainerV2");
    }

    const deadline = Date.now() + PORT_READY_TIMEOUT_MS;
    let lastErr: unknown;
    while (Date.now() < deadline) {
      try {
        if (!container.running) {
          await this.ensureContainerStarted();
        }
        const port = container.getTcpPort(CONTAINER_PORT);
        // TCP accept is enough; ASP.NET may not expose Worker /healthz.
        const probe = await port.fetch(`http://127.0.0.1:${CONTAINER_PORT}/`, {
          method: "GET",
        });
        // Any HTTP response means the listener is up (including 404).
        void probe.body?.cancel();
        return;
      } catch (err) {
        lastErr = err;
        await sleep(PORT_POLL_INTERVAL_MS);
      }
    }
    throw lastErr instanceof Error
      ? lastErr
      : new Error(`Container port ${CONTAINER_PORT} not ready within ${PORT_READY_TIMEOUT_MS}ms`);
  }

  private async ensurePortsReady(): Promise<void> {
    await this.ensureContainerStarted();
    await this.waitForPortReady();
  }

  private isContainerRunning(): boolean {
    return !!this.ctx.container?.running;
  }

  /** Port fetch; never starts a container. */
  private async dispatch(request: Request): Promise<Response> {
    const container = this.ctx.container;
    if (!container) {
      throw new Error("No container is configured for AccountGoApiContainerV2");
    }
    return await container.getTcpPort(CONTAINER_PORT).fetch(request);
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
    watchContainerExit(this.ctx.container, this.restoreState, token, (m) =>
      console.warn(`[worker-v2] ${m}`)
    );
    const syncKey = resolveInternalSyncKey(this.env);

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
          `[worker-v2] Build check failed: status=${buildRes.status} body=${errText.slice(0, 300)}`
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
            `[worker-v2] BUILD_SHA mismatch: container=${containerSha}, expected=${expectedSha}. Destroying container.`
          );
        }
        await this.destroyContainer("build-mismatch");
        throw new BuildMismatchError("Container build version mismatch, reloading...");
      }
    }

    // On Postgres the container already has the data.
    if (usesD1Copy(this.env)) {
      await restoreD1ToContainer({
        db: this.env.DB,
        env: this.env,
        containerFetchWithRetry: (input, init, label) =>
          this.containerFetchWithRetry(String(input), init, label, readyTiming),
      });
    }

    // Commit only if every restore batch went into the container that is running now.
    if (!this.isContainerRunning() || !this.restoreState.commit(token)) {
      throw new RestoreInterruptedError();
    }
  }

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
      if (!isRestoreTransient(err)) {
        throw err;
      }
      console.warn(`[worker-v2] initialize retry after transient failure: ${errorText(err)}`);
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
        await this.destroyContainer("restart");
      } catch (err: unknown) {
        console.error("[worker-v2] Failed to destroy container on restart:", err);
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
      return finish(
        new Response(JSON.stringify({ status: "container restarted" }), {
          status: 200,
          headers: { "content-type": "application/json" },
        })
      );
    }

    if (!shouldSkipPortsReady(this.isContainerRunning(), this.restored)) {
      try {
        const readyStarted = Date.now();
        await this.ensurePortsReady();
        readyTiming.readyMs += Date.now() - readyStarted;
      } catch (err) {
        console.error("[worker-v2] Container startup timed out or failed:", err);
        return finish(serviceUnavailable("Service is starting up, please retry in a few seconds", 5));
      }
    }

    if (!this.restored) {
      try {
        await this.ensureInitialized(readyTiming);
      } catch (prepErr: unknown) {
        if (prepErr instanceof BuildMismatchError) {
          return finish(serviceUnavailable(prepErr.message, 5));
        }

        if (prepErr instanceof RestoreError) {
          const retryAfter = isRestoreTransient(prepErr) ? 5 : 8;
          return finish(serviceUnavailable(prepErr.message, retryAfter));
        }

        console.error("[worker-v2] Container initialization failed:", prepErr);
        return finish(serviceUnavailable("Service is starting up, please retry in a few seconds", 5));
      }
    }

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

      // 101 upgrades: pass through untouched (no header mutation, no body read).
      if (containerRes.status === 101) {
        return containerRes;
      }

      let syncStatements: string[] = [];
      try {
        syncStatements = await parseSyncSqlHeaders(containerRes.headers);
      } catch (headerErr: unknown) {
        const message = headerErr instanceof Error ? headerErr.message : String(headerErr);
        console.error(
          `[worker-v2] Failed to parse D1 sync headers: method=${request.method} path=${scrubbedRoute} error=${message}`
        );
        await this.destroyContainer("d1-header-error");
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
        await this.destroyContainer("d1-sync-failure");
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
      console.error(
        `[worker-v2] Proxy failed method=${request.method} path=${scrubbedRoute}: ${errorText(err)}`
      );
      if (err instanceof ContainerUnavailableError) {
        return finish(unavailableResponse(err));
      }
      if (err instanceof BuildMismatchError || err instanceof RestoreError) {
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
