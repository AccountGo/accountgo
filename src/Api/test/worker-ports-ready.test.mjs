import test from "node:test";
import assert from "node:assert/strict";
import {
  shouldEnsurePortsReadyOnAttempt,
  shouldSkipPortsReady,
} from "../ports-ready.ts";
import { formatServerTiming } from "../server-timing.ts";

test("fast path: skip readiness when running and restored", () => {
  assert.equal(shouldSkipPortsReady(true, true), true);
  assert.equal(shouldEnsurePortsReadyOnAttempt(1, true, true), false);
});

test("readiness still runs when container is not running", () => {
  assert.equal(shouldSkipPortsReady(false, true), false);
  assert.equal(shouldEnsurePortsReadyOnAttempt(1, false, true), true);
  assert.equal(shouldEnsurePortsReadyOnAttempt(1, false, false), true);
});

test("readiness still runs when not restored (cold / initialize)", () => {
  assert.equal(shouldSkipPortsReady(true, false), false);
  assert.equal(shouldEnsurePortsReadyOnAttempt(1, true, false), true);
});

test("retry attempts always ensure ports ready after a transient error", () => {
  // Warm first attempt skips; attempt 2+ always re-checks even if still warm.
  assert.equal(shouldEnsurePortsReadyOnAttempt(1, true, true), false);
  assert.equal(shouldEnsurePortsReadyOnAttempt(2, true, true), true);
  assert.equal(shouldEnsurePortsReadyOnAttempt(3, true, true), true);
  assert.equal(shouldEnsurePortsReadyOnAttempt(2, false, false), true);
});

test("Server-Timing ready segment is absent on fast path (readyMs=0) and present when measured", () => {
  const warmSegments = [{ name: "container", dur: 40 }, { name: "worker", dur: 55 }];
  assert.doesNotMatch(formatServerTiming(warmSegments), /ready/);

  const coldSegments = [
    { name: "ready", dur: 312 },
    { name: "container", dur: 80 },
    { name: "worker", dur: 400 },
  ];
  assert.match(formatServerTiming(coldSegments), /ready;dur=312/);
});

/**
 * Mirrors worker / worker-container-v2 containerFetchWithRetry gate + fetch() skip.
 */
async function simulatePortsReadyGate(opts) {
  const { running, restored, failFirstAttempt = false } = opts;
  let ensureCalls = 0;
  const readyTiming = { readyMs: 0 };

  // fetch() top-level gate
  if (!shouldSkipPortsReady(running, restored)) {
    ensureCalls += 1;
    readyTiming.readyMs += 1;
  }

  for (let attempt = 1; attempt <= 4; attempt++) {
    try {
      if (shouldEnsurePortsReadyOnAttempt(attempt, running, restored)) {
        ensureCalls += 1;
        readyTiming.readyMs += 1;
      }
      if (failFirstAttempt && attempt === 1) {
        throw new Error(
          "Error proxying request to container: The container just exited"
        );
      }
      return { ensureCalls, readyMs: readyTiming.readyMs };
    } catch {
      if (attempt === 4) throw new Error("exhausted");
    }
  }
  return { ensureCalls, readyMs: readyTiming.readyMs };
}

test("simulated fetch+retry: warm path skips all readiness (readyMs absent/0)", async () => {
  const result = await simulatePortsReadyGate({ running: true, restored: true });
  assert.equal(result.ensureCalls, 0);
  assert.equal(result.readyMs, 0);
});

test("simulated fetch+retry: not running still ensures once on first path", async () => {
  const result = await simulatePortsReadyGate({ running: false, restored: true });
  // fetch() + attempt 1
  assert.equal(result.ensureCalls, 2);
  assert.ok(result.readyMs > 0);
});

test("simulated fetch+retry: not restored still ensures (cold/initialize)", async () => {
  const result = await simulatePortsReadyGate({ running: true, restored: false });
  assert.equal(result.ensureCalls, 2);
  assert.ok(result.readyMs > 0);
});

test("simulated fetch+retry: warm path re-checks readiness on retry after transient error", async () => {
  const result = await simulatePortsReadyGate({
    running: true,
    restored: true,
    failFirstAttempt: true,
  });
  // attempt 1 skipped; attempt 2 ensures
  assert.equal(result.ensureCalls, 1);
  assert.ok(result.readyMs > 0);
});
