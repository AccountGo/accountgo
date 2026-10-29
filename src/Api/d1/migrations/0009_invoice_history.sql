-- Invoices keep what they were issued with when customers and items are edited later.
-- Each line stores its own text; each issued invoice stores its "Bill to". Customers and items could not be edited
-- before this, so what they say now is what the existing invoices were issued with.
ALTER TABLE "SalesInvoiceLine" ADD COLUMN "Description" TEXT NULL;
ALTER TABLE "SalesInvoiceHeader" ADD COLUMN "BillToJson" TEXT NULL;
UPDATE "SalesInvoiceLine" SET "Description" = (
    SELECT "Item"."Description" FROM "Item" WHERE "Item"."Id" = "SalesInvoiceLine"."ItemId")
WHERE "Description" IS NULL;
-- Issued invoices: Open (1), Overdue (2), Closed (3), and posted ones that were voided (4) or predate statuses (NULL).
-- Drafts keep following the customer.
UPDATE "SalesInvoiceHeader" SET "BillToJson" = (
    SELECT json_object(
        'name', COALESCE("Party"."Name", 'Unknown'),
        'email', "Party"."Email",
        'phone', "Party"."Phone",
        'addressLines', json_array("Party"."CompanyStreet", "Party"."CompanyProvince", "Party"."CompanyPostalCode", "Party"."CompanyCountry"))
    FROM "Customer" JOIN "Party" ON "Party"."Id" = "Customer"."PartyId"
    WHERE "Customer"."Id" = "SalesInvoiceHeader"."CustomerId")
WHERE "BillToJson" IS NULL
  AND ("Status" IN (1, 2, 3) OR (("Status" IS NULL OR "Status" = 4) AND "GeneralLedgerHeaderId" IS NOT NULL));
