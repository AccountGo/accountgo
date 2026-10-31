import test from "node:test";
import assert from "node:assert";
import { RuntimeFlags, parseFlag } from "../runtime-flags.ts";

const store = (values, calls = { n: 0 }) => ({
  calls,
  async get(key) {
    calls.n++;
    if (values.error) throw new Error("kv down");
    return values[key] ?? null;
  },
});

test("parseFlag accepts true/false spellings and rejects the rest", () => {
  assert.equal(parseFlag("true"), true);
  assert.equal(parseFlag(" ON "), true);
  assert.equal(parseFlag("1"), true);
  assert.equal(parseFlag("false"), false);
  assert.equal(parseFlag("off"), false);
  assert.equal(parseFlag("0"), false);
  assert.equal(parseFlag(""), null);
  assert.equal(parseFlag("maybe"), null);
  assert.equal(parseFlag(null), null);
});

test("without a FLAGS binding the deployed default applies", async () => {
  const flags = new RuntimeFlags();
  assert.equal(await flags.isEnabled(undefined, "tiered_capacity", true), true);
  assert.equal(await flags.isEnabled(undefined, "tiered_capacity", false), false);
});

test("a KV value overrides the deployed default", async () => {
  const flags = new RuntimeFlags();
  assert.equal(await flags.isEnabled(store({ tiered_capacity: "false" }), "tiered_capacity", true), false);
  const other = new RuntimeFlags();
  assert.equal(await other.isEnabled(store({ tiered_capacity: "true" }), "tiered_capacity", false), true);
});

test("a missing or unrecognised KV value keeps the deployed default", async () => {
  assert.equal(await new RuntimeFlags().isEnabled(store({}), "tiered_capacity", true), true);
  assert.equal(await new RuntimeFlags().isEnabled(store({ tiered_capacity: "yes?" }), "tiered_capacity", false), false);
});

test("a KV error keeps the deployed default", async () => {
  assert.equal(await new RuntimeFlags().isEnabled(store({ error: true }), "tiered_capacity", false), false);
});

test("each key is read at most once per TTL, then re-read", async () => {
  let now = 0;
  const values = { tiered_capacity: "true" };
  const kv = store(values);
  const flags = new RuntimeFlags(30_000, () => now);
  assert.equal(await flags.isEnabled(kv, "tiered_capacity", false), true);
  values.tiered_capacity = "false";
  now = 29_999;
  assert.equal(await flags.isEnabled(kv, "tiered_capacity", false), true);
  assert.equal(kv.calls.n, 1);
  now = 30_000;
  assert.equal(await flags.isEnabled(kv, "tiered_capacity", false), false);
  assert.equal(kv.calls.n, 2);
});
