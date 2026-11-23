import test from "node:test";
import assert from "node:assert";
import {
  CapacityRouter,
  WarmupThrottle,
  isTieredCapacityEnabled,
  parseCapacityPool,
  parseDocumentCaptureExtractionPool,
  resolveContainerInstanceType,
  resolvePoolContainerName,
  resolvePoolInstanceType,
  resolveWriterContainerName,
} from "../container-capacity.ts";
import {
  isDocumentCaptureCapabilitiesRoute,
  isDocumentCaptureUploadRoute,
  isDocumentCaptureWarmupRequest,
} from "../container-proxy.ts";

test("isTieredCapacityEnabled only accepts true", () => {
  assert.equal(isTieredCapacityEnabled("true"), true);
  assert.equal(isTieredCapacityEnabled("TRUE"), true);
  assert.equal(isTieredCapacityEnabled(" true "), true);
  assert.equal(isTieredCapacityEnabled("false"), false);
  assert.equal(isTieredCapacityEnabled(""), false);
  assert.equal(isTieredCapacityEnabled(undefined), false);
  assert.equal(isTieredCapacityEnabled("1"), false);
});

test("resolveContainerInstanceType maps known sizes and falls back", () => {
  assert.equal(resolveContainerInstanceType("lite"), "lite");
  assert.equal(resolveContainerInstanceType("standard-2"), "standard-2");
  assert.equal(resolveContainerInstanceType(" STANDARD-1 "), "standard-1");
  assert.equal(resolveContainerInstanceType("basic"), "standard-1");
  assert.equal(resolveContainerInstanceType("dev"), "lite");
  assert.equal(resolveContainerInstanceType("standard"), "standard-1");
  assert.equal(resolveContainerInstanceType(""), "standard-1");
  assert.equal(resolveContainerInstanceType(undefined), "standard-1");
});

test("resolveWriterContainerName names the V1 free writer", () => {
  assert.equal(resolveWriterContainerName("abc123"), "v1-free-abc123");
  assert.equal(resolveWriterContainerName(undefined), "v1-free-dev");
});

test("parseCapacityPool accepts paid and free only", () => {
  assert.equal(parseCapacityPool("paid"), "paid");
  assert.equal(parseCapacityPool(" FREE "), "free");
  assert.equal(parseCapacityPool("pro"), null);
  assert.equal(parseCapacityPool(null), null);
});

test("resolvePoolInstanceType defaults paid to standard-1 and free to lite", () => {
  assert.equal(resolvePoolInstanceType("paid", {}), "standard-1");
  assert.equal(resolvePoolInstanceType("free", {}), "lite");
  assert.equal(resolvePoolInstanceType("paid", { CONTAINER_INSTANCE_TYPE_PAID: "standard-2" }), "standard-2");
  // durable_object start() has no "basic"; it maps to the nearest supported size.
  assert.equal(resolvePoolInstanceType("paid", { CONTAINER_INSTANCE_TYPE_PAID: "basic" }), "standard-1");
  assert.equal(resolvePoolInstanceType("free", { CONTAINER_INSTANCE_TYPE_FREE: "lite" }), "lite");
});

test("resolvePoolContainerName names V2 pool writers by generation and pool", () => {
  assert.equal(resolvePoolContainerName("paid", "abc"), "v2-paid-abc");
  assert.equal(resolvePoolContainerName("free", undefined), "v2-free-dev");
});

test("CapacityRouter sends unknown and missing workspaces to the free pool", () => {
  const router = new CapacityRouter();
  assert.equal(router.poolFor(null), "free");
  assert.equal(router.poolFor(""), "free");
  assert.equal(router.poolFor("acme"), "free");
});

test("CapacityRouter remembers a hint per slug until it expires", () => {
  let now = 1_000;
  const router = new CapacityRouter(60_000, 10, () => now);
  router.remember("Acme", "paid");
  assert.equal(router.poolFor("acme"), "paid");
  assert.equal(router.poolFor(" ACME "), "paid");
  router.remember("acme", "free");
  assert.equal(router.poolFor("acme"), "free");
  router.remember("acme", "paid");
  now += 60_000;
  assert.equal(router.poolFor("acme"), "free");
});

test("CapacityRouter evicts the oldest slug when full", () => {
  const router = new CapacityRouter(60_000, 2);
  router.remember("a", "paid");
  router.remember("b", "paid");
  router.remember("c", "paid");
  assert.equal(router.poolFor("a"), "free");
  assert.equal(router.poolFor("b"), "paid");
  assert.equal(router.poolFor("c"), "paid");
});

test('only public authentication endpoints use the optional V2 auth pool', async () => {
  const capacity = await import('../container-capacity.ts');
  assert.equal(typeof capacity.isPublicAuthRequest, 'function');
  for (const path of ['/api/auth/google', '/api/auth/google/start', '/api/auth/signup', '/api/auth/send-otp', '/api/auth/verify-otp', '/api/auth/refresh', '/api/auth/mfa/totp/verify', '/api/auth/SEND-OTP', '/API/Auth/SignUp/'])
    assert.equal(capacity.isPublicAuthRequest(path), true, path);
  for (const path of ['/health', '/api/auth/me', '/api/auth/logout', '/api/auth/google/extra', '/api/organizations'])
    assert.equal(capacity.isPublicAuthRequest(path), false, path);
});

test("parseDocumentCaptureExtractionPool only accepts paid", () => {
  assert.equal(parseDocumentCaptureExtractionPool("paid"), "paid");
  assert.equal(parseDocumentCaptureExtractionPool(" PAID "), "paid");
  assert.equal(parseDocumentCaptureExtractionPool("free"), null);
  assert.equal(parseDocumentCaptureExtractionPool(""), null);
  assert.equal(parseDocumentCaptureExtractionPool(undefined), null);
});

test("document capture upload and capabilities route matching isolates upload from other capture routes", () => {
  for (const uploadPath of ["/api/document-captures", "/api/document-captures/", "/API/Document-Captures", "/api/%64ocument-captures"]) {
    assert.equal(isDocumentCaptureUploadRoute(uploadPath), true, uploadPath);
    assert.equal(isDocumentCaptureCapabilitiesRoute(uploadPath), false, uploadPath);
  }

  for (const capPath of ["/api/document-captures/capabilities", "/api/document-captures/capabilities/", "/API/Document-Captures/Capabilities"]) {
    assert.equal(isDocumentCaptureCapabilitiesRoute(capPath), true, capPath);
    assert.equal(isDocumentCaptureUploadRoute(capPath), false, capPath);
  }

  for (const otherPath of [
    "/api/document-captures/cap_123",
    "/api/document-captures/cap_123/image",
    "/api/document-captures/cap_123/accept",
    "/api/document-captures/cap_123/reject",
    "/api/document-captures/cap_123/links",
    "/api/bills",
  ]) {
    assert.equal(isDocumentCaptureUploadRoute(otherPath), false, otherPath);
    assert.equal(isDocumentCaptureCapabilitiesRoute(otherPath), false, otherPath);
  }
});

test("WarmupThrottle permits at most one warm-up per interval", () => {
  let now = 1_000;
  const throttle = new WarmupThrottle(60_000, () => now);
  assert.equal(throttle.shouldRun(), true);
  assert.equal(throttle.shouldRun(), false);
  now += 59_999;
  assert.equal(throttle.shouldRun(), false);
  now += 1;
  assert.equal(throttle.shouldRun(), true);
  assert.equal(throttle.shouldRun(), false);
});

test("isDocumentCaptureWarmupRequest requires opt-in warmup=1 on GET capabilities", () => {
  assert.equal(isDocumentCaptureWarmupRequest("/api/document-captures/capabilities", "GET", "?warmup=1"), true);
  assert.equal(isDocumentCaptureWarmupRequest("/api/document-captures/capabilities", "GET", "?foo=bar&warmup=1"), true);
  assert.equal(isDocumentCaptureWarmupRequest("/api/document-captures/capabilities", "GET", ""), false);
  assert.equal(isDocumentCaptureWarmupRequest("/api/document-captures/capabilities", "GET", "?warmup=0"), false);
  assert.equal(isDocumentCaptureWarmupRequest("/api/document-captures/capabilities", "GET", "?warmup=true"), false);
  assert.equal(isDocumentCaptureWarmupRequest("/api/document-captures/capabilities", "POST", "?warmup=1"), false);
  assert.equal(isDocumentCaptureWarmupRequest("/api/document-captures", "GET", "?warmup=1"), false);
});
