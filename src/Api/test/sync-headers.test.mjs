import test from "node:test";
import assert from "node:assert";
import { gzipSync } from "node:zlib";
import { parseSyncSqlHeaders, constantTimeEqual } from "../sync-utils.ts";

test("parseSyncSqlHeaders parses single x-d1-sync-sql header", async () => {
  const statements = ["INSERT INTO T1 VALUES (1);", "UPDATE T2 SET a = 2;"];
  const b64 = Buffer.from(JSON.stringify(statements)).toString("base64");
  const headers = new Headers();
  headers.set("x-d1-sync-sql", b64);

  const result = await parseSyncSqlHeaders(headers);
  assert.deepStrictEqual(result, statements);
});

test("parseSyncSqlHeaders parses and concatenates chunked headers", async () => {
  const chunk1 = ["STMT 1", "STMT 2"];
  const chunk2 = ["STMT 3", "STMT 4"];
  const chunk3 = ["STMT 5"];

  const b64_1 = Buffer.from(JSON.stringify(chunk1)).toString("base64");
  const b64_2 = Buffer.from(JSON.stringify(chunk2)).toString("base64");
  const b64_3 = Buffer.from(JSON.stringify(chunk3)).toString("base64");

  const headers = new Headers();
  headers.set("x-d1-sync-sql-count", "3");
  headers.set("x-d1-sync-sql-0", b64_1);
  headers.set("x-d1-sync-sql-1", b64_2);
  headers.set("x-d1-sync-sql-2", b64_3);

  const result = await parseSyncSqlHeaders(headers);
  assert.deepStrictEqual(result, ["STMT 1", "STMT 2", "STMT 3", "STMT 4", "STMT 5"]);
});

test("parseSyncSqlHeaders throws error when a chunk is missing", async () => {
  const chunk1 = ["STMT 1"];
  const b64_1 = Buffer.from(JSON.stringify(chunk1)).toString("base64");

  const headers = new Headers();
  headers.set("x-d1-sync-sql-count", "2");
  headers.set("x-d1-sync-sql-0", b64_1);
  // x-d1-sync-sql-1 is missing

  await assert.rejects(
    () => parseSyncSqlHeaders(headers),
    /Missing expected chunk header: "x-d1-sync-sql-1"/
  );
});

test("parseSyncSqlHeaders throws error on invalid count header", async () => {
  const headers = new Headers();
  headers.set("x-d1-sync-sql-count", "invalid");

  await assert.rejects(
    () => parseSyncSqlHeaders(headers),
    /Invalid x-d1-sync-sql-count header/
  );
});

test("parseSyncSqlHeaders throws error on malformed chunk JSON and does not echo payload", async () => {
  const headers = new Headers();
  headers.set("x-d1-sync-sql-count", "1");
  headers.set("x-d1-sync-sql-0", Buffer.from("SELECT * FROM sensitive_table;").toString("base64"));

  let caughtError = null;
  try {
    await parseSyncSqlHeaders(headers);
  } catch (err) {
    caughtError = err;
  }

  assert(caughtError !== null, "Expected parseSyncSqlHeaders to throw");
  assert.match(caughtError.message, /Failed to decode\/parse chunk header "x-d1-sync-sql-0": SyntaxError/);
  assert(!caughtError.message.includes("sensitive_table"), "Error must not echo SQL payload");
});

test("parseSyncSqlHeaders returns empty array when no sync headers present", async () => {
  const headers = new Headers();
  headers.set("content-type", "application/json");

  const result = await parseSyncSqlHeaders(headers);
  assert.deepStrictEqual(result, []);
});

test("parseSyncSqlHeaders decompresses a single gzip header preserving Unicode", async () => {
  const statements = ["INSERT INTO T VALUES ('Café 東京');", "UPDATE T SET value = 2;"];
  const headers = new Headers({
    "x-d1-sync-encoding": "gzip",
    "x-d1-sync-sql": gzipSync(JSON.stringify(statements)).toString("base64"),
  });
  assert.deepStrictEqual(await parseSyncSqlHeaders(headers), statements);
});

test("parseSyncSqlHeaders joins gzip chunks before base64 decoding and decompressing", async () => {
  const statements = Array.from({ length: 1000 }, (_, i) => `INSERT INTO T VALUES (${i}, 'Workspace ${i}');`);
  const encoded = gzipSync(JSON.stringify(statements)).toString("base64");
  const chunks = encoded.match(/.{1,101}/g);
  const headers = new Headers({ "x-d1-sync-encoding": "gzip", "x-d1-sync-sql-count": String(chunks.length) });
  chunks.forEach((chunk, index) => headers.set(`x-d1-sync-sql-${index}`, chunk));
  assert.deepStrictEqual(await parseSyncSqlHeaders(headers), statements);
});

test("parseSyncSqlHeaders rejects corrupt or invalid gzip payloads without echoing SQL", async () => {
  for (const payload of [
    Buffer.from("sensitive_table").toString("base64"),
    gzipSync("sensitive_table").toString("base64"),
    gzipSync(JSON.stringify({ secret: "sensitive_table" })).toString("base64"),
    gzipSync(JSON.stringify(["sensitive_table", 42])).toString("base64"),
  ]) {
    const headers = new Headers({ "x-d1-sync-encoding": "gzip", "x-d1-sync-sql": payload });
    await assert.rejects(() => parseSyncSqlHeaders(headers), {
      message: "Failed to decode/parse gzip sync headers",
    });
  }
});

test("parseSyncSqlHeaders rejects missing gzip chunks and unknown encoding", async () => {
  const headers = new Headers({
    "x-d1-sync-encoding": "gzip",
    "x-d1-sync-sql-count": "2",
    "x-d1-sync-sql-0": "H4sI",
  });
  await assert.rejects(() => parseSyncSqlHeaders(headers), /Missing expected chunk/);
  headers.set("x-d1-sync-encoding", "br");
  await assert.rejects(() => parseSyncSqlHeaders(headers), /Unsupported x-d1-sync-encoding/);
});

test("constantTimeEqual correctly compares strings", () => {
  assert.strictEqual(constantTimeEqual("secret123", "secret123"), true);
  assert.strictEqual(constantTimeEqual("secret123", "secret124"), false);
  assert.strictEqual(constantTimeEqual("secret123", "short"), false);
});
