-- #436 I5: Origin on invoices for statement other income; IsSystem on customers for the workspace "Other income" client.

ALTER TABLE "SalesInvoiceHeader" ADD COLUMN "Origin" INTEGER NULL;
ALTER TABLE "Customer" ADD COLUMN "IsSystem" INTEGER NOT NULL DEFAULT 0;
CREATE INDEX IF NOT EXISTS "IX_SalesInvoiceHeader_Company_Origin" ON "SalesInvoiceHeader" ("CompanyId", "Origin");
