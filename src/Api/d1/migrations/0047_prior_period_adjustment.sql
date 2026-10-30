-- #455 Phase 2b-2: prior-period adjustment journals link to the closed fiscal year they correct.
-- One statement per line; safe under SQLITE_LIMIT_COMPOUND_SELECT=1.

ALTER TABLE "JournalEntryHeader" ADD COLUMN "AdjustsFiscalYearId" INTEGER NULL;
