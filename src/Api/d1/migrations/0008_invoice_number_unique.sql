-- Invoice numbers are unique per workspace. Blank numbers become NULL (not numbered yet), which never clash.
-- Fails, and so blocks the deploy, if a workspace already has two invoices with the same number.
UPDATE "SalesInvoiceHeader" SET "No" = NULL WHERE "No" IS NOT NULL AND TRIM("No") = '';
CREATE UNIQUE INDEX IF NOT EXISTS "IX_SalesInvoiceHeader_CompanyId_No" ON "SalesInvoiceHeader" ("CompanyId", "No");
