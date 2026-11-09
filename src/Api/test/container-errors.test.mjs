import test from "node:test";
import assert from "node:assert";
import {
  isTransientContainerError,
  errorText,
  serviceUnavailable,
  startupCode,
} from "../container-errors.ts";

test("isTransientContainerError matches observed Cloudflare proxy races", () => {
  assert.equal(
    isTransientContainerError(
      new Error("Error proxying request to container: The container is not running, consider calling start()")
    ),
    true
  );
  assert.equal(
    isTransientContainerError(new Error("Error proxying request to container: The container just exited")),
    true
  );
  assert.equal(
    isTransientContainerError(
      "Database restore from Cloudflare D1 failed: Restore batch 1 failed (500): Error proxying request to container: The container is not running"
    ),
    true
  );
});

test("isTransientContainerError rejects unrelated failures", () => {
  assert.equal(isTransientContainerError(new Error("INTERNAL_SYNC_KEY is not configured.")), false);
  assert.equal(isTransientContainerError(new Error("Batch count mismatch: restored 1 of 2")), false);
});

test("errorText formats Error instances", () => {
  assert.equal(errorText(new TypeError("boom")), "TypeError: boom");
  assert.equal(errorText("plain"), "plain");
});

test("serviceUnavailable returns 503 problem+json with typed code and retryAfterSeconds", async () => {
  const res = serviceUnavailable("SERVICE_UPDATING", "Deploy in progress", 10);
  assert.equal(res.status, 503);
  assert.equal(res.headers.get("content-type"), "application/problem+json");
  assert.equal(res.headers.get("Retry-After"), "10");

  const body = await res.json();
  assert.deepEqual(body, {
    title: "Service Unavailable",
    status: 503,
    code: "SERVICE_UPDATING",
    detail: "Deploy in progress",
    retryAfterSeconds: 10,
  });
});

test("startupCode table: recent, old, missing and garbage timestamps", () => {
  const now = Date.parse("2026-11-09T12:00:00Z");
  const table = [
    // 5 minutes ago (< 20 min) -> UPDATING
    { timestamp: "2026-11-09T11:55:00Z", expected: "SERVICE_UPDATING" },
    // Exactly at window boundary (20 minutes ago) -> UPDATING
    { timestamp: "2026-11-09T11:40:00Z", expected: "SERVICE_UPDATING" },
    // Just deployed (now) -> UPDATING
    { timestamp: "2026-11-09T12:00:00Z", expected: "SERVICE_UPDATING" },
    // 21 minutes ago (> 20 min) -> STARTING
    { timestamp: "2026-11-09T11:39:00Z", expected: "SERVICE_STARTING" },
    // Hours ago -> STARTING
    { timestamp: "2026-11-09T08:00:00Z", expected: "SERVICE_STARTING" },
    // Missing (undefined) -> STARTING
    { timestamp: undefined, expected: "SERVICE_STARTING" },
    // Empty string -> STARTING
    { timestamp: "", expected: "SERVICE_STARTING" },
    // Garbage unparseable -> STARTING
    { timestamp: "not-a-date", expected: "SERVICE_STARTING" },
    { timestamp: "invalid_iso_xyz", expected: "SERVICE_STARTING" },
  ];

  for (const { timestamp, expected } of table) {
    const code = startupCode({ versionTimestamp: timestamp, now });
    assert.equal(
      code,
      expected,
      `Expected ${expected} for timestamp "${timestamp}", got ${code}`
    );
  }

  // Custom windowMs (e.g. 5 minutes)
  assert.equal(
    startupCode({
      versionTimestamp: "2026-11-09T11:54:00Z",
      now,
      windowMs: 5 * 60_000,
    }),
    "SERVICE_STARTING" // 6 minutes ago > 5 min window
  );
  assert.equal(
    startupCode({
      versionTimestamp: "2026-11-09T11:56:00Z",
      now,
      windowMs: 5 * 60_000,
    }),
    "SERVICE_UPDATING" // 4 minutes ago <= 5 min window
  );
});
