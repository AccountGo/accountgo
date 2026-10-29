-- #413 Phase 2B: bank reconciliation tables and GL line stamp.
-- Plan called this part of 0034; 0035/0036 shipped first — never add a 0034.
-- One statement per line; safe under SQLITE_LIMIT_COMPOUND_SELECT=1.

CREATE TABLE IF NOT EXISTS "BankReconciliation" ("Id" INTEGER NOT NULL CONSTRAINT "PK_BankReconciliation" PRIMARY KEY AUTOINCREMENT, "CompanyId" INTEGER NOT NULL, "BankId" INTEGER NOT NULL, "StatementEndDate" TEXT NOT NULL, "StatementEndingBalance" NUMERIC NOT NULL, "BeginningBalance" NUMERIC NOT NULL, "Status" INTEGER NOT NULL, "CreatedBy" TEXT NULL, "CreatedAt" TEXT NOT NULL, "FinishedBy" TEXT NULL, "FinishedAt" TEXT NULL, "ReviewedBy" TEXT NULL, "ReviewedAt" TEXT NULL, "UndoneBy" TEXT NULL, "UndoneAt" TEXT NULL, "UndoReason" TEXT NULL, "Version" INTEGER NOT NULL DEFAULT 1, CONSTRAINT "FK_BankReconciliation_Bank_BankId" FOREIGN KEY ("BankId") REFERENCES "Bank" ("Id"));
CREATE INDEX IF NOT EXISTS "IX_BankReconciliation_CompanyId" ON "BankReconciliation" ("CompanyId");
CREATE INDEX IF NOT EXISTS "IX_BankReconciliation_Company_Bank_EndDate" ON "BankReconciliation" ("CompanyId", "BankId", "StatementEndDate");
CREATE UNIQUE INDEX IF NOT EXISTS "UX_BankReconciliation_OneDraft" ON "BankReconciliation" ("CompanyId", "BankId") WHERE "Status" = 1;

CREATE TABLE IF NOT EXISTS "BankReconciliationItem" ("Id" INTEGER NOT NULL CONSTRAINT "PK_BankReconciliationItem" PRIMARY KEY AUTOINCREMENT, "CompanyId" INTEGER NOT NULL, "BankReconciliationId" INTEGER NOT NULL, "GeneralLedgerLineId" INTEGER NOT NULL, "Amount" NUMERIC NOT NULL, "DrCr" INTEGER NOT NULL, CONSTRAINT "FK_BankReconciliationItem_BankReconciliation_BankReconciliationId" FOREIGN KEY ("BankReconciliationId") REFERENCES "BankReconciliation" ("Id"), CONSTRAINT "FK_BankReconciliationItem_GeneralLedgerLine_GeneralLedgerLineId" FOREIGN KEY ("GeneralLedgerLineId") REFERENCES "GeneralLedgerLine" ("Id"));
CREATE INDEX IF NOT EXISTS "IX_BankReconciliationItem_CompanyId" ON "BankReconciliationItem" ("CompanyId");
CREATE INDEX IF NOT EXISTS "IX_BankReconciliationItem_BankReconciliationId" ON "BankReconciliationItem" ("BankReconciliationId");
CREATE UNIQUE INDEX IF NOT EXISTS "UX_BankReconciliationItem_Recon_Line" ON "BankReconciliationItem" ("BankReconciliationId", "GeneralLedgerLineId");

ALTER TABLE "GeneralLedgerLine" ADD COLUMN "BankReconciliationId" INTEGER NULL REFERENCES "BankReconciliation" ("Id");
CREATE INDEX IF NOT EXISTS "IX_GeneralLedgerLine_Company_BankReconciliationId" ON "GeneralLedgerLine" ("CompanyId", "BankReconciliationId");
