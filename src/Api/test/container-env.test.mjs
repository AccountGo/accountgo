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

test("buildContainerEnvVars forces ExtractionHost per pool when DOCUMENT_CAPTURE_EXTRACTION_POOL=paid", () => {
  const baseEnv = {
    DatabaseProvider: "Postgres",
    DOCUMENT_CAPTURE_EXTRACTION_POOL: "paid",
    DocumentCapture__Enabled: "true",
    DocumentCapture__Extractor: "local-ocr",
    // Even if ops sets ExtractionHost=true (or a case variant), V1 and V2 free must get false.
    DocumentCapture__ExtractionHost: "true",
    DOCUMENTCAPTURE__EXTRACTIONHOST: "true",
  };

  const v1Vars = buildContainerEnvVars(baseEnv, 8001, "v1");
  assert.equal(v1Vars.DocumentCapture__ExtractionHost, "false");
  assert.equal(v1Vars.DOCUMENTCAPTURE__EXTRACTIONHOST, undefined);

  const v2FreeVars = buildContainerEnvVars(baseEnv, 8001, "free");
  assert.equal(v2FreeVars.DocumentCapture__ExtractionHost, "false");
  assert.equal(v2FreeVars.DOCUMENTCAPTURE__EXTRACTIONHOST, undefined);

  const v2PaidVars = buildContainerEnvVars(baseEnv, 8001, "paid");
  assert.equal(v2PaidVars.DocumentCapture__ExtractionHost, "true");
  assert.equal(v2PaidVars.DOCUMENTCAPTURE__EXTRACTIONHOST, undefined);
});

test("buildContainerEnvVars leaves ExtractionHost untouched when DOCUMENT_CAPTURE_EXTRACTION_POOL is unset", () => {
  const vars = buildContainerEnvVars(
    { DatabaseProvider: "Postgres", DocumentCapture__Enabled: "true" },
    8001,
    "v1"
  );
  assert.equal(vars.DocumentCapture__ExtractionHost, undefined);
});
