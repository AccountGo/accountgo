-- Clients can be archived: hidden when picking a client for an invoice, their invoices untouched. NULL means active.
ALTER TABLE "Customer" ADD COLUMN "ArchivedAt" TEXT NULL;
