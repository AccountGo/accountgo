-- Preserve exact movement cents for already-posted stock; legacy unvalued journals use the existing cost cache.
ALTER TABLE "Item" ADD COLUMN "StockValue" decimal(18, 2) NOT NULL DEFAULT 0;

UPDATE "Item" SET "StockValue" = ROUND(CASE WHEN NOT EXISTS (
    SELECT 1 FROM "InventoryControlJournal" j
    WHERE j."ItemId" = "Item"."Id" AND j."CompanyId" = "Item"."CompanyId" AND j."TotalCost" IS NULL
) THEN COALESCE((
    SELECT SUM(CASE WHEN COALESCE(j."INQty", 0) > 0 THEN j."TotalCost" ELSE -j."TotalCost" END)
    FROM "InventoryControlJournal" j
    WHERE j."ItemId" = "Item"."Id" AND j."CompanyId" = "Item"."CompanyId"
), "QuantityOnHand" * "AverageCost")
ELSE "QuantityOnHand" * "AverageCost" END, 2);
