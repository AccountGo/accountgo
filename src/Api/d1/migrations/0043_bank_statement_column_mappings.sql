-- #447 Phase B: remembered CSV column maps per bank account + header signature.

CREATE TABLE IF NOT EXISTS "BankStatementColumnMapping" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_BankStatementColumnMapping" PRIMARY KEY AUTOINCREMENT,
    "CompanyId" INTEGER NOT NULL,
    "BankId" INTEGER NOT NULL,
    "HeaderSignature" TEXT NOT NULL,
    "ColumnMapJson" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    CONSTRAINT "FK_BankStatementColumnMapping_Bank_BankId" FOREIGN KEY ("BankId") REFERENCES "Bank" ("Id")
);
CREATE UNIQUE INDEX IF NOT EXISTS "UX_BankStatementColumnMapping_Company_Bank_Header"
    ON "BankStatementColumnMapping" ("CompanyId", "BankId", "HeaderSignature");
CREATE INDEX IF NOT EXISTS "IX_BankStatementColumnMapping_CompanyId" ON "BankStatementColumnMapping" ("CompanyId");
CREATE INDEX IF NOT EXISTS "IX_BankStatementColumnMapping_BankId" ON "BankStatementColumnMapping" ("BankId");
