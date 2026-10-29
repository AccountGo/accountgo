// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

/**
 * Container request dispatch shared by the V1 and V2 writers.
 *
 * Invariant: a request is only dispatched while `restored` is true, and `restored` is only true for the
 * container instance the D1 restore ran against:
 * - RestoreState hands out a token when a restore starts and accepts the commit only if nothing
 *   invalidated it in between.
 * - Starting a container, a container exit (watchContainerExit), onStop/onError and every transient
 *   failure invalidate.
 * - dispatch() is a raw port fetch that never starts a container, so the only way to get a new
 *   instance is the host's ensureReady(), which invalidates before starting one.
 * - fetchFromContainer() checks `restored && running` synchronously right before each dispatch and runs
 *   readiness + restore first when either is false.
 */
import {
  errorText,
  failureReach,
  isTransientContainerError,
  libraryFailureText,
  serviceUnavailable,
  type FailureReach,
} from "./container-errors.ts";
import { shouldEnsurePortsReadyOnAttempt, type ReadyTiming } from "./ports-ready.ts";

export class RestoreState {
  private epoch = 0;
  private restoredEpoch = -1;

  get restored(): boolean {
    return this.restoredEpoch === this.epoch;
  }

  /** Token for a restore that is about to start. */
  current(): number {
    return this.epoch;
  }

  /** Returns true when this cleared a completed restore. */
  invalidate(): boolean {
    const wasRestored = this.restored;
    this.epoch++;
    return wasRestored;
  }

  invalidateIf(token: number): void {
    if (token === this.epoch) this.epoch++;
  }

  /** Marks the restore for `token` complete; false when something invalidated it meanwhile. */
  commit(token: number): boolean {
    if (token !== this.epoch) return false;
    this.restoredEpoch = token;
    return true;
  }
}

/**
 * Invalidates `token` when the running container exits for any reason. The library reports some exits
 * (such as a version rollout) only through onError and never calls onStop. A monitor that settles while
 * the container is still running is not an exit and is only logged, so it can never force a restore loop.
 */
export function watchContainerExit(
  container: { readonly running: boolean; monitor(): Promise<void> } | undefined,
  state: RestoreState,
  token: number,
  log: (message: string) => void
): void {
  const settled = (why: string) => {
    if (state.current() !== token) return;
    if (container?.running) {
      log(`container monitor settled while running (${why}); not treated as an exit`);
      return;
    }
    state.invalidateIf(token);
    log(`container exited (${why}); restore required before the next request`);
  };
  let exited: Promise<void>;
  try {
    if (!container) throw new Error("no container");
    exited = container.monitor();
  } catch (err) {
    settled(errorText(err));
    return;
  }
  void exited.then(
    () => settled("exited"),
    (err: unknown) => settled(errorText(err))
  );
}

export interface ContainerHost {
  readonly restored: boolean;
  isRunning(): boolean;
  /** Start the container if needed and wait for the port; invalidates before starting a new instance. */
  ensureReady(): Promise<void>;
  /** Build check + D1 restore (single-flight). Throws the writer's restore errors. */
  ensureRestored(readyTiming?: ReadyTiming): Promise<void>;
  invalidate(reason: string): void;
  /** Port fetch that never starts a container. */
  dispatch(request: Request): Promise<Response>;
  log(message: string): void;
}

export class ContainerUnavailableError extends Error {
  readonly reach: FailureReach;

  constructor(message: string, reach: FailureReach) {
    super(message);
    this.name = "ContainerUnavailableError";
    this.reach = reach;
  }
}

export function unavailableResponse(err: ContainerUnavailableError): Response {
  return serviceUnavailable(
    err.reach === "not-reached"
      ? "Service is starting up, please retry in a few seconds"
      : "The service restarted while handling this request, please retry",
    5
  );
}

export type BufferedRequest = {
  url: string;
  method: string;
  headers: Headers;
  body: ArrayBuffer | null;
};

/** Reads the body once so every attempt can resend the same bytes. */
export async function bufferRequest(
  url: string,
  request: Request,
  headers: Headers
): Promise<BufferedRequest> {
  const method = request.method.toUpperCase();
  const body =
    method === "GET" || method === "HEAD" || request.body === null
      ? null
      : await request.arrayBuffer();
  return { url, method, headers, body };
}

export function toRequest(buffered: BufferedRequest): Request {
  return new Request(buffered.url, {
    method: buffered.method,
    headers: new Headers(buffered.headers),
    body: buffered.body,
    redirect: "manual",
  });
}

export type FetchFromContainerOptions = {
  label: string;
  method: string;
  /** Proxied app requests; internal build-check / restore calls run before the restore commits. */
  requireRestored: boolean;
  /** Resend after a failure that may have reached the app. Defaults to GET/HEAD only. */
  replayable?: boolean;
  readyTiming?: ReadyTiming;
  attempts: number;
  baseDelayMs: number;
  sleep: (ms: number) => Promise<void>;
};

/**
 * Sends `build()` to the container. A library 500 or a thrown transient error invalidates the restore,
 * then retries when the request is replayable or provably never reached the app. Otherwise, and when
 * attempts run out, throws ContainerUnavailableError. App responses (including app 500s) pass through.
 */
export async function fetchFromContainer(
  host: ContainerHost,
  build: () => Request,
  opts: FetchFromContainerOptions
): Promise<Response> {
  const method = opts.method.toUpperCase();
  const replayable = opts.replayable ?? (method === "GET" || method === "HEAD");

  for (let attempt = 1; ; attempt++) {
    let failure: unknown;
    let dispatched = false;
    try {
      if (shouldEnsurePortsReadyOnAttempt(attempt, host.isRunning(), host.restored)) {
        const readyStarted = Date.now();
        await host.ensureReady();
        if (opts.readyTiming) opts.readyTiming.readyMs += Date.now() - readyStarted;
      }
    } catch (err) {
      if (!isTransientContainerError(err)) throw err;
      failure = err;
    }

    if (failure === undefined && opts.requireRestored && !host.restored) {
      await host.ensureRestored(opts.readyTiming);
    }

    if (failure === undefined && opts.requireRestored && !(host.restored && host.isRunning())) {
      failure = new Error("The container is not ready: it changed before the restore finished");
    }

    if (failure === undefined) {
      dispatched = true;
      try {
        const res = await host.dispatch(build());
        const libraryFailure = await libraryFailureText(res);
        if (libraryFailure === null) return res;
        failure = libraryFailure;
      } catch (err) {
        if (!isTransientContainerError(err)) throw err;
        failure = err;
      }
    }

    host.invalidate(`${opts.label} failed`);
    const reach: FailureReach = dispatched ? failureReach(failure) : "not-reached";
    const retry = attempt < opts.attempts && (replayable || reach === "not-reached");
    host.log(
      `${opts.label} attempt ${attempt}/${opts.attempts} failed transient=true reach=${reach}${
        retry ? "" : " giving up"
      }: ${errorText(failure).slice(0, 300)}`
    );
    if (!retry) {
      throw new ContainerUnavailableError(errorText(failure), reach);
    }
    await opts.sleep(opts.baseDelayMs * attempt);
  }
}
