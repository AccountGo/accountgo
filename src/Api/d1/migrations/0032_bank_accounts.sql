-- #413 Phase 1A: bank account columns, backfill, name/default clean-up, indexes, D11 triggers.
-- Steps that allocate dedicated cash accounts (summary / shared / null AccountId) run in
-- SqliteSchemaUpgrader for local/container DBs. On D1, run scripts/d1/bank-preflight.sql
-- first and fix any rows with a reviewed one-off migration so this file sees zero violations.
-- One statement per line; safe under SQLITE_LIMIT_COMPOUND_SELECT=1.

ALTER TABLE "Bank" ADD COLUMN "RoutingNumber" TEXT NULL;
ALTER TABLE "Bank" ADD COLUMN "Iban" TEXT NULL;
ALTER TABLE "Bank" ADD COLUMN "Swift" TEXT NULL;
ALTER TABLE "Bank" ADD COLUMN "IsDefaultForReceipts" INTEGER NOT NULL DEFAULT 0;
ALTER TABLE "Bank" ADD COLUMN "IsDefaultForPayments" INTEGER NOT NULL DEFAULT 0;
ALTER TABLE "Bank" ADD COLUMN "OpeningBalance" NUMERIC NULL;
ALTER TABLE "Bank" ADD COLUMN "OpeningBalanceDate" TEXT NULL;
ALTER TABLE "Bank" ADD COLUMN "OpeningBalanceGeneralLedgerHeaderId" INTEGER NULL;
ALTER TABLE "Bank" ADD COLUMN "ArchivedAt" TEXT NULL;
ALTER TABLE "Bank" ADD COLUMN "CurrencyCode" TEXT NULL;

UPDATE "Bank" SET "IsDefaultForReceipts" = 1 WHERE "IsDefault" = 1 AND "IsDefaultForReceipts" = 0 AND NOT EXISTS (SELECT 1 FROM "Bank" AS other WHERE other."CompanyId" = "Bank"."CompanyId" AND other."IsDefaultForReceipts" = 1);
UPDATE "Bank" SET "ArchivedAt" = CURRENT_TIMESTAMP WHERE "IsActive" = 0 AND "ArchivedAt" IS NULL;

UPDATE "Bank" SET "Name" = (SELECT a."AccountName" FROM "Account" AS a WHERE a."Id" = "Bank"."AccountId" AND a."CompanyId" = "Bank"."CompanyId") WHERE ("Name" IS NULL OR TRIM("Name") = '') AND "AccountId" IS NOT NULL AND EXISTS (SELECT 1 FROM "Account" AS a WHERE a."Id" = "Bank"."AccountId" AND a."CompanyId" = "Bank"."CompanyId" AND a."AccountName" IS NOT NULL AND TRIM(a."AccountName") != '');
UPDATE "Bank" SET "Name" = 'Bank ' || CAST("Id" AS TEXT) WHERE "Name" IS NULL OR TRIM("Name") = '';

UPDATE "Bank" SET "Name" = "Name" || ' (' || CAST("Id" AS TEXT) || ')' WHERE EXISTS (SELECT 1 FROM "Bank" AS other WHERE other."CompanyId" = "Bank"."CompanyId" AND lower(other."Name") = lower("Bank"."Name") AND other."Id" < "Bank"."Id");

UPDATE "Bank" SET "IsDefaultForReceipts" = 0 WHERE "IsDefaultForReceipts" = 1 AND "Id" NOT IN (SELECT "Id" FROM (SELECT MIN("Id") AS "Id" FROM "Bank" WHERE "IsDefaultForReceipts" = 1 GROUP BY "CompanyId"));
UPDATE "Bank" SET "IsDefaultForPayments" = 0 WHERE "IsDefaultForPayments" = 1 AND "Id" NOT IN (SELECT "Id" FROM (SELECT MIN("Id") AS "Id" FROM "Bank" WHERE "IsDefaultForPayments" = 1 GROUP BY "CompanyId"));

-- RAISE() is trigger-only in SQLite; fail the migration with a CHECK when any AccountId is still NULL.
CREATE TABLE "__BankAccountIdGuard" ("Ok" INTEGER NOT NULL CHECK ("Ok" = 1));
INSERT INTO "__BankAccountIdGuard" ("Ok") SELECT CASE WHEN EXISTS (SELECT 1 FROM "Bank" WHERE "AccountId" IS NULL) THEN 0 ELSE 1 END;
DROP TABLE "__BankAccountIdGuard";

CREATE UNIQUE INDEX IF NOT EXISTS "UX_Bank_Company_Account" ON "Bank" ("CompanyId", "AccountId");
CREATE UNIQUE INDEX IF NOT EXISTS "UX_Bank_Company_Name" ON "Bank" ("CompanyId", "Name" COLLATE NOCASE);
CREATE UNIQUE INDEX IF NOT EXISTS "UX_Bank_DefaultReceipts" ON "Bank" ("CompanyId") WHERE "IsDefaultForReceipts" = 1;
CREATE UNIQUE INDEX IF NOT EXISTS "UX_Bank_DefaultPayments" ON "Bank" ("CompanyId") WHERE "IsDefaultForPayments" = 1;

DROP TRIGGER IF EXISTS "trg_Bank_AccountId_required_insert";
CREATE TRIGGER "trg_Bank_AccountId_required_insert" BEFORE INSERT ON "Bank" BEGIN SELECT RAISE(ABORT, 'Bank.AccountId is required') WHERE NEW."AccountId" IS NULL; END;
DROP TRIGGER IF EXISTS "trg_Bank_AccountId_required_update";
CREATE TRIGGER "trg_Bank_AccountId_required_update" BEFORE UPDATE ON "Bank" BEGIN SELECT RAISE(ABORT, 'Bank.AccountId is required') WHERE NEW."AccountId" IS NULL; END;
