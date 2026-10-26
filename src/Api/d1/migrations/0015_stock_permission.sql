INSERT INTO "SecurityPermission" ("Name", "DisplayName", "SecurityGroupId")
SELECT 'inventory.adjustments.post', 'Adjust Stock', g."Id"
FROM "SecurityGroup" g
WHERE g."Name" = 'Inventory'
  AND NOT EXISTS (SELECT 1 FROM "SecurityPermission" WHERE "Name" = 'inventory.adjustments.post');

INSERT INTO "SecurityRolePermission" ("SecurityRoleId", "SecurityPermissionId")
SELECT DISTINCT rp."SecurityRoleId", adjust."Id"
FROM "SecurityRolePermission" rp
JOIN "SecurityPermission" manage ON manage."Id" = rp."SecurityPermissionId"
JOIN "SecurityPermission" adjust ON adjust."Name" = 'inventory.adjustments.post'
WHERE manage."Name" = 'inventory.items.manage'
  AND NOT EXISTS (
      SELECT 1 FROM "SecurityRolePermission" existing
      WHERE existing."SecurityRoleId" = rp."SecurityRoleId"
        AND existing."SecurityPermissionId" = adjust."Id"
  );
