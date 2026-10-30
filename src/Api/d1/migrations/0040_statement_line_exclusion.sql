-- #436 I3: exclude statement lines (Personal / Transfer / Duplicate / Other). Excluded lines post nothing.

ALTER TABLE "BankStatementLine" ADD COLUMN "ExcludedAt" TEXT NULL;
ALTER TABLE "BankStatementLine" ADD COLUMN "ExcludedBy" TEXT NULL;
ALTER TABLE "BankStatementLine" ADD COLUMN "ExclusionReason" INTEGER NULL;
ALTER TABLE "BankStatementLine" ADD COLUMN "ExclusionNote" TEXT NULL;
CREATE INDEX IF NOT EXISTS "IX_BankStatementLine_Company_Bank_ExcludedAt" ON "BankStatementLine" ("CompanyId", "BankId", "ExcludedAt");
