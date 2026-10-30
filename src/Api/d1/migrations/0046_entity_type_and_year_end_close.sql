-- #455 Phase 2b-1: entity type, equity close accounts, fiscal year closed fields.
-- One statement per line; safe under SQLITE_LIMIT_COMPOUND_SELECT=1.

ALTER TABLE "CompanySetting" ADD COLUMN "EntityType" INTEGER NOT NULL DEFAULT 3;

ALTER TABLE "FinancialYear" ADD COLUMN "ClosedAt" TEXT NULL;
ALTER TABLE "FinancialYear" ADD COLUMN "ClosedBy" TEXT NULL;
ALTER TABLE "FinancialYear" ADD COLUMN "ClosingGeneralLedgerHeaderId" INTEGER NULL;

INSERT INTO "Account" ("CompanyId", "AccountCode", "AccountName", "ParentAccountId", "AccountClassId", "DrOrCrSide", "IsCash", "IsContraAccount")
SELECT c."Id", '30800', 'Owner''s Drawings', parent."Id", cls."Id", 1, 0, 0
FROM "Company" c
JOIN "AccountClass" cls ON cls."Name" = 'Equity'
JOIN "Account" parent ON parent."CompanyId" = c."Id" AND parent."AccountCode" = '30000'
WHERE NOT EXISTS (
    SELECT 1 FROM "Account" a WHERE a."CompanyId" = c."Id" AND a."AccountCode" = '30800'
);

INSERT INTO "Account" ("CompanyId", "AccountCode", "AccountName", "ParentAccountId", "AccountClassId", "DrOrCrSide", "IsCash", "IsContraAccount")
SELECT c."Id", '30810', 'Dividends Declared', parent."Id", cls."Id", 1, 0, 0
FROM "Company" c
JOIN "AccountClass" cls ON cls."Name" = 'Equity'
JOIN "Account" parent ON parent."CompanyId" = c."Id" AND parent."AccountCode" = '30000'
WHERE NOT EXISTS (
    SELECT 1 FROM "Account" a WHERE a."CompanyId" = c."Id" AND a."AccountCode" = '30810'
);

INSERT INTO "Account" ("CompanyId", "AccountCode", "AccountName", "ParentAccountId", "AccountClassId", "DrOrCrSide", "IsCash", "IsContraAccount")
SELECT c."Id", '30820', 'Prior Period Adjustments', parent."Id", cls."Id", 2, 0, 0
FROM "Company" c
JOIN "AccountClass" cls ON cls."Name" = 'Equity'
JOIN "Account" parent ON parent."CompanyId" = c."Id" AND parent."AccountCode" = '30000'
WHERE NOT EXISTS (
    SELECT 1 FROM "Account" a WHERE a."CompanyId" = c."Id" AND a."AccountCode" = '30820'
);

UPDATE "GeneralLedgerSetting"
SET "PermanentAccountId" = (
    SELECT a."Id" FROM "Account" a
    WHERE a."CompanyId" = "GeneralLedgerSetting"."CompanyId" AND a."AccountCode" = '30300'
)
WHERE "PermanentAccountId" IS NULL
  AND EXISTS (
    SELECT 1 FROM "Account" a
    WHERE a."CompanyId" = "GeneralLedgerSetting"."CompanyId" AND a."AccountCode" = '30300'
);
