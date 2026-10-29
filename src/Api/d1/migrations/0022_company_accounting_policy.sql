ALTER TABLE "CompanySetting" ADD COLUMN "ReportingFramework" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "CompanySetting" ADD COLUMN "TaxRegime" INTEGER NOT NULL DEFAULT 0;
ALTER TABLE "CompanySetting" ADD COLUMN "FiscalYearPattern" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "CompanySetting" ADD COLUMN "FiscalYearStartMonth" INTEGER NULL;
ALTER TABLE "CompanySetting" ADD COLUMN "BooksStartDate" TEXT NOT NULL DEFAULT '1970-01-01';
ALTER TABLE "CompanySetting" ADD COLUMN "IndustryTemplate" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "CompanySetting" ADD COLUMN "InventoryCostingMethod" INTEGER NOT NULL DEFAULT 0;
ALTER TABLE "CompanySetting" ADD COLUMN "CoaTemplateId" TEXT NOT NULL DEFAULT 'legacy-default';
ALTER TABLE "CompanySetting" ADD COLUMN "OnboardingChecklistDismissedAt" TEXT NULL;
ALTER TABLE "CompanySetting" ADD COLUMN "OnboardingChecklistJson" TEXT NULL;

UPDATE "CompanySetting"
SET "BooksStartDate" = COALESCE(
    (SELECT date(f."StartDate")
     FROM "FinancialYear" f
     WHERE f."CompanyId" = "CompanySetting"."CompanyId"
     ORDER BY f."IsActive" DESC, f."StartDate"
     LIMIT 1),
    (SELECT date(c."CreatedAt")
     FROM "Company" c
     WHERE c."Id" = "CompanySetting"."CompanyId"),
    '1970-01-01');
