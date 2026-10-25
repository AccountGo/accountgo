ALTER TABLE "SalesInvoiceLine" ADD COLUMN "SalesAccountId" INTEGER NULL REFERENCES "Account" ("Id");
