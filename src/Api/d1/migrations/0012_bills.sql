-- Purchases & Bills. Bills get due dates, notes, the vendor as approved, and lines that keep their own text, the
-- account they are booked to and their tax. Vendors can be archived. Vendor payments record where they were paid
-- from. Voiding a bill or an invoice now reverses what approving or issuing posted.
ALTER TABLE "PurchaseInvoiceHeader" ADD COLUMN "DueDate" TEXT NULL;
ALTER TABLE "PurchaseInvoiceHeader" ADD COLUMN "Notes" TEXT NULL;
ALTER TABLE "PurchaseInvoiceHeader" ADD COLUMN "BillFromJson" TEXT NULL;
ALTER TABLE "PurchaseInvoiceHeader" ADD COLUMN "VoidGeneralLedgerHeaderId" INTEGER NULL;
ALTER TABLE "PurchaseInvoiceLine" ADD COLUMN "Description" TEXT NULL;
ALTER TABLE "PurchaseInvoiceLine" ADD COLUMN "AccountId" INTEGER NULL;
ALTER TABLE "PurchaseInvoiceLine" ADD COLUMN "TaxId" INTEGER NULL;
ALTER TABLE "PurchaseInvoiceLine" ADD COLUMN "TaxAmount" decimal(18, 2) NOT NULL DEFAULT 0;
ALTER TABLE "Vendor" ADD COLUMN "ArchivedAt" TEXT NULL;
ALTER TABLE "VendorPayment" ADD COLUMN "AccountId" INTEGER NULL;
ALTER TABLE "VendorPayment" ADD COLUMN "PaymentMethod" TEXT NULL;
ALTER TABLE "VendorPayment" ADD COLUMN "ReferenceNo" TEXT NULL;
ALTER TABLE "VendorPayment" ADD COLUMN "Memo" TEXT NULL;
ALTER TABLE "SalesInvoiceHeader" ADD COLUMN "VoidGeneralLedgerHeaderId" INTEGER NULL;

-- Bills saved before this: posted ones are approved (Open), the rest drafts. Lines keep the item's text, and posted
-- lines the inventory account they were posted to. Their tax stays 0: their totals were posted without tax on top.
UPDATE "PurchaseInvoiceHeader" SET "Status" = CASE WHEN "GeneralLedgerHeaderId" IS NULL THEN 0 ELSE 1 END
WHERE "Status" IS NULL;
UPDATE "PurchaseInvoiceLine" SET "Description" = (
    SELECT "Item"."Description" FROM "Item" WHERE "Item"."Id" = "PurchaseInvoiceLine"."ItemId")
WHERE "Description" IS NULL;
UPDATE "PurchaseInvoiceLine" SET "AccountId" = (
    SELECT "Item"."InventoryAccountId" FROM "Item" WHERE "Item"."Id" = "PurchaseInvoiceLine"."ItemId")
WHERE "AccountId" IS NULL
  AND "PurchaseInvoiceHeaderId" IN (SELECT "Id" FROM "PurchaseInvoiceHeader" WHERE "GeneralLedgerHeaderId" IS NOT NULL);

-- Invoices voided before this kept their posting: each gets the entry that reverses it, dated as the original.
INSERT INTO "GeneralLedgerHeader" ("CompanyId", "Date", "DocumentType", "Description")
SELECT i."CompanyId", g."Date", g."DocumentType", 'Void of invoice ' || COALESCE(i."No", i."Id")
FROM "SalesInvoiceHeader" i JOIN "GeneralLedgerHeader" g ON g."Id" = i."GeneralLedgerHeaderId"
WHERE i."Status" = 4 AND i."VoidGeneralLedgerHeaderId" IS NULL;
UPDATE "SalesInvoiceHeader" SET "VoidGeneralLedgerHeaderId" = (
    SELECT MAX(g."Id") FROM "GeneralLedgerHeader" g
    WHERE g."CompanyId" = "SalesInvoiceHeader"."CompanyId"
      AND g."Description" = 'Void of invoice ' || COALESCE("SalesInvoiceHeader"."No", "SalesInvoiceHeader"."Id"))
WHERE "Status" = 4 AND "GeneralLedgerHeaderId" IS NOT NULL AND "VoidGeneralLedgerHeaderId" IS NULL;
INSERT INTO "GeneralLedgerLine" ("CompanyId", "GeneralLedgerHeaderId", "AccountId", "DrCr", "Amount")
SELECT l."CompanyId", i."VoidGeneralLedgerHeaderId", l."AccountId", CASE l."DrCr" WHEN 1 THEN 2 ELSE 1 END, l."Amount"
FROM "SalesInvoiceHeader" i JOIN "GeneralLedgerLine" l ON l."GeneralLedgerHeaderId" = i."GeneralLedgerHeaderId"
WHERE i."VoidGeneralLedgerHeaderId" IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM "GeneralLedgerLine" x WHERE x."GeneralLedgerHeaderId" = i."VoidGeneralLedgerHeaderId");

-- "Pay Bills", for the Accountant role. Startup adds the same when it is missing.
INSERT INTO "SecurityPermission" ("Name", "DisplayName", "SecurityGroupId")
SELECT 'purchases.payments.create', 'Pay Bills', "Id" FROM "SecurityGroup" WHERE "Name" = 'Purchasing'
  AND NOT EXISTS (SELECT 1 FROM "SecurityPermission" WHERE "Name" = 'purchases.payments.create');
INSERT INTO "SecurityRolePermission" ("SecurityRoleId", "SecurityPermissionId")
SELECT r."Id", p."Id" FROM "SecurityRole" r, "SecurityPermission" p
WHERE r."Name" = 'Accountant' AND p."Name" = 'purchases.payments.create'
  AND NOT EXISTS (SELECT 1 FROM "SecurityRolePermission" x WHERE x."SecurityRoleId" = r."Id" AND x."SecurityPermissionId" = p."Id");
