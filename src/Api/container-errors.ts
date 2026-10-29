// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

/** Normalize unknown thrown values for logging / matching. */
export function errorText(err: unknown): string {
  if (err instanceof Error) return `${err.name}: ${err.message}`;
  return String(err);
}

/**
 * Cloudflare Containers often surfaces these while the process is still coming up
 * or was just recycled (cold start / D1 restore races).
 */
export function isTransientContainerError(err: unknown): boolean {
  const msg = errorText(err).toLowerCase();
  return (
    msg.includes("not running") ||
    msg.includes("just exited") ||
    msg.includes("error proxying request") ||
    msg.includes("container is not ready") ||
    msg.includes("no container") ||
    msg.includes("connection refused") ||
    msg.includes("econnreset") ||
    msg.includes("econnrefused") ||
    msg.includes("socket") ||
    msg.includes("broken pipe") ||
    msg.includes("timed out") ||
    msg.includes("timeout") ||
    msg.includes("network connection lost")
  );
}

/**
 * Bodies of the 500 responses @cloudflare/containers returns instead of throwing when it cannot reach
 * the container. An app 500 never starts with these.
 */
export const LIBRARY_FAILURE_PREFIXES = [
  "Failed to start container",
  "Error proxying request to container",
  "Container suddenly disconnected",
] as const;

/** The library failure text of a 500 response (read from a clone), or null for any other response. */
export async function libraryFailureText(res: Response): Promise<string | null> {
  if (res.status !== 500) return null;
  const text = await res.clone().text().catch(() => "");
  return LIBRARY_FAILURE_PREFIXES.some((p) => text.startsWith(p)) ? text : null;
}

/**
 * "not-reached": the failure proves the app never saw the request (the container failed to start, was
 * not running, not ready, or refused the connection), so any method can be resent.
 * "maybe-reached": the connection broke after it may have been delivered (disconnects, resets, timeouts,
 * a container that exited mid-request).
 */
export type FailureReach = "not-reached" | "maybe-reached";

export function failureReach(failure: unknown): FailureReach {
  const msg = errorText(failure).toLowerCase();
  if (
    msg.startsWith("failed to start container") ||
    msg.includes("not running") ||
    msg.includes("not ready") ||
    msg.includes("no container") ||
    msg.includes("connection refused") ||
    msg.includes("econnrefused")
  ) {
    return "not-reached";
  }
  return "maybe-reached";
}

export function serviceUnavailable(detail: string, retryAfterSec = 5): Response {
  return new Response(
    JSON.stringify({
      title: "Service Unavailable",
      status: 503,
      detail,
    }),
    {
      status: 503,
      headers: {
        "content-type": "application/problem+json",
        "Retry-After": String(retryAfterSec),
      },
    }
  );
}
