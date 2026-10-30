-- #455 Phase 2a: books locked-through date + audit trail.
-- One statement per line; safe under SQLITE_LIMIT_COMPOUND_SELECT=1.

ALTER TABLE "CompanySetting" ADD COLUMN "BooksLockedThrough" TEXT NULL;

CREATE TABLE IF NOT EXISTS "BooksLockChange" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_BooksLockChange" PRIMARY KEY AUTOINCREMENT,
    "CompanyId" INTEGER NOT NULL,
    "PreviousLockedThrough" TEXT NULL,
    "NewLockedThrough" TEXT NULL,
    "Reason" TEXT NULL,
    "ChangedBy" TEXT NOT NULL,
    "ChangedAt" TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS "IX_BooksLockChange_CompanyId" ON "BooksLockChange" ("CompanyId");
