// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

/**
 * Container request dispatch shared by the V1 and V2 writers.
 *
 * Invariant: a request is only dispatched while `restored` is true, and `restored` is only true for the
 * container instance the start-up (build) check ran against:
 * - RestoreState hands out a token when the check starts and accepts the commit only if nothing
 *   invalidated it in between.
 * - Starting a container, a container exit (watchContainerExit), onStop/onError and a transient
 *   failure that leaves the container not running invalidate. A failure on a container that is still
 *   running keeps its check.
 * - dispatch() is a raw port fetch that never starts a container, so the only way to get a new
 *   instance is the host's ensureReady(), which invalidates before starting one.
 * - fetchFromContainer() checks `restored && running` synchronously right before each dispatch and runs
 *   readiness + the check first when either is false.
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
  /** Build check (single-flight). Throws the writer's start-up errors. */
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

export class PayloadTooLargeError extends Error {
  readonly maxBytes: number;

  constructor(message: string, maxBytes: number) {
    super(message);
    this.name = "PayloadTooLargeError";
    this.maxBytes = maxBytes;
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

export function payloadTooLargeResponse(err: PayloadTooLargeError): Response {
  return new Response(
    JSON.stringify({
      title: "Payload Too Large",
      status: 413,
      detail: err.message,
    }),
    {
      status: 413,
      headers: { "content-type": "application/problem+json" },
    }
  );
}

export const MAX_LOGO_FILE_BYTES = 2 * 1024 * 1024;
export const MAX_LOGO_REQUEST_BYTES = 2 * 1024 * 1024 + 64 * 1024; // 2 MiB file + 64 KiB multipart overhead

export function canonicalizePath(pathname: string): string {
  let path = pathname.split(/[?#]/)[0];
  for (let i = 0; i < 3; i++) {
    if (!path.includes("%")) break;
    try {
      const decoded = decodeURIComponent(path);
      if (decoded === path) break;
      path = decoded;
    } catch {
      path = path.replace(/%(?![0-9a-fA-F]{2})/g, "%25");
      try {
        path = decodeURIComponent(path);
      } catch {
        break;
      }
    }
  }
  return path.replace(/\/+/g, "/").replace(/\/+$/, "").toLowerCase();
}

export function isInvoiceLogoRoute(pathname: string): boolean {
  const raw = pathname.split(/[?#]/)[0].replace(/\/+/g, "/").replace(/\/+$/, "").toLowerCase();
  const canonical = canonicalizePath(pathname);
  return raw === "/api/invoice-branding/logo" || canonical === "/api/invoice-branding/logo";
}

export function maxRequestBodyBytesForRoute(pathname: string, method?: string): number | undefined {
  if (method) {
    const m = method.toUpperCase();
    if (m === "GET" || m === "HEAD" || m === "DELETE" || m === "OPTIONS") {
      return undefined;
    }
  }
  if (isInvoiceLogoRoute(pathname)) {
    return MAX_LOGO_REQUEST_BYTES;
  }
  return undefined;
}

export type BufferedRequest = {
  url: string;
  method: string;
  headers: Headers;
  body: ArrayBuffer | null;
};

export async function readBodyWithLimit(
  bodyStream: ReadableStream<Uint8Array>,
  maxBytes: number
): Promise<ArrayBuffer> {
  const reader = bodyStream.getReader();
  const chunks: Uint8Array[] = [];
  let totalBytes = 0;
  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      if (value) {
        totalBytes += value.byteLength;
        if (totalBytes > maxBytes) {
          try {
            await reader.cancel();
          } catch {}
          throw new PayloadTooLargeError(
            `Request body exceeds maximum allowed size of ${maxBytes} bytes`,
            maxBytes
          );
        }
        chunks.push(value);
      }
    }
  } catch (err) {
    throw err;
  }
  const result = new Uint8Array(totalBytes);
  let offset = 0;
  for (const chunk of chunks) {
    result.set(chunk, offset);
    offset += chunk.byteLength;
  }
  return result.buffer;
}

/** Reads the body once so every attempt can resend the same bytes. */
export async function bufferRequest(
  url: string,
  request: Request,
  headers: Headers,
  options?: { maxBytes?: number }
): Promise<BufferedRequest> {
  const method = request.method.toUpperCase();
  if (method === "GET" || method === "HEAD" || request.body === null) {
    return { url, method, headers, body: null };
  }

  const maxBytes = options?.maxBytes;
  if (maxBytes !== undefined) {
    const contentLength = headers.get("content-length") || request.headers.get("content-length");
    if (contentLength) {
      const parsed = parseInt(contentLength, 10);
      if (!isNaN(parsed) && parsed > maxBytes) {
        if (request.body) {
          try {
            await request.body.cancel();
          } catch {}
        }
        throw new PayloadTooLargeError(
          `Request body exceeds maximum allowed size of ${maxBytes} bytes`,
          maxBytes
        );
      }
    }

    const body = await readBodyWithLimit(request.body, maxBytes);
    return { url, method, headers, body };
  }

  const body = await request.arrayBuffer();
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
 * Sends `build()` to the container. After a library 500 or a thrown transient error (invalidating the
 * restore only if the container is no longer running), retries when the request is replayable or provably never reached the app. Otherwise, and when
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

    // Only a container that is gone loses its check; real exits and new starts invalidate on their own.
    if (!host.isRunning()) host.invalidate(`${opts.label} failed: container is not running`);
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
