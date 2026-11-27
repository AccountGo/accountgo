import test from "node:test";
import assert from "node:assert";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import {
  AUTH_POOL_HEADER,
  AUTH_POOL_V1_FALLBACK,
  MAX_AUTH_FALLBACK_BODY_BYTES,
  isAuthWarmupTriggerRequest,
  routePublicAuthRequest,
} from "../auth-pool-fallback.ts";
import { CAPACITY_POOL_HEADER, WarmupThrottle } from "../container-capacity.ts";
import {
  NOT_DISPATCHED_HEADER,
  isNotDispatchedUnavailable,
  serviceUnavailable,
} from "../container-errors.ts";
import { ContainerUnavailableError, unavailableResponse } from "../container-proxy.ts";

const __dirname = path.dirname(fileURLToPath(import.meta.url));

test("flag on + V2 pre-dispatch 503 replays once on V1 with identical method, body, and headers minus pool header", async () => {
  const v2Calls = [];
  const v1Calls = [];
  const logs = [];
  const body = JSON.stringify({ email: "user@example.com", code: "123456" });
  const req = new Request("https://api.accountgo.dev/api/auth/verify-otp?secret=keep-out", {
    method: "POST",
    headers: {
      "content-type": "application/json",
      "x-custom": "keep-me",
      [CAPACITY_POOL_HEADER]: "paid",
    },
    body,
  });

  const res = await routePublicAuthRequest({
    request: req,
    url: new URL(req.url),
    v2Enabled: true,
    getV2Stub: () => ({
      async fetch(r) {
        v2Calls.push({
          method: r.method,
          pool: r.headers.get(CAPACITY_POOL_HEADER),
          body: await r.text(),
        });
        return serviceUnavailable("SERVICE_STARTING", "Service is starting up", 5, { notDispatched: true });
      },
    }),
    getV1Stub: () => ({
      async fetch(r) {
        v1Calls.push({
          url: r.url,
          method: r.method,
          custom: r.headers.get("x-custom"),
          contentType: r.headers.get("content-type"),
          pool: r.headers.get(CAPACITY_POOL_HEADER),
          body: await r.text(),
        });
        return new Response(JSON.stringify({ accessToken: "v1-ok" }), {
          status: 200,
          headers: { "content-type": "application/json" },
        });
      },
    }),
    waitUntil: () => {},
    warmupThrottle: new WarmupThrottle(60_000),
    logWarn: (msg) => logs.push(msg),
  });

  assert.equal(v2Calls.length, 1);
  assert.equal(v2Calls[0].pool, "paid");
  assert.equal(v2Calls[0].body, body);

  assert.equal(v1Calls.length, 1);
  assert.equal(v1Calls[0].method, "POST");
  assert.equal(v1Calls[0].url, req.url);
  assert.equal(v1Calls[0].custom, "keep-me");
  assert.equal(v1Calls[0].contentType, "application/json");
  assert.equal(v1Calls[0].pool, null);
  assert.equal(v1Calls[0].body, body);

  assert.equal(res.status, 200);
  assert.equal(res.headers.get(AUTH_POOL_HEADER), AUTH_POOL_V1_FALLBACK);
  assert.deepEqual(await res.json(), { accessToken: "v1-ok" });

  assert.equal(logs.length, 1);
  assert.equal(logs[0], "[worker] auth fallback to V1 path=/api/auth/verify-otp code=SERVICE_STARTING");
  assert.ok(!logs[0].includes("keep-out"));
  assert.ok(!logs[0].includes("user@example.com"));
  assert.ok(!logs[0].includes("123456"));
});

test("V2 kind-2 503, SERVICE_MISCONFIGURED, app 503, 500, and 401 are returned as-is without calling V1", async () => {
  const cases = [
    {
      name: "kind-2 503 (SERVICE_INTERRUPTED, no header)",
      response: () => serviceUnavailable("SERVICE_INTERRUPTED", "Restarted mid-request", 5),
      expectedStatus: 503,
    },
    {
      name: "SERVICE_MISCONFIGURED 503",
      response: () => serviceUnavailable("SERVICE_MISCONFIGURED", "Bad config", 60),
      expectedStatus: 503,
    },
    {
      name: "app-generated 503",
      response: () => new Response(JSON.stringify({ error: "db down" }), { status: 503 }),
      expectedStatus: 503,
    },
    {
      name: "app 500",
      response: () => new Response("internal error", { status: 500 }),
      expectedStatus: 500,
    },
    {
      name: "app 401",
      response: () => new Response("unauthorized", { status: 401 }),
      expectedStatus: 401,
    },
  ];

  for (const tc of cases) {
    let v1Called = 0;
    const req = new Request("https://api.accountgo.dev/api/auth/refresh", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ refreshToken: "tok-1" }),
    });

    const res = await routePublicAuthRequest({
      request: req,
      url: new URL(req.url),
      v2Enabled: true,
      getV2Stub: () => ({ async fetch() { return tc.response(); } }),
      getV1Stub: () => ({
        async fetch() {
          v1Called++;
          return new Response("should not happen", { status: 200 });
        },
      }),
      waitUntil: () => {},
      warmupThrottle: new WarmupThrottle(60_000),
      logWarn: () => {},
    });

    assert.equal(v1Called, 0, `${tc.name}: V1 must not be called`);
    assert.equal(res.status, tc.expectedStatus, tc.name);
    assert.equal(res.headers.get(AUTH_POOL_HEADER), null, tc.name);
  }
});

test("V2 stub.fetch throwing returns 503 SERVICE_INTERRUPTED without replaying on V1", async () => {
  let v1Called = 0;
  const logs = [];
  const req = new Request("https://api.accountgo.dev/api/auth/mfa/webauthn/assertion/complete", {
    method: "POST",
    body: JSON.stringify({ assertion: "data" }),
  });

  const res = await routePublicAuthRequest({
    request: req,
    url: new URL(req.url),
    v2Enabled: true,
    getV2Stub: () => ({
      async fetch() {
        throw new Error("Durable Object network connection lost");
      },
    }),
    getV1Stub: () => ({
      async fetch() {
        v1Called++;
        return new Response("ok", { status: 200 });
      },
    }),
    waitUntil: () => {},
    warmupThrottle: new WarmupThrottle(60_000),
    logWarn: (m) => logs.push(m),
  });

  assert.equal(v1Called, 0);
  assert.equal(res.status, 503);
  assert.equal(isNotDispatchedUnavailable(res), false);
  const payload = await res.json();
  assert.equal(payload.code, "SERVICE_INTERRUPTED");
  assert.equal(logs.length, 1);
  assert.ok(logs[0].includes("path=/api/auth/mfa/webauthn/assertion/complete"));
});

test("body over the 64 KiB cap skips fallback (both declared Content-Length and chunked stream)", async () => {
  const oversized = "x".repeat(MAX_AUTH_FALLBACK_BODY_BYTES + 1);

  // Case 1: declared Content-Length over cap
  {
    let v1Called = 0;
    let v2BodyLen = 0;
    const req = new Request("https://api.accountgo.dev/api/auth/verify-otp", {
      method: "POST",
      headers: {
        "content-type": "application/json",
        "content-length": String(oversized.length),
      },
      body: oversized,
    });

    const res = await routePublicAuthRequest({
      request: req,
      url: new URL(req.url),
      v2Enabled: true,
      getV2Stub: () => ({
        async fetch(r) {
          v2BodyLen = (await r.text()).length;
          return serviceUnavailable("SERVICE_STARTING", "Starting", 5, { notDispatched: true });
        },
      }),
      getV1Stub: () => ({
        async fetch() {
          v1Called++;
          return new Response("v1", { status: 200 });
        },
      }),
      waitUntil: () => {},
      warmupThrottle: new WarmupThrottle(60_000),
      logWarn: () => {},
    });

    assert.equal(v2BodyLen, oversized.length);
    assert.equal(v1Called, 0);
    assert.equal(res.status, 503);
  }

  // Case 2: streamed body without Content-Length over cap
  {
    let v1Called = 0;
    let v2BodyLen = 0;
    const chunkA = new Uint8Array(40 * 1024).fill(65);
    const chunkB = new Uint8Array(30 * 1024).fill(66);
    const stream = new ReadableStream({
      start(controller) {
        controller.enqueue(chunkA);
        controller.enqueue(chunkB);
        controller.close();
      },
    });
    const req = new Request("https://api.accountgo.dev/api/auth/verify-otp", {
      method: "POST",
      body: stream,
      duplex: "half",
    });

    const res = await routePublicAuthRequest({
      request: req,
      url: new URL(req.url),
      v2Enabled: true,
      getV2Stub: () => ({
        async fetch(r) {
          v2BodyLen = (await r.arrayBuffer()).byteLength;
          return serviceUnavailable("SERVICE_STARTING", "Starting", 5, { notDispatched: true });
        },
      }),
      getV1Stub: () => ({
        async fetch() {
          v1Called++;
          return new Response("v1", { status: 200 });
        },
      }),
      waitUntil: () => {},
      warmupThrottle: new WarmupThrottle(60_000),
      logWarn: () => {},
    });

    assert.equal(v2BodyLen, chunkA.byteLength + chunkB.byteLength);
    assert.equal(v1Called, 0);
    assert.equal(res.status, 503);
  }
});

test("warm-up fires once per throttle window on send-otp and google/start with flag on, never with flag off, and failures do not affect response", async () => {
  assert.equal(isAuthWarmupTriggerRequest("/api/auth/send-otp"), true);
  assert.equal(isAuthWarmupTriggerRequest("/API/Auth/Google/Start/"), true);
  assert.equal(isAuthWarmupTriggerRequest("/api/auth/verify-otp"), false);
  assert.equal(isAuthWarmupTriggerRequest("/api/auth/refresh"), false);

  let now = 10_000;
  const throttle = new WarmupThrottle(60_000, () => now);
  const warmupCalls = [];
  const pending = [];
  const logs = [];

  const send = (pathname, v2Enabled, failWarmup = false) => {
    const req = new Request(`https://api.accountgo.dev${pathname}`, {
      method: pathname.includes("send-otp") ? "POST" : "GET",
      body: pathname.includes("send-otp") ? JSON.stringify({ email: "a@b.com" }) : undefined,
    });
    return routePublicAuthRequest({
      request: req,
      url: new URL(req.url),
      v2Enabled,
      getV2Stub: () => ({
        async fetch(r) {
          if (new URL(r.url).pathname === "/health") {
            warmupCalls.push({ url: r.url, pool: r.headers.get(CAPACITY_POOL_HEADER) });
            if (failWarmup) throw new Error("warmup boom");
            return new Response("ok", { status: 200 });
          }
          return new Response("v2-ok", { status: 200 });
        },
      }),
      getV1Stub: () => ({
        async fetch() {
          return new Response("v1-ok", { status: 200 });
        },
      }),
      waitUntil: (p) => pending.push(p),
      warmupThrottle: throttle,
      logWarn: (m) => logs.push(m),
    });
  };

  // Flag off: never warms up, routes straight to V1
  const offRes = await send("/api/auth/send-otp", false);
  assert.equal(await offRes.text(), "v1-ok");
  assert.equal(warmupCalls.length, 0);

  // Flag on, send-otp: fires 1 warm-up
  const onRes1 = await send("/api/auth/send-otp", true);
  assert.equal(await onRes1.text(), "v2-ok");
  await Promise.all(pending);
  assert.equal(warmupCalls.length, 1);
  assert.equal(warmupCalls[0].url, "https://api.accountgo.dev/health");
  assert.equal(warmupCalls[0].pool, "paid");

  // Within 60s window: google/start does NOT fire another warm-up
  now += 30_000;
  const onRes2 = await send("/api/auth/google/start", true);
  assert.equal(await onRes2.text(), "v2-ok");
  assert.equal(warmupCalls.length, 1);

  // After 60s window: google/start fires warm-up; a failing warm-up does not fail the request
  now += 30_000;
  const onRes3 = await send("/api/auth/google/start", true, true);
  assert.equal(onRes3.status, 200);
  assert.equal(await onRes3.text(), "v2-ok");
  await Promise.all(pending);
  assert.equal(warmupCalls.length, 2);
  assert.ok(logs.some((l) => l.includes("auth pool warm-up failed")));
});

test("flag off routes directly to V1 and never touches V2", async () => {
  let v2Called = 0;
  let v1Called = 0;
  const req = new Request("https://api.accountgo.dev/api/auth/google", {
    method: "POST",
    body: JSON.stringify({ code: "abc" }),
  });

  const res = await routePublicAuthRequest({
    request: req,
    url: new URL(req.url),
    v2Enabled: false,
    getV2Stub: () => {
      v2Called++;
      return { async fetch() { return new Response("v2", { status: 200 }); } };
    },
    getV1Stub: () => ({
      async fetch() {
        v1Called++;
        return new Response("v1-direct", { status: 200 });
      },
    }),
    waitUntil: () => {},
    warmupThrottle: new WarmupThrottle(60_000),
  });

  assert.equal(v2Called, 0);
  assert.equal(v1Called, 1);
  assert.equal(res.status, 200);
  assert.equal(res.headers.get(AUTH_POOL_HEADER), null);
  assert.equal(await res.text(), "v1-direct");
});

test("not-dispatched header is set only on pre-dispatch 503 helpers and never on SERVICE_MISCONFIGURED or post-dispatch paths", () => {
  const preStart = serviceUnavailable("SERVICE_STARTING", "starting", 5, { notDispatched: true });
  assert.equal(preStart.headers.get(NOT_DISPATCHED_HEADER), "1");
  assert.equal(isNotDispatchedUnavailable(preStart), true);

  const preUpdating = serviceUnavailable("SERVICE_UPDATING", "updating", 5, { notDispatched: true });
  assert.equal(isNotDispatchedUnavailable(preUpdating), true);

  // SERVICE_MISCONFIGURED must never be marked not-dispatched even if requested.
  const misconfigured = serviceUnavailable("SERVICE_MISCONFIGURED", "bad cfg", 60, { notDispatched: true });
  assert.equal(misconfigured.headers.get(NOT_DISPATCHED_HEADER), null);
  assert.equal(isNotDispatchedUnavailable(misconfigured), false);

  const postInterrupted = serviceUnavailable("SERVICE_INTERRUPTED", "interrupted", 5);
  assert.equal(postInterrupted.headers.get(NOT_DISPATCHED_HEADER), null);
  assert.equal(isNotDispatchedUnavailable(postInterrupted), false);

  const notReachedRes = unavailableResponse(new ContainerUnavailableError("not running", "not-reached"));
  assert.equal(isNotDispatchedUnavailable(notReachedRes), true);

  const maybeReachedRes = unavailableResponse(new ContainerUnavailableError("connection reset", "maybe-reached"));
  assert.equal(isNotDispatchedUnavailable(maybeReachedRes), false);

  // Verify V2 DO marks all pre-dispatch 503 paths (in fetch() before proxy fetchFromContainer) and no post-dispatch catch-all path.
  const v2Source = fs.readFileSync(path.join(__dirname, "..", "worker-container-v2.ts"), "utf8");
  const fetchStart = v2Source.indexOf("async fetch(request: Request)");
  const dispatchIdx = v2Source.lastIndexOf("fetchFromContainer(");
  assert.ok(fetchStart > 0 && dispatchIdx > fetchStart, "expected proxy fetchFromContainer call in worker-container-v2.ts fetch()");
  const preDispatchSection = v2Source.slice(fetchStart, dispatchIdx);
  const postDispatchSection = v2Source.slice(dispatchIdx);

  const preMatches = preDispatchSection.match(/notDispatched:\s*true/g) ?? [];
  assert.equal(preMatches.length, 3, "expected 3 pre-dispatch notDispatched: true markers (portsReady, BuildMismatch, ensureInitialized)");
  assert.equal(/notDispatched:\s*true/.test(postDispatchSection), false, "post-dispatch catch-all must not set notDispatched: true");
});
