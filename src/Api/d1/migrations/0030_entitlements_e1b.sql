-- E1b: DisabledModulesJson (owner opt-outs) + PaidAccessEndedAt; backfill + plan/tier normalize.
-- Mirrors SqliteSchemaUpgrader and the EF migration EntitlementsE1b.
-- One UPDATE per statement (D1 SQLITE_LIMIT_COMPOUND_SELECT=1). Do not drop ActiveModulesJson.
-- Per-plan backfills require json_valid + non-empty arrays; empty/invalid ActiveModulesJson
-- fall through to the final SET DisabledModulesJson = '[]' (legacy: empty = all modules on).

ALTER TABLE "Company" ADD COLUMN "DisabledModulesJson" TEXT NULL;
ALTER TABLE "Company" ADD COLUMN "PaidAccessEndedAt" TEXT NULL;

-- Backfill DisabledModulesJson = plan modules − ActiveModulesJson (required cores never disabled).
-- Accounting family (sales, financials, purchases, reports); sales is required.
UPDATE "Company"
SET "DisabledModulesJson" = (
  SELECT COALESCE(json_group_array(m.value), '[]')
  FROM json_each('["sales","financials","purchases","reports"]') AS m
  WHERE lower(m.value) != 'sales'
    AND NOT EXISTS (
      SELECT 1 FROM json_each("Company"."ActiveModulesJson") AS a
      WHERE lower(a.value) = lower(m.value)
    )
)
WHERE "DisabledModulesJson" IS NULL
  AND "ActiveModulesJson" IS NOT NULL
  AND json_valid("ActiveModulesJson")
  AND json_array_length("ActiveModulesJson") > 0
  AND lower(COALESCE("Plan", '')) IN ('accounting', 'accounting_free', 'accounting_pro');

-- Invoicing family (sales, purchases); both required — disabled list is empty when ActiveModules was set.
UPDATE "Company"
SET "DisabledModulesJson" = '[]'
WHERE "DisabledModulesJson" IS NULL
  AND "ActiveModulesJson" IS NOT NULL
  AND json_valid("ActiveModulesJson")
  AND json_array_length("ActiveModulesJson") > 0
  AND lower(COALESCE("Plan", '')) IN ('invoicing', 'invoicing_free', 'invoicing_pro');

-- Enterprise family.
UPDATE "Company"
SET "DisabledModulesJson" = (
  SELECT COALESCE(json_group_array(m.value), '[]')
  FROM json_each('["sales","financials","purchases","reports","inventory","donations","audit"]') AS m
  WHERE lower(m.value) != 'sales'
    AND NOT EXISTS (
      SELECT 1 FROM json_each("Company"."ActiveModulesJson") AS a
      WHERE lower(a.value) = lower(m.value)
    )
)
WHERE "DisabledModulesJson" IS NULL
  AND "ActiveModulesJson" IS NOT NULL
  AND json_valid("ActiveModulesJson")
  AND json_array_length("ActiveModulesJson") > 0
  AND lower(COALESCE("Plan", '')) = 'enterprise';

-- Unknown / null plan treated like accounting modules for backfill.
UPDATE "Company"
SET "DisabledModulesJson" = (
  SELECT COALESCE(json_group_array(m.value), '[]')
  FROM json_each('["sales","financials","purchases","reports"]') AS m
  WHERE lower(m.value) != 'sales'
    AND NOT EXISTS (
      SELECT 1 FROM json_each("Company"."ActiveModulesJson") AS a
      WHERE lower(a.value) = lower(m.value)
    )
)
WHERE "DisabledModulesJson" IS NULL
  AND "ActiveModulesJson" IS NOT NULL
  AND json_valid("ActiveModulesJson")
  AND json_array_length("ActiveModulesJson") > 0
  AND lower(COALESCE("Plan", '')) NOT IN (
    'accounting', 'accounting_free', 'accounting_pro',
    'invoicing', 'invoicing_free', 'invoicing_pro',
    'enterprise'
  );

-- Empty array or invalid JSON: legacy GetActiveModules treated these as full plan (all modules on).
UPDATE "Company"
SET "DisabledModulesJson" = '[]'
WHERE "DisabledModulesJson" IS NULL
  AND "ActiveModulesJson" IS NOT NULL;

UPDATE "Company" SET "Plan" = 'invoicing_free' WHERE lower("Plan") = 'invoicing';
UPDATE "Company" SET "Plan" = 'accounting_free' WHERE lower("Plan") = 'accounting';
UPDATE "Company" SET "Tier" = NULL WHERE "Tier" IS NOT NULL AND lower("Tier") != 'comp';
