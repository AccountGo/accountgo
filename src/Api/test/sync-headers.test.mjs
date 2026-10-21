import test from "node:test";
import assert from "node:assert";
import { parseSyncSqlHeaders, constantTimeEqual } from "../sync-utils.ts";

test("parseSyncSqlHeaders parses single x-d1-sync-sql header", () => {
  const statements = ["INSERT INTO T1 VALUES (1);", "UPDATE T2 SET a = 2;"];
  const b64 = Buffer.from(JSON.stringify(statements)).toString("base64");
  const headers = new Headers();
  headers.set("x-d1-sync-sql", b64);

  const result = parseSyncSqlHeaders(headers);
  assert.deepStrictEqual(result, statements);
});

test("parseSyncSqlHeaders parses and concatenates chunked headers", () => {
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

  const result = parseSyncSqlHeaders(headers);
  assert.deepStrictEqual(result, ["STMT 1", "STMT 2", "STMT 3", "STMT 4", "STMT 5"]);
});

test("parseSyncSqlHeaders returns empty array when no sync headers present", () => {
  const headers = new Headers();
  headers.set("content-type", "application/json");

  const result = parseSyncSqlHeaders(headers);
  assert.deepStrictEqual(result, []);
});

test("constantTimeEqual correctly compares strings", () => {
  assert.strictEqual(constantTimeEqual("secret123", "secret123"), true);
  assert.strictEqual(constantTimeEqual("secret123", "secret124"), false);
  assert.strictEqual(constantTimeEqual("secret123", "short"), false);
});
