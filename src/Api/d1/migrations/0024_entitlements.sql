-- Plan tier (null = free) and metered usage events for plan limits.
-- Mirrors SqliteSchemaUpgrader (Company.Tier, EntitlementUsageTableSql) and the EF migration Entitlements.
ALTER TABLE "Company" ADD COLUMN "Tier" TEXT NULL;

CREATE TABLE IF NOT EXISTS "EntitlementUsage" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_EntitlementUsage" PRIMARY KEY AUTOINCREMENT,
    "CompanyId" INTEGER NOT NULL,
    "Resource" TEXT NOT NULL,
    "EntityId" INTEGER NULL,
    "OccurredAt" TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS "IX_EntitlementUsage_CompanyId_Resource_OccurredAt" ON "EntitlementUsage" ("CompanyId", "Resource", "OccurredAt");
