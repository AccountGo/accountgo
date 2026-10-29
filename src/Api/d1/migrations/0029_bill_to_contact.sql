-- Bill To contact on Customer and Vendor (nullable FK to Contact; ON DELETE SET NULL).
-- Mirrors SqliteSchemaUpgrader and the EF migration BillToContact.
ALTER TABLE "Customer" ADD COLUMN "BillToContactId" INTEGER NULL REFERENCES "Contact" ("Id") ON DELETE SET NULL;
ALTER TABLE "Vendor" ADD COLUMN "BillToContactId" INTEGER NULL REFERENCES "Contact" ("Id") ON DELETE SET NULL;
CREATE INDEX IF NOT EXISTS "IX_Customer_BillToContactId" ON "Customer" ("BillToContactId");
CREATE INDEX IF NOT EXISTS "IX_Vendor_BillToContactId" ON "Vendor" ("BillToContactId");
