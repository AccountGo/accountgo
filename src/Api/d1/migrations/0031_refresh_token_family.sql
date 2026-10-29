-- Refresh-token families: scoped reuse detection + grace sibling bound.
-- Mirrors SqliteSchemaUpgrader and the EF IdentityDb RefreshTokenFamily migration.
ALTER TABLE "RefreshTokens" ADD COLUMN "FamilyId" TEXT NOT NULL DEFAULT '';
ALTER TABLE "RefreshTokens" ADD COLUMN "GraceReuseCount" INTEGER NOT NULL DEFAULT 0;
UPDATE "RefreshTokens" SET "FamilyId" = CAST("Id" AS TEXT) WHERE "FamilyId" IS NULL OR "FamilyId" = '';
CREATE INDEX IF NOT EXISTS "IX_RefreshTokens_FamilyId" ON "RefreshTokens" ("FamilyId");
