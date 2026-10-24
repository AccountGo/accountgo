-- Add stock movement values, item stock caches, stock adjustments, and their ledger accounts.
ALTER TABLE "Item" ADD COLUMN "QuantityOnHand" decimal(18, 4) NOT NULL DEFAULT 0;
ALTER TABLE "Item" ADD COLUMN "AverageCost" decimal(18, 4) NOT NULL DEFAULT 0;
ALTER TABLE "Item" ADD COLUMN "StockValue" decimal(18, 2) NOT NULL DEFAULT 0;
ALTER TABLE "InventoryControlJournal" ADD COLUMN "DocumentId" INTEGER NULL;
ALTER TABLE "InventoryControlJournal" ADD COLUMN "UnitCost" decimal(18, 4) NULL;
ALTER TABLE "InventoryControlJournal" ADD COLUMN "Memo" TEXT NULL;

CREATE TABLE IF NOT EXISTS "InventoryAdjustment" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_InventoryAdjustment" PRIMARY KEY AUTOINCREMENT,
    "CompanyId" INTEGER NOT NULL,
    "No" TEXT NOT NULL,
    "Date" TEXT NOT NULL,
    "ItemId" INTEGER NOT NULL,
    "QuantityChange" decimal(18, 4) NOT NULL,
    "UnitCost" decimal(18, 4) NULL,
    "Reason" TEXT NOT NULL,
    "Memo" TEXT NULL,
    "GeneralLedgerHeaderId" INTEGER NULL,
    "InventoryControlJournalId" INTEGER NULL,
    CONSTRAINT "FK_InventoryAdjustment_Item_ItemId" FOREIGN KEY ("ItemId") REFERENCES "Item" ("Id"),
    CONSTRAINT "FK_InventoryAdjustment_GeneralLedgerHeader_GeneralLedgerHeaderId" FOREIGN KEY ("GeneralLedgerHeaderId") REFERENCES "GeneralLedgerHeader" ("Id"),
    CONSTRAINT "FK_InventoryAdjustment_InventoryControlJournal_InventoryControlJournalId" FOREIGN KEY ("InventoryControlJournalId") REFERENCES "InventoryControlJournal" ("Id")
);
CREATE INDEX IF NOT EXISTS "IX_InventoryAdjustment_CompanyId" ON "InventoryAdjustment" ("CompanyId");
CREATE INDEX IF NOT EXISTS "IX_InventoryAdjustment_GeneralLedgerHeaderId" ON "InventoryAdjustment" ("GeneralLedgerHeaderId");
CREATE INDEX IF NOT EXISTS "IX_InventoryAdjustment_InventoryControlJournalId" ON "InventoryAdjustment" ("InventoryControlJournalId");
CREATE INDEX IF NOT EXISTS "IX_InventoryAdjustment_ItemId" ON "InventoryAdjustment" ("ItemId");

-- New workspaces get these from the chart-of-accounts template; add them to existing workspaces once.
INSERT INTO "Account" ("CompanyId", "AccountCode", "AccountName", "ParentAccountId", "AccountClassId", "DrOrCrSide", "IsCash", "IsContraAccount")
SELECT c."Id", '30700', 'Opening Balance Equity', parent."Id", cls."Id", 2, 0, 0
FROM "Company" c
JOIN "AccountClass" cls ON cls."Name" = 'Equity'
JOIN "Account" parent ON parent."CompanyId" = c."Id" AND parent."AccountCode" = '30000'
WHERE NOT EXISTS (
    SELECT 1 FROM "Account" a WHERE a."CompanyId" = c."Id" AND a."AccountCode" = '30700'
);
INSERT INTO "Account" ("CompanyId", "AccountCode", "AccountName", "ParentAccountId", "AccountClassId", "DrOrCrSide", "IsCash", "IsContraAccount")
SELECT c."Id", '50310', 'Inventory Adjustments', parent."Id", cls."Id", 1, 0, 0
FROM "Company" c
JOIN "AccountClass" cls ON cls."Name" = 'Expense'
JOIN "Account" parent ON parent."CompanyId" = c."Id" AND parent."AccountCode" = '50000'
WHERE NOT EXISTS (
    SELECT 1 FROM "Account" a WHERE a."CompanyId" = c."Id" AND a."AccountCode" = '50310'
);

INSERT INTO "SequenceNumber" ("CompanyId", "SequenceNumberType", "Description", "Prefix", "UsePrefix", "NextNumber")
SELECT c."Id", 16, 'InventoryAdjustment', '', 0, 1 FROM "Company" c
WHERE NOT EXISTS (
    SELECT 1 FROM "SequenceNumber" s WHERE s."CompanyId" = c."Id" AND s."SequenceNumberType" = 16
);

-- Recompute the quantity cache from every movement; purchased items keep their legacy cost as the opening average.
UPDATE "Item"
SET "QuantityOnHand" = COALESCE((
        SELECT SUM(COALESCE(j."INQty", 0) - COALESCE(j."OUTQty", 0))
        FROM "InventoryControlJournal" j
        WHERE j."ItemId" = "Item"."Id" AND j."CompanyId" = "Item"."CompanyId"
    ), 0),
    "AverageCost" = CASE WHEN EXISTS (
        SELECT 1 FROM "ItemCategory" c
        WHERE c."Id" = "Item"."ItemCategoryId" AND c."ItemType" = 2
    ) THEN COALESCE("Item"."Cost", 0) ELSE 0 END;

UPDATE "Item" SET "StockValue" = ROUND("QuantityOnHand" * "AverageCost", 2);
