-- #455 Phase 2a: financials.periods.close permission.
-- Workspace Owner/Admin receive every SecurityPermission via tenant resolution.
-- Grant to existing roles that already hold admin.members.manage (Owner/Admin-equivalent custom roles).
-- One statement per line; safe under SQLITE_LIMIT_COMPOUND_SELECT=1.

INSERT INTO "SecurityPermission" ("Name", "DisplayName", "SecurityGroupId")
SELECT 'financials.periods.close', 'Close / Lock Accounting Periods', g."Id"
FROM "SecurityGroup" g
WHERE g."Name" = 'Financials'
  AND NOT EXISTS (SELECT 1 FROM "SecurityPermission" WHERE "Name" = 'financials.periods.close');

INSERT INTO "SecurityRolePermission" ("SecurityRoleId", "SecurityPermissionId")
SELECT DISTINCT rp."SecurityRoleId", closePerm."Id"
FROM "SecurityRolePermission" rp
JOIN "SecurityPermission" adminPerm ON adminPerm."Id" = rp."SecurityPermissionId"
JOIN "SecurityPermission" closePerm ON closePerm."Name" = 'financials.periods.close'
WHERE adminPerm."Name" = 'admin.members.manage'
  AND NOT EXISTS (
      SELECT 1 FROM "SecurityRolePermission" existing
      WHERE existing."SecurityRoleId" = rp."SecurityRoleId"
        AND existing."SecurityPermissionId" = closePerm."Id"
  );
