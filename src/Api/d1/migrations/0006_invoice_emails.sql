CREATE TABLE IF NOT EXISTS "SalesInvoiceEmail" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SalesInvoiceEmail" PRIMARY KEY AUTOINCREMENT,
    "CompanyId" INTEGER NOT NULL,
    "SalesInvoiceHeaderId" INTEGER NOT NULL,
    "Kind" TEXT NOT NULL,
    "ToEmail" TEXT NOT NULL,
    "BccEmail" TEXT NULL,
    "Subject" TEXT NOT NULL,
    "SentAt" TEXT NOT NULL,
    "SentByUserId" TEXT NULL,
    "SentByEmail" TEXT NULL,
    "ProviderMessageId" TEXT NULL,
    CONSTRAINT "FK_SalesInvoiceEmail_SalesInvoiceHeader_SalesInvoiceHeaderId" FOREIGN KEY ("SalesInvoiceHeaderId") REFERENCES "SalesInvoiceHeader" ("Id") ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS "IX_SalesInvoiceEmail_CompanyId_SentAt" ON "SalesInvoiceEmail" ("CompanyId", "SentAt");
CREATE INDEX IF NOT EXISTS "IX_SalesInvoiceEmail_SalesInvoiceHeaderId" ON "SalesInvoiceEmail" ("SalesInvoiceHeaderId");
