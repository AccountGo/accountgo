-- #436 I4: remembered bank description → vendor + expense category rules.

CREATE TABLE IF NOT EXISTS "BankStatementDescriptionRule" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_BankStatementDescriptionRule" PRIMARY KEY AUTOINCREMENT,
    "CompanyId" INTEGER NOT NULL,
    "DescriptionKey" TEXT NOT NULL,
    "VendorId" INTEGER NULL,
    "ExpenseAccountId" INTEGER NULL,
    "CreatedAt" TEXT NOT NULL,
    "LastUsedAt" TEXT NOT NULL,
    CONSTRAINT "FK_BankStatementDescriptionRule_Vendor_VendorId" FOREIGN KEY ("VendorId") REFERENCES "Vendor" ("Id"),
    CONSTRAINT "FK_BankStatementDescriptionRule_Account_ExpenseAccountId" FOREIGN KEY ("ExpenseAccountId") REFERENCES "Account" ("Id")
);
CREATE UNIQUE INDEX IF NOT EXISTS "UX_BankStatementDescriptionRule_Company_DescriptionKey"
    ON "BankStatementDescriptionRule" ("CompanyId", "DescriptionKey");
CREATE INDEX IF NOT EXISTS "IX_BankStatementDescriptionRule_CompanyId" ON "BankStatementDescriptionRule" ("CompanyId");
CREATE INDEX IF NOT EXISTS "IX_BankStatementDescriptionRule_VendorId" ON "BankStatementDescriptionRule" ("VendorId");
CREATE INDEX IF NOT EXISTS "IX_BankStatementDescriptionRule_ExpenseAccountId" ON "BankStatementDescriptionRule" ("ExpenseAccountId");
