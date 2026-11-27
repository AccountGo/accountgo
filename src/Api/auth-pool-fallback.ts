// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

import {
  CAPACITY_POOL_HEADER,
  type WarmupThrottle,
} from "./container-capacity.ts";
import {
  errorText,
  isNotDispatchedUnavailable,
  serviceUnavailable,
} from "./container-errors.ts";
import { scrubPathForLog } from "./server-timing.ts";

/** Response header added when a pre-dispatch V2 503 fell back to V1. */
export const AUTH_POOL_HEADER = "x-accountgo-auth-pool";
export const AUTH_POOL_V1_FALLBACK = "v1-fallback";

/** Maximum request body buffered for V2 → V1 auth fallback (64 KiB). Larger bodies skip fallback. */
export const MAX_AUTH_FALLBACK_BODY_BYTES = 64 * 1024;

const AUTH_WARMUP_TRIGGER_PATHS = new Set([
  "/api/auth/send-otp",
  "/api/auth/google/start",
]);

export function isAuthWarmupTriggerRequest(path: string): boolean {
  const normalized = path.split(/[?#]/)[0].replace(/\/+/g, "/").replace(/\/+$/, "").toLowerCase();
  return AUTH_WARMUP_TRIGGER_PATHS.has(normalized);
}

export interface FetchStub {
  fetch(request: Request): Promise<Response>;
}

export interface RoutePublicAuthOptions {
  request: Request;
  url: URL;
  v2Enabled: boolean;
  getV2Stub: () => FetchStub;
  getV1Stub: () => FetchStub;
  waitUntil: (promise: Promise<unknown>) => void;
  warmupThrottle: WarmupThrottle;
  maxFallbackBodyBytes?: number;
  logWarn?: (message: string) => void;
}

type PreparedBody =
  | { canFallback: true; body: ArrayBuffer | null }
  | { canFallback: false; body: BodyInit | null };

async function prepareBodyForFallback(
  request: Request,
  maxBytes: number
): Promise<PreparedBody> {
  const method = request.method.toUpperCase();
  if (method === "GET" || method === "HEAD" || request.body === null) {
    return { canFallback: true, body: null };
  }

  const contentLength = request.headers.get("content-length");
  if (contentLength !== null) {
    const parsed = parseInt(contentLength, 10);
    if (!Number.isNaN(parsed) && parsed > maxBytes) {
      return { canFallback: false, body: request.body };
    }
  }

  const reader = request.body.getReader();
  const chunks: Uint8Array[] = [];
  let totalBytes = 0;

  while (true) {
    const { done, value } = await reader.read();
    if (done) break;
    if (!value || value.byteLength === 0) continue;
    chunks.push(value);
    totalBytes += value.byteLength;
    if (totalBytes > maxBytes) {
      // Body exceeded the fallback cap without a declared Content-Length (chunked stream).
      // Reconstruct a single-use stream for V2 without buffering the rest and skip fallback.
      const passthrough = new ReadableStream<Uint8Array>({
        start(controller) {
          for (const chunk of chunks) {
            controller.enqueue(chunk);
          }
        },
        async pull(controller) {
          const next = await reader.read();
          if (next.done) {
            controller.close();
            return;
          }
          if (next.value) controller.enqueue(next.value);
        },
        cancel(reason) {
          return reader.cancel(reason);
        },
      });
      return { canFallback: false, body: passthrough };
    }
  }

  const combined = new Uint8Array(totalBytes);
  let offset = 0;
  for (const chunk of chunks) {
    combined.set(chunk, offset);
    offset += chunk.byteLength;
  }
  return { canFallback: true, body: combined.buffer };
}

async function readUnavailableCode(res: Response): Promise<string> {
  try {
    const parsed = (await res.json()) as { code?: unknown } | null;
    if (parsed && typeof parsed.code === "string" && parsed.code.length > 0) {
      return parsed.code;
    }
  } catch {
    // Body may not be JSON; fall back to unknown code.
  }
  return "UNKNOWN";
}

function withFallbackHeader(res: Response): Response {
  if (res.status === 101) return res;
  const headers = new Headers(res.headers);
  headers.set(AUTH_POOL_HEADER, AUTH_POOL_V1_FALLBACK);
  return new Response(res.body, {
    status: res.status,
    statusText: res.statusText,
    headers,
  });
}

function triggerAuthWarmup(
  url: URL,
  v2Stub: FetchStub,
  waitUntil: (promise: Promise<unknown>) => void,
  warmupThrottle: WarmupThrottle,
  logWarn: (message: string) => void
): void {
  if (!isAuthWarmupTriggerRequest(url.pathname) || !warmupThrottle.shouldRun()) {
    return;
  }
  const warmupUrl = new URL("/health", url.origin);
  const warmupRequest = new Request(warmupUrl.toString(), {
    method: "GET",
    headers: { [CAPACITY_POOL_HEADER]: "paid" },
  });
  try {
    const promise = Promise.resolve(v2Stub.fetch(warmupRequest)).then(
      (res) => res.body?.cancel(),
      (err) => logWarn(`[worker] auth pool warm-up failed: ${errorText(err)}`)
    );
    waitUntil(promise);
  } catch (err) {
    logWarn(`[worker] auth pool warm-up failed: ${errorText(err)}`);
  }
}

/**
 * Routes a public pre-workspace auth request.
 * - When `v2Enabled` is false: sends directly to V1 (existing routing unchanged).
 * - When `v2Enabled` is true: sends to the V2 paid pool, firing a non-blocking throttled warm-up
 *   on `/api/auth/send-otp` and `/api/auth/google/start`. Replays once on V1 ONLY when V2 returns
 *   a 503 carrying `x-accountgo-not-dispatched: 1` and the body fit within the fallback cap.
 */
export async function routePublicAuthRequest(opts: RoutePublicAuthOptions): Promise<Response> {
  const {
    request,
    url,
    v2Enabled,
    getV2Stub,
    getV1Stub,
    waitUntil,
    warmupThrottle,
    maxFallbackBodyBytes = MAX_AUTH_FALLBACK_BODY_BYTES,
    logWarn = (msg) => console.warn(msg),
  } = opts;

  if (!v2Enabled) {
    return getV1Stub().fetch(request);
  }

  const v2Stub = getV2Stub();
  triggerAuthWarmup(url, v2Stub, waitUntil, warmupThrottle, logWarn);

  const prepared = await prepareBodyForFallback(request, maxFallbackBodyBytes);
  const v2Headers = new Headers(request.headers);
  v2Headers.set(CAPACITY_POOL_HEADER, "paid");

  const v2Init: RequestInit & { duplex?: "half" } = {
    method: request.method,
    headers: v2Headers,
    body: prepared.body,
    redirect: "manual",
  };
  if (prepared.body instanceof ReadableStream) {
    v2Init.duplex = "half";
  }

  let v2Res: Response;
  try {
    v2Res = await v2Stub.fetch(new Request(request.url, v2Init));
  } catch (err) {
    // Per Cloudflare Durable Objects semantics, a thrown stub.fetch() error (e.g. network
    // connection lost, DO reset/eviction, or overloaded) does NOT guarantee the DO's fetch()
    // never dispatched the request to the container. Because auth endpoints (verify-otp,
    // assertion/complete, refresh) are not idempotent, do not replay on V1.
    logWarn(
      `[worker] auth V2 stub error path=${scrubPathForLog(url.pathname)}: ${errorText(err)}`
    );
    return serviceUnavailable(
      "SERVICE_INTERRUPTED",
      "Upstream authentication error, please retry",
      5
    );
  }

  if (!prepared.canFallback || !isNotDispatchedUnavailable(v2Res)) {
    return v2Res;
  }

  const v2Code = await readUnavailableCode(v2Res);
  logWarn(`[worker] auth fallback to V1 path=${scrubPathForLog(url.pathname)} code=${v2Code}`);

  const v1Headers = new Headers(request.headers);
  v1Headers.delete(CAPACITY_POOL_HEADER);
  const v1Request = new Request(request.url, {
    method: request.method,
    headers: v1Headers,
    body: prepared.body,
    redirect: "manual",
  });

  const v1Res = await getV1Stub().fetch(v1Request);
  return withFallbackHeader(v1Res);
}
