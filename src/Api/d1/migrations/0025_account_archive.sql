-- Migration: Add ArchivedAt column to Account for lifecycle management (§5.4/§12)
ALTER TABLE "Account" ADD COLUMN "ArchivedAt" TEXT NULL;
