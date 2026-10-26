-- Additive MFA columns on RefreshTokens. Required because 0026's CREATE TABLE IF NOT EXISTS
-- did not alter existing hosted RefreshTokens rows (D1 sync then failed: "no column named Amr").
ALTER TABLE "RefreshTokens" ADD COLUMN "MfaMethod" TEXT NULL;
ALTER TABLE "RefreshTokens" ADD COLUMN "Amr" TEXT NULL;
ALTER TABLE "RefreshTokens" ADD COLUMN "TrustedDeviceId" INTEGER NULL;
