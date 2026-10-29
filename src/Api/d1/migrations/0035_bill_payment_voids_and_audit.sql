-- #415: Bill immutability, payment voids, audit trail, and payable account preservation.
-- One statement per line; safe under SQLITE_LIMIT_COMPOUND_SELECT=1.

ALTER TABLE "PurchaseInvoiceHeader" ADD COLUMN "PayableAccountId" INTEGER NULL;
ALTER TABLE "PurchaseInvoiceHeader" ADD COLUMN "ApprovedBy" TEXT NULL;
ALTER TABLE "PurchaseInvoiceHeader" ADD COLUMN "ApprovedAt" TEXT NULL;
ALTER TABLE "PurchaseInvoiceHeader" ADD COLUMN "VoidedBy" TEXT NULL;
ALTER TABLE "PurchaseInvoiceHeader" ADD COLUMN "VoidedAt" TEXT NULL;
ALTER TABLE "PurchaseInvoiceHeader" ADD COLUMN "VoidReason" TEXT NULL;

ALTER TABLE "VendorPayment" ADD COLUMN "VoidGeneralLedgerHeaderId" INTEGER NULL;
ALTER TABLE "VendorPayment" ADD COLUMN "VoidedBy" TEXT NULL;
ALTER TABLE "VendorPayment" ADD COLUMN "VoidedAt" TEXT NULL;
ALTER TABLE "VendorPayment" ADD COLUMN "VoidReason" TEXT NULL;

CREATE INDEX IF NOT EXISTS "IX_PurchaseInvoiceHeader_PayableAccountId" ON "PurchaseInvoiceHeader" ("PayableAccountId");
CREATE INDEX IF NOT EXISTS "IX_VendorPayment_VoidGeneralLedgerHeaderId" ON "VendorPayment" ("VoidGeneralLedgerHeaderId");

UPDATE "PurchaseInvoiceHeader" SET "PayableAccountId" = (SELECT l."AccountId" FROM "GeneralLedgerLine" AS l INNER JOIN "Account" AS a ON a."Id" = l."AccountId" WHERE l."GeneralLedgerHeaderId" = "PurchaseInvoiceHeader"."GeneralLedgerHeaderId" AND l."DrCr" = 2 AND a."AccountClassId" = 2 ORDER BY l."Amount" DESC LIMIT 1) WHERE "GeneralLedgerHeaderId" IS NOT NULL AND "PayableAccountId" IS NULL;
