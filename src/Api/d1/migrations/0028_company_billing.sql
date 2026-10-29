-- Provider billing columns on Company (generic — no processor-specific names in values).
-- Mirrors SqliteSchemaUpgrader and the EF migration CompanyBilling.
ALTER TABLE "Company" ADD COLUMN "BillingProvider" TEXT NULL;
ALTER TABLE "Company" ADD COLUMN "BillingCustomerId" TEXT NULL;
ALTER TABLE "Company" ADD COLUMN "BillingSubscriptionId" TEXT NULL;
ALTER TABLE "Company" ADD COLUMN "CurrentPeriodEnd" TEXT NULL;
ALTER TABLE "Company" ADD COLUMN "CancelAtPeriodEnd" INTEGER NOT NULL DEFAULT 0;
ALTER TABLE "Company" ADD COLUMN "BillingManagedManually" INTEGER NOT NULL DEFAULT 0;
