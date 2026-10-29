-- Cash basis accounting & general ledger source traceability.
-- Workspaces default to Accrual basis (1); GeneralLedgerHeader tracks its originating document and reversed header.
ALTER TABLE "CompanySetting" ADD COLUMN "AccountingBasis" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "GeneralLedgerHeader" ADD COLUMN "SourceDocumentId" INTEGER NULL;
ALTER TABLE "GeneralLedgerHeader" ADD COLUMN "ReversedGeneralLedgerHeaderId" INTEGER NULL;
ALTER TABLE "CustomerAllocation" ADD COLUMN "GeneralLedgerHeaderId" INTEGER NULL;

UPDATE "GeneralLedgerHeader" SET "SourceDocumentId" = (
    SELECT i."Id" FROM "SalesInvoiceHeader" i WHERE i."GeneralLedgerHeaderId" = "GeneralLedgerHeader"."Id")
WHERE "SourceDocumentId" IS NULL
  AND EXISTS (SELECT 1 FROM "SalesInvoiceHeader" i WHERE i."GeneralLedgerHeaderId" = "GeneralLedgerHeader"."Id");

UPDATE "GeneralLedgerHeader" SET
    "SourceDocumentId" = (SELECT i."Id" FROM "SalesInvoiceHeader" i WHERE i."VoidGeneralLedgerHeaderId" = "GeneralLedgerHeader"."Id"),
    "ReversedGeneralLedgerHeaderId" = (SELECT i."GeneralLedgerHeaderId" FROM "SalesInvoiceHeader" i WHERE i."VoidGeneralLedgerHeaderId" = "GeneralLedgerHeader"."Id")
WHERE ("SourceDocumentId" IS NULL OR "ReversedGeneralLedgerHeaderId" IS NULL)
  AND EXISTS (SELECT 1 FROM "SalesInvoiceHeader" i WHERE i."VoidGeneralLedgerHeaderId" = "GeneralLedgerHeader"."Id");

UPDATE "GeneralLedgerHeader" SET "SourceDocumentId" = (
    SELECT r."Id" FROM "SalesReceiptHeader" r WHERE r."GeneralLedgerHeaderId" = "GeneralLedgerHeader"."Id")
WHERE "SourceDocumentId" IS NULL
  AND EXISTS (SELECT 1 FROM "SalesReceiptHeader" r WHERE r."GeneralLedgerHeaderId" = "GeneralLedgerHeader"."Id");

UPDATE "GeneralLedgerHeader" SET "SourceDocumentId" = (
    SELECT b."Id" FROM "PurchaseInvoiceHeader" b WHERE b."GeneralLedgerHeaderId" = "GeneralLedgerHeader"."Id")
WHERE "SourceDocumentId" IS NULL
  AND EXISTS (SELECT 1 FROM "PurchaseInvoiceHeader" b WHERE b."GeneralLedgerHeaderId" = "GeneralLedgerHeader"."Id");

UPDATE "GeneralLedgerHeader" SET
    "SourceDocumentId" = (SELECT b."Id" FROM "PurchaseInvoiceHeader" b WHERE b."VoidGeneralLedgerHeaderId" = "GeneralLedgerHeader"."Id"),
    "ReversedGeneralLedgerHeaderId" = (SELECT b."GeneralLedgerHeaderId" FROM "PurchaseInvoiceHeader" b WHERE b."VoidGeneralLedgerHeaderId" = "GeneralLedgerHeader"."Id")
WHERE ("SourceDocumentId" IS NULL OR "ReversedGeneralLedgerHeaderId" IS NULL)
  AND EXISTS (SELECT 1 FROM "PurchaseInvoiceHeader" b WHERE b."VoidGeneralLedgerHeaderId" = "GeneralLedgerHeader"."Id");

UPDATE "GeneralLedgerHeader" SET "SourceDocumentId" = (
    SELECT p."PurchaseInvoiceHeaderId" FROM "VendorPayment" p WHERE p."GeneralLedgerHeaderId" = "GeneralLedgerHeader"."Id")
WHERE "SourceDocumentId" IS NULL
  AND EXISTS (SELECT 1 FROM "VendorPayment" p WHERE p."GeneralLedgerHeaderId" = "GeneralLedgerHeader"."Id" AND p."PurchaseInvoiceHeaderId" IS NOT NULL);

UPDATE "GeneralLedgerHeader" SET "SourceDocumentId" = (
    SELECT j."Id" FROM "JournalEntryHeader" j WHERE j."GeneralLedgerHeaderId" = "GeneralLedgerHeader"."Id")
WHERE "SourceDocumentId" IS NULL
  AND EXISTS (SELECT 1 FROM "JournalEntryHeader" j WHERE j."GeneralLedgerHeaderId" = "GeneralLedgerHeader"."Id");

UPDATE "GeneralLedgerHeader" SET "SourceDocumentId" = (
    SELECT a."Id" FROM "CustomerAllocation" a WHERE a."GeneralLedgerHeaderId" = "GeneralLedgerHeader"."Id")
WHERE "SourceDocumentId" IS NULL
  AND EXISTS (SELECT 1 FROM "CustomerAllocation" a WHERE a."GeneralLedgerHeaderId" = "GeneralLedgerHeader"."Id");
