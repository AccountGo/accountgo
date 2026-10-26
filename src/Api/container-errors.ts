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
