-- Products and services can be archived: hidden when picking an item for a line, past lines untouched. NULL means active.
ALTER TABLE "Item" ADD COLUMN "ArchivedAt" TEXT NULL;
