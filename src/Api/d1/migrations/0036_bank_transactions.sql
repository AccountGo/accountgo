-- #413 Phase 2A: BankTransaction table.
-- Plan called this part of 0034; 0035 (bill voids) shipped first, so this is 0036.
-- Never introduce a 0034 — it would run before 0036 on a fresh database.
-- Reconciliation tables land in a later migration (2B / 0037+).
-- One statement per line; safe under SQLITE_LIMIT_COMPOUND_SELECT=1.
-- No opening-balance backfill: production has no OB headers yet; new posts set
-- SourceDocumentId to the BankTransaction id like every other kind.

CREATE TABLE IF NOT EXISTS "BankTransaction" ("Id" INTEGER NOT NULL CONSTRAINT "PK_BankTransaction" PRIMARY KEY AUTOINCREMENT, "CompanyId" INTEGER NOT NULL, "Kind" INTEGER NOT NULL, "BankId" INTEGER NOT NULL, "ToBankId" INTEGER NULL, "Date" TEXT NOT NULL, "Amount" NUMERIC NOT NULL, "ContraAccountId" INTEGER NULL, "PartyId" INTEGER NULL, "No" TEXT NOT NULL, "ReferenceNo" TEXT NULL, "Memo" TEXT NULL, "GeneralLedgerHeaderId" INTEGER NULL, "VoidGeneralLedgerHeaderId" INTEGER NULL, CONSTRAINT "FK_BankTransaction_Bank_BankId" FOREIGN KEY ("BankId") REFERENCES "Bank" ("Id"), CONSTRAINT "FK_BankTransaction_Bank_ToBankId" FOREIGN KEY ("ToBankId") REFERENCES "Bank" ("Id"), CONSTRAINT "FK_BankTransaction_Account_ContraAccountId" FOREIGN KEY ("ContraAccountId") REFERENCES "Account" ("Id"), CONSTRAINT "FK_BankTransaction_Party_PartyId" FOREIGN KEY ("PartyId") REFERENCES "Party" ("Id"), CONSTRAINT "FK_BankTransaction_GeneralLedgerHeader_GeneralLedgerHeaderId" FOREIGN KEY ("GeneralLedgerHeaderId") REFERENCES "GeneralLedgerHeader" ("Id"), CONSTRAINT "FK_BankTransaction_GeneralLedgerHeader_VoidGeneralLedgerHeaderId" FOREIGN KEY ("VoidGeneralLedgerHeaderId") REFERENCES "GeneralLedgerHeader" ("Id"));
CREATE INDEX IF NOT EXISTS "IX_BankTransaction_CompanyId" ON "BankTransaction" ("CompanyId");
CREATE INDEX IF NOT EXISTS "IX_BankTransaction_Company_Bank_Date" ON "BankTransaction" ("CompanyId", "BankId", "Date");
CREATE INDEX IF NOT EXISTS "IX_BankTransaction_GeneralLedgerHeaderId" ON "BankTransaction" ("GeneralLedgerHeaderId");
CREATE UNIQUE INDEX IF NOT EXISTS "UX_BankTransaction_Company_No" ON "BankTransaction" ("CompanyId", "No");
