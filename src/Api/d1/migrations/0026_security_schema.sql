-- Security and MFA schema: DataProtection keys, audit events, recovery codes, trusted devices, WebAuthn credentials & challenges
-- Mirrors SqliteSchemaUpgrader and EF IdentityDb/ApiDb migrations.

CREATE TABLE IF NOT EXISTS "RefreshTokens" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_RefreshTokens" PRIMARY KEY AUTOINCREMENT,
    "UserId" TEXT NOT NULL,
    "TokenHash" TEXT NOT NULL,
    "ExpiresAt" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "CreatedByIp" TEXT NULL,
    "RevokedAt" TEXT NULL,
    "ReplacedByTokenHash" TEXT NULL,
    "UserAgent" TEXT NULL,
    "MfaMethod" TEXT NULL,
    "Amr" TEXT NULL,
    "TrustedDeviceId" INTEGER NULL
);
CREATE INDEX IF NOT EXISTS "IX_RefreshTokens_TokenHash" ON "RefreshTokens" ("TokenHash");
CREATE INDEX IF NOT EXISTS "IX_RefreshTokens_UserId" ON "RefreshTokens" ("UserId");

CREATE TABLE IF NOT EXISTS "DataProtectionKeys" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_DataProtectionKeys" PRIMARY KEY AUTOINCREMENT,
    "FriendlyName" TEXT NOT NULL,
    "Xml" TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS "IX_DataProtectionKeys_FriendlyName" ON "DataProtectionKeys" ("FriendlyName");

CREATE TABLE IF NOT EXISTS "SecurityAuditEvents" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SecurityAuditEvents" PRIMARY KEY AUTOINCREMENT,
    "UserId" TEXT NULL,
    "CompanyId" INTEGER NULL,
    "EventType" TEXT NOT NULL,
    "IpAddress" TEXT NULL,
    "UserAgent" TEXT NULL,
    "MetadataJson" TEXT NULL,
    "CreatedAt" TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS "IX_SecurityAuditEvents_EventType" ON "SecurityAuditEvents" ("EventType");
CREATE INDEX IF NOT EXISTS "IX_SecurityAuditEvents_UserId_CreatedAt" ON "SecurityAuditEvents" ("UserId", "CreatedAt");

CREATE TABLE IF NOT EXISTS "UserRecoveryCodes" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_UserRecoveryCodes" PRIMARY KEY AUTOINCREMENT,
    "UserId" TEXT NOT NULL,
    "CodeHash" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "ConsumedAt" TEXT NULL
);
CREATE INDEX IF NOT EXISTS "IX_UserRecoveryCodes_UserId" ON "UserRecoveryCodes" ("UserId");
CREATE INDEX IF NOT EXISTS "IX_UserRecoveryCodes_CodeHash" ON "UserRecoveryCodes" ("CodeHash");

CREATE TABLE IF NOT EXISTS "TrustedDevices" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_TrustedDevices" PRIMARY KEY AUTOINCREMENT,
    "UserId" TEXT NOT NULL,
    "TokenHash" TEXT NOT NULL,
    "Label" TEXT NULL,
    "UserAgent" TEXT NULL,
    "CreatedByIp" TEXT NULL,
    "CreatedAt" TEXT NOT NULL,
    "ExpiresAt" TEXT NOT NULL,
    "LastSeenAt" TEXT NOT NULL,
    "RevokedAt" TEXT NULL
);
CREATE INDEX IF NOT EXISTS "IX_TrustedDevices_UserId" ON "TrustedDevices" ("UserId");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_TrustedDevices_TokenHash" ON "TrustedDevices" ("TokenHash");

CREATE TABLE IF NOT EXISTS "UserWebAuthnCredentials" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_UserWebAuthnCredentials" PRIMARY KEY AUTOINCREMENT,
    "UserId" TEXT NOT NULL,
    "CredentialId" BLOB NOT NULL,
    "PublicKey" BLOB NOT NULL,
    "UserHandle" BLOB NOT NULL,
    "SignatureCounter" INTEGER NOT NULL,
    "CredType" TEXT NULL,
    "AaGuid" TEXT NOT NULL,
    "Nickname" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "LastUsedAt" TEXT NULL,
    "Transports" TEXT NULL
);
CREATE INDEX IF NOT EXISTS "IX_UserWebAuthnCredentials_UserId" ON "UserWebAuthnCredentials" ("UserId");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_UserWebAuthnCredentials_CredentialId" ON "UserWebAuthnCredentials" ("CredentialId");

CREATE TABLE IF NOT EXISTS "WebAuthnChallenges" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_WebAuthnChallenges" PRIMARY KEY AUTOINCREMENT,
    "UserId" TEXT NOT NULL,
    "Purpose" TEXT NOT NULL,
    "JsonPayload" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "ExpiresAt" TEXT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_WebAuthnChallenges_UserId_Purpose" ON "WebAuthnChallenges" ("UserId", "Purpose");

ALTER TABLE "AspNetUsers" ADD COLUMN "MfaReEnrollRequired" INTEGER NOT NULL DEFAULT 0;
ALTER TABLE "CompanySetting" ADD COLUMN "MfaPolicy" INTEGER NOT NULL DEFAULT 0;
ALTER TABLE "CompanySetting" ADD COLUMN "MfaGracePeriodEndsAt" TEXT NULL;
ALTER TABLE "CompanySetting" ADD COLUMN "RequireMfaEveryLogin" INTEGER NOT NULL DEFAULT 0;
