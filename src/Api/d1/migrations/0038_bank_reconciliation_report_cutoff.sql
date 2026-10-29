-- #413 Phase 2D: finish-time GL header cutoff for as-of reconciliation reports.
ALTER TABLE "BankReconciliation" ADD COLUMN "FinishedMaxGeneralLedgerHeaderId" INTEGER NULL;
