/**
 * Warm path: skip redundant startAndWaitForPorts / port probes when the container is
 * already running and D1 has been restored. A container that is not running is only
 * started through readiness, which invalidates the restore first (container-proxy.ts).
 */
export function shouldSkipPortsReady(running: boolean, restored: boolean): boolean {
  return running && restored;
}

/** First attempt may skip when warm; retries always re-check after a transient error. */
export function shouldEnsurePortsReadyOnAttempt(
  attempt: number,
  running: boolean,
  restored: boolean
): boolean {
  return attempt > 1 || !shouldSkipPortsReady(running, restored);
}

export type ReadyTiming = { readyMs: number };
