import test from "node:test";
import assert from "node:assert";
import {
  isTieredCapacityEnabled,
  resolveContainerInstanceType,
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
  assert.equal(resolveContainerInstanceType(""), "standard-1");
  assert.equal(resolveContainerInstanceType(undefined), "standard-1");
});

test("resolveWriterContainerName matches Phase 0 shared writer key", () => {
  assert.equal(resolveWriterContainerName("v9", "abc123"), "v9-abc123");
  assert.equal(resolveWriterContainerName(undefined, undefined), "default-dev");
  assert.equal(resolveWriterContainerName("local", "dev"), "local-dev");
});
