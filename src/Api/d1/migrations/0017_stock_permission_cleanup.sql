-- Converge databases that already applied the original 0015 without losing custom-role grants.
INSERT INTO "SecurityRolePermission" ("SecurityRoleId", "SecurityPermissionId")
SELECT DISTINCT rp."SecurityRoleId", adjust."Id"
FROM "SecurityRolePermission" rp
JOIN "SecurityPermission" old ON old."Id" = rp."SecurityPermissionId"
JOIN "SecurityPermission" adjust ON adjust."Name" = 'inventory.adjustments.post'
WHERE old."Name" IN ('inventory.stock.adjust', 'inventory.items.manage')
  AND NOT EXISTS (
      SELECT 1 FROM "SecurityRolePermission" existing
      WHERE existing."SecurityRoleId" = rp."SecurityRoleId"
        AND existing."SecurityPermissionId" = adjust."Id"
  );

DELETE FROM "SecurityRolePermission" WHERE "SecurityPermissionId" IN (
    SELECT "Id" FROM "SecurityPermission" WHERE "Name" = 'inventory.stock.adjust'
);
DELETE FROM "SecurityPermission" WHERE "Name" = 'inventory.stock.adjust';
