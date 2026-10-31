import test from "node:test";
import assert from "node:assert";
import { ContainerConfigError, buildContainerEnvVars, requirePersistentDatabase } from "../container-env.ts";

test("requirePersistentDatabase accepts Postgres", () => {
  requirePersistentDatabase({ DatabaseProvider: "Postgres" });
  requirePersistentDatabase({ DatabaseProvider: "postgresql" });
  requirePersistentDatabase({ DatabaseProvider: " Postgres " });
});

test("requirePersistentDatabase refuses SQLite or no provider", () => {
  assert.throws(() => requirePersistentDatabase({ DatabaseProvider: "Sqlite" }), ContainerConfigError);
  assert.throws(() => requirePersistentDatabase({}), /unset/);
});

test("buildContainerEnvVars passes string settings through and drops bindings", () => {
  const vars = buildContainerEnvVars(
    { DatabaseProvider: "Postgres", ConnectionStrings__PostgresConnection: "Host=db", SOME_BINDING: {} },
    8001
  );
  assert.equal(vars.ASPNETCORE_URLS, "http://+:8001");
  assert.equal(vars.DatabaseProvider, "Postgres");
  assert.equal(vars.ConnectionStrings__PostgresConnection, "Host=db");
  assert.equal(vars.SOME_BINDING, undefined);
  assert.equal(vars.RESTORE_FROM_D1, undefined);
  // The container warms its heavy paths before the build check lets traffic in.
  assert.equal(vars.WARMUP_ON_START, "true");
  // The image's .NET tuning must reach containers started with an explicit env (V2).
  assert.equal(vars.DOTNET_gcServer, "0");
  assert.equal(vars.DOTNET_TieredPGO, "0");
});
