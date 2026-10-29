-- #413 Phase 3A: bank statement imports and lines (CSV / OFX), matched to GL lines.
-- One statement per line; safe under SQLITE_LIMIT_COMPOUND_SELECT=1.
-- Statement lines never post to the ledger; they only link to existing GeneralLedgerLine rows.

CREATE TABLE IF NOT EXISTS "BankStatementImport" ("Id" INTEGER NOT NULL CONSTRAINT "PK_BankStatementImport" PRIMARY KEY AUTOINCREMENT, "CompanyId" INTEGER NOT NULL, "BankId" INTEGER NOT NULL, "Source" INTEGER NOT NULL, "FileName" TEXT NULL, "ImportedAt" TEXT NOT NULL, "ImportedBy" TEXT NULL, "ColumnMapJson" TEXT NULL, CONSTRAINT "FK_BankStatementImport_Bank_BankId" FOREIGN KEY ("BankId") REFERENCES "Bank" ("Id"));
CREATE INDEX IF NOT EXISTS "IX_BankStatementImport_CompanyId" ON "BankStatementImport" ("CompanyId");
CREATE INDEX IF NOT EXISTS "IX_BankStatementImport_Company_Bank" ON "BankStatementImport" ("CompanyId", "BankId");
CREATE INDEX IF NOT EXISTS "IX_BankStatementImport_BankId" ON "BankStatementImport" ("BankId");

CREATE TABLE IF NOT EXISTS "BankStatementLine" ("Id" INTEGER NOT NULL CONSTRAINT "PK_BankStatementLine" PRIMARY KEY AUTOINCREMENT, "CompanyId" INTEGER NOT NULL, "BankId" INTEGER NOT NULL, "BankStatementImportId" INTEGER NOT NULL, "Source" INTEGER NOT NULL, "ExternalId" TEXT NULL, "PostedDate" TEXT NOT NULL, "Amount" NUMERIC NOT NULL, "Payee" TEXT NULL, "Memo" TEXT NULL, "Reference" TEXT NULL, "MatchedGeneralLedgerLineId" INTEGER NULL, "CreatedAt" TEXT NOT NULL, CONSTRAINT "FK_BankStatementLine_Bank_BankId" FOREIGN KEY ("BankId") REFERENCES "Bank" ("Id"), CONSTRAINT "FK_BankStatementLine_BankStatementImport_BankStatementImportId" FOREIGN KEY ("BankStatementImportId") REFERENCES "BankStatementImport" ("Id"), CONSTRAINT "FK_BankStatementLine_GeneralLedgerLine_MatchedGeneralLedgerLineId" FOREIGN KEY ("MatchedGeneralLedgerLineId") REFERENCES "GeneralLedgerLine" ("Id"));
CREATE INDEX IF NOT EXISTS "IX_BankStatementLine_CompanyId" ON "BankStatementLine" ("CompanyId");
CREATE INDEX IF NOT EXISTS "IX_BankStatementLine_BankStatementImportId" ON "BankStatementLine" ("BankStatementImportId");
CREATE INDEX IF NOT EXISTS "IX_BankStatementLine_MatchedGeneralLedgerLineId" ON "BankStatementLine" ("MatchedGeneralLedgerLineId");
CREATE INDEX IF NOT EXISTS "IX_BankStatementLine_BankId" ON "BankStatementLine" ("BankId");
CREATE INDEX IF NOT EXISTS "IX_BankStatementLine_Company_Bank_PostedDate" ON "BankStatementLine" ("CompanyId", "BankId", "PostedDate");
CREATE UNIQUE INDEX IF NOT EXISTS "UX_BankStatementLine_Company_Bank_Source_External" ON "BankStatementLine" ("CompanyId", "BankId", "Source", "ExternalId") WHERE "ExternalId" IS NOT NULL;
