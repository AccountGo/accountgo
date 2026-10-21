-- Phase 1: Multi-Tenancy Data Model & Schema Migration
-- 1. Add CompanyId to all tenant-owned business tables

ALTER TABLE "Address" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "AuditLog" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "Bank" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "Contact" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "Customer" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "CustomerAllocation" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "CustomerContact" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "DonationInvoiceHeader" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "DonationInvoiceLine" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "Donors" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "FinancialYear" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "GeneralLedgerHeader" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "GeneralLedgerLine" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "InventoryControlJournal" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "Item" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "ItemCategory" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "ItemTaxGroup" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "ItemTaxGroupTax" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "JournalEntryHeader" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "JournalEntryLine" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "Log" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "MainContraAccount" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "Measurement" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "Party" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "PaymentTerm" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "PurchaseInvoiceHeader" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "PurchaseInvoiceLine" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "PurchaseOrderHeader" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "PurchaseOrderLine" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "PurchaseReceiptHeader" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "PurchaseReceiptLine" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "SalesDeliveryHeader" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "SalesDeliveryLine" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "SalesInvoiceHeader" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "SalesInvoiceLine" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "SalesOrderHeader" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "SalesOrderLine" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "SalesQuoteHeader" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "SalesQuoteLine" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "SalesReceiptHeader" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "SalesReceiptLine" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "SequenceNumber" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "Tax" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "TaxGroup" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "TaxGroupTax" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "Vendor" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "VendorContact" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "VendorPayment" ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1;

-- Backfill and ensure GeneralLedgerSetting has CompanyId = 1
UPDATE "GeneralLedgerSetting" SET "CompanyId" = 1 WHERE "CompanyId" IS NULL;

-- 2. Add audit fields to Company table
ALTER TABLE "Company" ADD COLUMN "CreatedAt" TEXT NOT NULL DEFAULT '2025-01-01T00:00:00Z';
ALTER TABLE "Company" ADD COLUMN "CreatedByUserId" TEXT NULL;

-- 3. Create Tenancy tables
CREATE TABLE IF NOT EXISTS "CompanyMember" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_CompanyMember" PRIMARY KEY AUTOINCREMENT,
    "CompanyId" INTEGER NOT NULL,
    "UserId" TEXT NOT NULL,
    "Role" INTEGER NOT NULL,
    "Status" INTEGER NOT NULL,
    "JoinedAt" TEXT NOT NULL,
    "InvitedByUserId" TEXT NULL,
    CONSTRAINT "FK_CompanyMember_Company_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Company" ("Id") ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_CompanyMember_CompanyId_UserId" ON "CompanyMember" ("CompanyId", "UserId");
CREATE INDEX IF NOT EXISTS "IX_CompanyMember_UserId" ON "CompanyMember" ("UserId");

CREATE TABLE IF NOT EXISTS "CompanyMemberSecurityRole" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_CompanyMemberSecurityRole" PRIMARY KEY AUTOINCREMENT,
    "CompanyMemberId" INTEGER NOT NULL,
    "SecurityRoleId" INTEGER NOT NULL,
    CONSTRAINT "FK_CompanyMemberSecurityRole_CompanyMember_CompanyMemberId" FOREIGN KEY ("CompanyMemberId") REFERENCES "CompanyMember" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_CompanyMemberSecurityRole_SecurityRole_SecurityRoleId" FOREIGN KEY ("SecurityRoleId") REFERENCES "SecurityRole" ("Id") ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_CompanyMemberSecurityRole_CompanyMemberId_SecurityRoleId" ON "CompanyMemberSecurityRole" ("CompanyMemberId", "SecurityRoleId");
CREATE INDEX IF NOT EXISTS "IX_CompanyMemberSecurityRole_SecurityRoleId" ON "CompanyMemberSecurityRole" ("SecurityRoleId");

CREATE TABLE IF NOT EXISTS "CompanyInvitation" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_CompanyInvitation" PRIMARY KEY AUTOINCREMENT,
    "CompanyId" INTEGER NOT NULL,
    "Email" TEXT NOT NULL,
    "Role" INTEGER NOT NULL,
    "SecurityRoleIdsJson" TEXT NOT NULL DEFAULT '[]',
    "TokenHash" TEXT NOT NULL,
    "ExpiresAt" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "InvitedByUserId" TEXT NOT NULL,
    "AcceptedAt" TEXT NULL,
    "AcceptedByUserId" TEXT NULL,
    "RevokedAt" TEXT NULL,
    CONSTRAINT "FK_CompanyInvitation_Company_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Company" ("Id") ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_CompanyInvitation_TokenHash" ON "CompanyInvitation" ("TokenHash");
CREATE INDEX IF NOT EXISTS "IX_CompanyInvitation_CompanyId_Email" ON "CompanyInvitation" ("CompanyId", "Email");

-- 4. Composite unique indexes on tenant tables
DROP INDEX IF EXISTS "IX_Account_CompanyId";
CREATE UNIQUE INDEX IF NOT EXISTS "IX_Account_CompanyId_AccountCode" ON "Account" ("CompanyId", "AccountCode");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_SequenceNumber_CompanyId_SequenceNumberType" ON "SequenceNumber" ("CompanyId", "SequenceNumberType");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_Customer_CompanyId_No" ON "Customer" ("CompanyId", "No");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_Vendor_CompanyId_No" ON "Vendor" ("CompanyId", "No");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_Item_CompanyId_No" ON "Item" ("CompanyId", "No");

-- 5. Backfill Company 1 memberships (Decision D1)
INSERT INTO "CompanyMember" ("CompanyId", "UserId", "Role", "Status", "JoinedAt")
SELECT 1, "Id", 1, 1, datetime('now')
FROM "AspNetUsers"
WHERE LOWER("Email") = 'marvinperez@gmail.com'
  AND NOT EXISTS (
      SELECT 1 FROM "CompanyMember"
      WHERE "CompanyId" = 1 AND "UserId" = "AspNetUsers"."Id"
  );

INSERT INTO "CompanyMember" ("CompanyId", "UserId", "Role", "Status", "JoinedAt")
SELECT 1, "Id", 2, 1, datetime('now')
FROM "AspNetUsers"
WHERE LOWER("Email") = 'hello@marvinperez.com'
  AND NOT EXISTS (
      SELECT 1 FROM "CompanyMember"
      WHERE "CompanyId" = 1 AND "UserId" = "AspNetUsers"."Id"
  );

INSERT INTO "CompanyMember" ("CompanyId", "UserId", "Role", "Status", "JoinedAt")
SELECT 1, "Id", 3, 1, datetime('now')
FROM "AspNetUsers"
WHERE LOWER("Email") = 'admin@accountgo.ph'
  AND NOT EXISTS (
      SELECT 1 FROM "CompanyMember"
      WHERE "CompanyId" = 1 AND "UserId" = "AspNetUsers"."Id"
  );

-- Attach Auditor / Viewer role to admin@accountgo.ph (Decision D1)
INSERT INTO "CompanyMemberSecurityRole" ("CompanyMemberId", "SecurityRoleId")
SELECT cm."Id", sr."Id"
FROM "CompanyMember" cm
JOIN "AspNetUsers" u ON u."Id" = cm."UserId"
JOIN "SecurityRole" sr ON sr."Name" = 'Auditor / Viewer'
WHERE cm."CompanyId" = 1 AND LOWER(u."Email") = 'admin@accountgo.ph'
  AND NOT EXISTS (
      SELECT 1 FROM "CompanyMemberSecurityRole" cmsr
      WHERE cmsr."CompanyMemberId" = cm."Id" AND cmsr."SecurityRoleId" = sr."Id"
  );

-- Remove SystemAdministrators from non-platform staff (Decision D2)
DELETE FROM "SecurityUserRole"
WHERE "SecurityRoleId" IN (SELECT "Id" FROM "SecurityRole" WHERE "Name" = 'SystemAdministrators')
  AND "UserId" NOT IN (
      SELECT "Id" FROM "User"
      WHERE LOWER("UserName") = 'marvinperez@gmail.com' OR LOWER("EmailAddress") = 'marvinperez@gmail.com'
  );
