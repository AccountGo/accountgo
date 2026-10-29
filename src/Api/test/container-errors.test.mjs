import test from "node:test";
import assert from "node:assert";
import { isTransientContainerError, errorText } from "../container-errors.ts";

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
