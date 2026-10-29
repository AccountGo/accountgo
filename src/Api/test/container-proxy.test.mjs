import test from "node:test";
import assert from "node:assert/strict";
import {
  LIBRARY_FAILURE_PREFIXES,
  failureReach,
  libraryFailureText,
} from "../container-errors.ts";
import {
  ContainerUnavailableError,
  RestoreState,
  bufferRequest,
  fetchFromContainer,
  toRequest,
  unavailableResponse,
  watchContainerExit,
} from "../container-proxy.ts";

const ATTEMPTS = 4;

/**
 * Mirrors the V1/V2 writers: ensureReady starts a new (empty) instance when none is running, ensureRestored
 * restores into the current instance, dispatch replays `script` (a Response, an Error to throw, or a function).
 */
function makeHost(script, { running = true, restored = true } = {}) {
  const state = new RestoreState();
  const events = [];
  const sent = [];
  let instance = running ? 1 : 0;
  let restoredInstance = 0;
  const host = {
    state,
    events,
    sent,
    running,
    get restored() {
      return state.restored;
    },
    isRunning: () => host.running,
    async ensureReady() {
      events.push("ready");
      if (!host.running) {
        host.invalidate("container is starting");
        host.running = true;
        instance += 1;
      }
    },
    async ensureRestored() {
      events.push("restore");
      const token = state.current();
      restoredInstance = instance;
      assert.ok(state.commit(token));
    },
    invalidate() {
      state.invalidate();
    },
    async dispatch(request) {
      const body = request.body ? new Uint8Array(await request.arrayBuffer()) : null;
      events.push(`dispatch ${request.method}`);
      sent.push({ method: request.method, url: request.url, body, instance, restoredInstance });
      const next = script.shift();
      if (next instanceof Error) throw next;
      return typeof next === "function" ? next() : next;
    },
    log() {},
  };
  if (restored) {
    restoredInstance = instance;
    state.commit(state.current());
  }
  return host;
}

const ok = () => new Response('{"ok":true}', { status: 200 });
const library500 = (text) => new Response(text, { status: 500 });

function run(host, request, extra = {}) {
  return bufferRequest(request.url, request, new Headers(request.headers)).then((buffered) =>
    fetchFromContainer(host, () => toRequest(buffered), {
      label: `proxy ${request.method}`,
      method: request.method,
      requireRestored: true,
      attempts: ATTEMPTS,
      baseDelayMs: 0,
      sleep: async () => {},
      ...extra,
    })
  );
}

const get = () => new Request("http://app.test/api/organizations");
const post = (body = '{"amount":12.5}') =>
  new Request("http://app.test/api/Sales/invoices", {
    method: "POST",
    headers: { "content-type": "application/json" },
    body,
  });

test("library 500 bodies with each prefix are transient; an app 500 is not", async () => {
  for (const prefix of LIBRARY_FAILURE_PREFIXES) {
    const res = library500(`${prefix}: The container is not running, consider calling start()`);
    assert.ok(await libraryFailureText(res), prefix);
    // The original body is still readable after classification.
    assert.match(await res.text(), new RegExp(`^${prefix}`));
  }
  assert.equal(await libraryFailureText(library500('{"title":"Internal Server Error","status":500}')), null);
  assert.equal(await libraryFailureText(new Response("Error proxying request to container", { status: 502 })), null);
});

test("an app 500 is passed through unchanged and not retried", async () => {
  const body = '{"title":"Internal Server Error","status":500,"detail":"boom"}';
  const host = makeHost([new Response(body, { status: 500, headers: { "x-app": "1" } })]);
  const res = await run(host, get());
  assert.equal(res.status, 500);
  assert.equal(res.headers.get("x-app"), "1");
  assert.equal(await res.text(), body);
  assert.equal(host.sent.length, 1);
  assert.ok(host.restored, "an app error does not invalidate the restore");
});

test("failure reach: only start/not-running/not-ready/refused failures prove the app never saw the request", () => {
  assert.equal(failureReach("Failed to start container: Container did not start after 8000ms"), "not-reached");
  assert.equal(
    failureReach("Error proxying request to container: The container is not running, consider calling start()"),
    "not-reached"
  );
  assert.equal(failureReach(new Error("connect ECONNREFUSED 127.0.0.1:8001")), "not-reached");
  assert.equal(failureReach("Container suddenly disconnected, try again"), "maybe-reached");
  assert.equal(failureReach("Error proxying request to container: Network connection lost."), "maybe-reached");
  assert.equal(failureReach(new Error("Error proxying request to container: The container just exited")), "maybe-reached");
});

test("transient failure on GET is retried and succeeds", async () => {
  const host = makeHost([library500("Container suddenly disconnected, try again"), ok()]);
  const res = await run(host, get());
  assert.equal(res.status, 200);
  assert.equal(host.sent.length, 2);
});

test('POST "Error proxying" (may have reached the app) returns 503 without a second send', async () => {
  for (const failure of [
    library500("Error proxying request to container: Network connection lost."),
    library500("Container suddenly disconnected, try again"),
    new Error("Error proxying request to container: The container just exited"),
  ]) {
    const host = makeHost([failure, ok()]);
    const err = await run(host, post()).then(
      () => assert.fail("expected ContainerUnavailableError"),
      (e) => e
    );
    assert.ok(err instanceof ContainerUnavailableError);
    assert.equal(err.reach, "maybe-reached");
    assert.equal(host.sent.length, 1, "no second send");
    const res = unavailableResponse(err);
    assert.equal(res.status, 503);
    assert.equal(res.headers.get("Retry-After"), "5");
  }
});

test('POST "Failed to start container" is retried with the same body bytes', async () => {
  const body = '{"amount":12.5,"memo":"café"}';
  const host = makeHost([library500("Failed to start container: Container did not start"), ok()]);
  const res = await run(host, post(body));
  assert.equal(res.status, 200);
  assert.equal(host.sent.length, 2);
  const expected = new TextEncoder().encode(body);
  assert.deepEqual(host.sent[0].body, expected);
  assert.deepEqual(host.sent[1].body, expected);
});

test("after a transient failure restored is false and the next attempt runs readiness + restore first", async () => {
  let restoredAfterFailure;
  const host = makeHost([
    library500("Error proxying request to container: The container is not running, consider calling start()"),
    ok(),
  ]);
  const realReady = host.ensureReady;
  host.ensureReady = async () => {
    restoredAfterFailure ??= host.restored;
    await realReady();
  };
  const res = await run(host, get());
  assert.equal(res.status, 200);
  assert.equal(restoredAfterFailure, false);
  assert.deepEqual(host.events, ["dispatch GET", "ready", "restore", "dispatch GET"]);
  assert.ok(host.restored);
});

test("when retries run out the caller gets 503 with Retry-After, never the library 500", async () => {
  const failures = Array.from({ length: ATTEMPTS }, () =>
    library500("Error proxying request to container: The container is not running, consider calling start()")
  );
  const host = makeHost(failures);
  const err = await run(host, get()).then(
    () => assert.fail("expected ContainerUnavailableError"),
    (e) => e
  );
  assert.ok(err instanceof ContainerUnavailableError);
  assert.equal(host.sent.length, ATTEMPTS);
  const res = unavailableResponse(err);
  assert.equal(res.status, 503);
  assert.equal(res.headers.get("Retry-After"), "5");
  assert.equal(res.headers.get("content-type"), "application/problem+json");
});

test("readiness and the ready timing still run on retries (Server-Timing ready segment)", async () => {
  const host = makeHost([library500("Container suddenly disconnected, try again"), ok()]);
  const readyTiming = { readyMs: 0 };
  host.ensureReady = (
    (inner) => async () => {
      await new Promise((r) => setTimeout(r, 5));
      await inner();
    }
  )(host.ensureReady);
  await run(host, get(), { readyTiming });
  assert.ok(readyTiming.readyMs > 0);
});

test("refresh after a rollout replaced the container waits for the D1 restore (incident case)", async () => {
  // A restored container; then the runtime stops it for a version rollout. The library reports that only
  // through onError, so before this change the DO kept restored=true and served the next refresh from a
  // fresh, empty container (401 -> signed out).
  let exit;
  const container = {
    running: true,
    monitor: () => new Promise((_, reject) => (exit = reject)),
  };
  const host = makeHost([() => new Response('{"accessToken":"redacted"}', { status: 200 })]);
  Object.defineProperty(host, "running", {
    get: () => container.running,
    set: (v) => (container.running = v),
  });
  // Registered for the restore that makeHost committed, as initialize() does.
  watchContainerExit(container, host.state, host.state.current(), () => {});
  assert.ok(host.restored);
  container.running = false;
  exit(new Error("Runtime signalled the container to exit due to a new version rollout: 0"));
  await new Promise((r) => setImmediate(r));
  assert.equal(host.restored, false, "the exit invalidates the restore");

  const refresh = new Request("http://app.test/api/auth/refresh", { method: "POST", body: "" });
  const res = await run(host, refresh);
  assert.equal(res.status, 200);
  assert.deepEqual(host.events, ["ready", "restore", "dispatch POST"]);
  const [sent] = host.sent;
  assert.equal(sent.instance, 2, "served by the replacement container");
  assert.equal(sent.restoredInstance, sent.instance, "only after it was restored");
});

test("refresh whose response was lost returns 503 (session kept) and is not resent", async () => {
  // The client keeps the session on 5xx and retries with the same cookie; the server's rotation grace
  // accepts the previous token if the rotation did commit.
  const host = makeHost([library500("Container suddenly disconnected, try again"), ok()]);
  const refresh = new Request("http://app.test/api/auth/refresh", { method: "POST", body: "" });
  const err = await run(host, refresh).then(
    () => assert.fail("expected ContainerUnavailableError"),
    (e) => e
  );
  const res = unavailableResponse(err);
  assert.ok(res.status >= 500, "never a 401");
  assert.equal(res.status, 503);
  assert.equal(host.sent.length, 1);
  assert.equal(host.restored, false);

  // The client's retry restores first, then reaches the app.
  const retry = await run(host, new Request("http://app.test/api/auth/refresh", { method: "POST", body: "" }));
  assert.equal(retry.status, 200);
  assert.deepEqual(host.events.slice(1), ["ready", "restore", "dispatch POST"]);
});

test("a restore interrupted by an invalidation does not commit", () => {
  const state = new RestoreState();
  const token = state.current();
  state.invalidate();
  assert.equal(state.commit(token), false);
  assert.equal(state.restored, false);
  const next = state.current();
  assert.equal(state.commit(next), true);
  assert.equal(state.restored, true);
});

test("a monitor that settles while the container is still running does not invalidate", async () => {
  const state = new RestoreState();
  const token = state.current();
  state.commit(token);
  const container = { running: true, monitor: () => Promise.reject(new Error("monitor already attached")) };
  watchContainerExit(container, state, token, () => {});
  await new Promise((r) => setImmediate(r));
  assert.equal(state.restored, true);
});

test("bufferRequest keeps GET bodiless and lets a POST body be sent twice", async () => {
  const g = await bufferRequest("http://app.test/a", get(), new Headers());
  assert.equal(g.body, null);
  const p = await bufferRequest("http://app.test/b", post("abc"), new Headers({ "content-type": "text/plain" }));
  assert.equal(await toRequest(p).text(), "abc");
  assert.equal(await toRequest(p).text(), "abc");
  assert.equal(toRequest(p).headers.get("content-type"), "text/plain");
});
