import test from "node:test";
import assert from "node:assert";
import { buildContainerEnvVars, usesD1Copy } from "../container-env.ts";

test("usesD1Copy is true for SQLite and when no provider is set", () => {
  assert.equal(usesD1Copy({}), true);
  assert.equal(usesD1Copy({ DatabaseProvider: "Sqlite" }), true);
});

test("usesD1Copy is false for Postgres", () => {
  assert.equal(usesD1Copy({ DatabaseProvider: "Postgres" }), false);
  assert.equal(usesD1Copy({ DatabaseProvider: "postgresql" }), false);
  assert.equal(usesD1Copy({ DatabaseProvider: " Postgres " }), false);
});

test("buildContainerEnvVars asks SQLite containers to wait for the D1 restore", () => {
  const vars = buildContainerEnvVars({ DatabaseProvider: "Sqlite" }, 8001);
  assert.equal(vars.RESTORE_FROM_D1, "true");
  assert.equal(vars.DatabaseProvider, "Sqlite");
});

test("buildContainerEnvVars passes Postgres settings through without a D1 restore", () => {
  const vars = buildContainerEnvVars(
    { DatabaseProvider: "Postgres", ConnectionStrings__PostgresConnection: "Host=db", DB: {} },
    8001
  );
  assert.equal(vars.RESTORE_FROM_D1, undefined);
  assert.equal(vars.DatabaseProvider, "Postgres");
  assert.equal(vars.ConnectionStrings__PostgresConnection, "Host=db");
  assert.equal(vars.DB, undefined);
});
