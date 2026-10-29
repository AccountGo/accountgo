import test from "node:test";
import assert from "node:assert/strict";
import {
  appendServerTimingValue,
  formatServerTiming,
  isServerTimingEnabled,
  scrubPathForLog,
  stripD1SyncHeaders,
  withServerTiming,
} from "../server-timing.ts";

test("isServerTimingEnabled defaults true and respects false", () => {
  assert.equal(isServerTimingEnabled({}), true);
  assert.equal(isServerTimingEnabled({ SERVER_TIMING_ENABLED: "" }), true);
  assert.equal(isServerTimingEnabled({ SERVER_TIMING_ENABLED: "false" }), false);
  assert.equal(isServerTimingEnabled({ SERVER_TIMING_ENABLED: "0" }), false);
  assert.equal(isServerTimingEnabled({ SERVER_TIMING_ENABLED: "true" }), true);
});

test("appendServerTimingValue never overwrites existing segments", () => {
  const merged = appendServerTimingValue('app;dur=10, db;dur=2;desc="1 query"', [
    { name: "container", dur: 50 },
    { name: "worker", dur: 80 },
  ]);
  assert.match(merged, /app;dur=10/);
  assert.match(merged, /db;dur=2;desc="1 query"/);
  assert.match(merged, /container;dur=50/);
  assert.match(merged, /worker;dur=80/);
});

test("stripD1SyncHeaders keeps Server-Timing and removes only sync headers", () => {
  const headers = new Headers({
    "Server-Timing": "app;dur=5",
    "content-type": "application/json",
    "x-d1-sync-encoding": "gzip",
    "x-d1-sync-sql": "abc",
    "x-d1-sync-sql-0": "chunk",
    "x-d1-sync-sql-count": "1",
  });
  const stripped = stripD1SyncHeaders(headers);
  assert.equal(stripped.get("Server-Timing"), "app;dur=5");
  assert.equal(stripped.get("content-type"), "application/json");
  assert.equal(stripped.get("x-d1-sync-encoding"), null);
  assert.equal(stripped.get("x-d1-sync-sql"), null);
  assert.equal(stripped.get("x-d1-sync-sql-0"), null);
  assert.equal(stripped.get("x-d1-sync-sql-count"), null);
});

test("scrubPathForLog replaces ids, guids, hex hashes, and org slugs; drops query", () => {
  assert.equal(
    scrubPathForLog("/api/Sales/SalesInvoices/42?secret=1"),
    "/api/Sales/SalesInvoices/{id}"
  );
  assert.equal(
    scrubPathForLog("/api/users/550e8400-e29b-41d4-a716-446655440000"),
    "/api/users/{id}"
  );
  assert.equal(
    scrubPathForLog("/api/tokens/abcdef0123456789abcdef0123456789"),
    "/api/tokens/{id}"
  );
  assert.equal(scrubPathForLog("/org/acme-corp/invoices/9"), "/org/{org}/invoices/{id}");
});

test("scrubPathForLog redacts invitation tokens and high-entropy segments", () => {
  const inviteToken = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-xyz";
  assert.equal(
    scrubPathForLog(`/api/invitations/${inviteToken}`),
    "/api/invitations/{token}"
  );
  assert.equal(
    scrubPathForLog(`/api/invitations/${inviteToken}/accept`),
    "/api/invitations/{token}/accept"
  );
  // 43-char base64url (typical opaque token length) with letter + digit
  assert.equal(
    scrubPathForLog("/api/misc/AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-xyzab"),
    "/api/misc/{token}"
  );
  // Normal route words (letter+digit alone is not enough without ≥16 entropy charset match,
  // and these have no digits / are below the catch or are dictionary path segments).
  assert.equal(scrubPathForLog("/api/Sales/SalesInvoices"), "/api/Sales/SalesInvoices");
  assert.equal(
    scrubPathForLog("/api/company/accounting-policy"),
    "/api/company/accounting-policy"
  );
  assert.equal(
    scrubPathForLog("/api/settings/business-profile"),
    "/api/settings/business-profile"
  );
});

test("withServerTiming wraps body without consuming it; 101 passes through", async () => {
  const chunks = ["a", "b", "c"];
  let pullCount = 0;
  const body = new ReadableStream({
    pull(controller) {
      if (pullCount < chunks.length) {
        controller.enqueue(new TextEncoder().encode(chunks[pullCount++]));
      } else {
        controller.close();
      }
    },
  });

  const original = new Response(body, {
    status: 200,
    headers: { "Server-Timing": "app;dur=1", "content-type": "text/event-stream" },
  });

  const wrapped = withServerTiming(
    original,
    [
      { name: "container", dur: 9 },
      { name: "worker", dur: 12 },
    ],
    true
  );

  assert.equal(wrapped.status, 200);
  assert.match(wrapped.headers.get("Server-Timing") ?? "", /app;dur=1/);
  assert.match(wrapped.headers.get("Server-Timing") ?? "", /container;dur=9/);
  assert.match(wrapped.headers.get("Server-Timing") ?? "", /worker;dur=12/);

  // Incremental read: first chunk available before the stream ends.
  const reader = wrapped.body.getReader();
  const first = await reader.read();
  assert.equal(first.done, false);
  assert.equal(new TextDecoder().decode(first.value), "a");
  const second = await reader.read();
  assert.equal(new TextDecoder().decode(second.value), "b");
  await reader.cancel();

  // Node's Response rejects status 101; patch status to exercise the Workers upgrade path.
  const switching = new Response(null, { status: 200, statusText: "Switching Protocols" });
  Object.defineProperty(switching, "status", { value: 101 });
  const passthrough = withServerTiming(switching, [{ name: "worker", dur: 1 }], true);
  assert.equal(passthrough, switching);
});

test("formatServerTiming rounds durations", () => {
  assert.equal(formatServerTiming([{ name: "cold", desc: "1" }]), 'cold;desc="1"');
  assert.equal(formatServerTiming([{ name: "d1", dur: 3.7, desc: "2 stmts" }]), 'd1;dur=4;desc="2 stmts"');
});
