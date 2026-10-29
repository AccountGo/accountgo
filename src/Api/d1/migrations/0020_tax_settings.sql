-- Input tax is an asset, not an expense. Existing workspaces get the account, the tax permission, and a corrected default.
INSERT INTO "Account" ("CompanyId", "AccountCode", "AccountName", "ParentAccountId", "AccountClassId", "DrOrCrSide", "IsCash", "IsContraAccount")
SELECT c."Id", '10160', 'Input Tax Receivable', parent."Id", cls."Id", 1, 0, 0
FROM "Company" c
JOIN "AccountClass" cls ON cls."Name" = 'Assets'
JOIN "Account" parent ON parent."CompanyId" = c."Id" AND parent."AccountCode" = '10100'
WHERE NOT EXISTS (
    SELECT 1 FROM "Account" a WHERE a."CompanyId" = c."Id" AND a."AccountCode" = '10160'
);

INSERT INTO "SecurityPermission" ("Name", "DisplayName", "SecurityGroupId")
SELECT 'admin.taxes.manage', 'Manage Taxes', g."Id"
FROM "SecurityGroup" g
WHERE g."Name" = 'Admin'
  AND NOT EXISTS (SELECT 1 FROM "SecurityPermission" WHERE "Name" = 'admin.taxes.manage');

INSERT INTO "SecurityRolePermission" ("SecurityRoleId", "SecurityPermissionId")
SELECT DISTINCT rp."SecurityRoleId", taxes."Id"
FROM "SecurityRolePermission" rp
JOIN "SecurityPermission" manage ON manage."Id" = rp."SecurityPermissionId"
JOIN "SecurityPermission" taxes ON taxes."Name" = 'admin.taxes.manage'
WHERE manage."Name" = 'financials.accounts.manage'
  AND NOT EXISTS (
      SELECT 1 FROM "SecurityRolePermission" existing
      WHERE existing."SecurityRoleId" = rp."SecurityRoleId"
        AND existing."SecurityPermissionId" = taxes."Id"
  );

UPDATE "Tax"
SET "PurchasingAccountId" = (
    SELECT a."Id" FROM "Account" a
    WHERE a."CompanyId" = "Tax"."CompanyId" AND a."AccountCode" = '10160'
)
WHERE "TaxCode" = 'NONE'
  AND EXISTS (
      SELECT 1 FROM "Account" a
      WHERE a."CompanyId" = "Tax"."CompanyId" AND a."AccountCode" = '10160'
  );
