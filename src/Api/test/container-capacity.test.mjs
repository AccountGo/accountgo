import test from "node:test";
import assert from "node:assert";
import {
  CapacityRouter,
  isTieredCapacityEnabled,
  parseCapacityPool,
  resolveContainerInstanceType,
  resolvePoolContainerName,
  resolvePoolInstanceType,
  resolveWriterContainerName,
} from "../container-capacity.ts";

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

test("resolveWriterContainerName matches Phase 0 shared writer key", () => {
  assert.equal(resolveWriterContainerName("v9", "abc123"), "v9-abc123");
  assert.equal(resolveWriterContainerName(undefined, undefined), "default-dev");
  assert.equal(resolveWriterContainerName("local", "dev"), "local-dev");
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

test("resolvePoolContainerName keeps one writer per pool per version", () => {
  assert.equal(resolvePoolContainerName("v10", "paid", "abc"), "v10-paid-abc");
  assert.equal(resolvePoolContainerName(undefined, "free", undefined), "default-free-dev");
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
