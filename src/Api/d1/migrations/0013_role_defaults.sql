-- Built-in roles get the default permissions the code defines (SecurityService.EnsureDefaultPermissionsAndRoles).
-- Start-up applied those defaults only to the container's own copy, which the D1 restore then replaces, so
-- defaults added after the first release never reached D1: the Products & Services permissions are missing from
-- every built-in role. Only what is missing is added, and running this again changes nothing. Generated from
-- SecurityService.cs; a later change to the defaults needs its own migration.

INSERT INTO "SecurityGroup" ("Name", "DisplayName")
SELECT v.name, v.display FROM (
    SELECT 'Sales' AS name, 'Sales & Invoicing' AS display
    UNION ALL SELECT 'Financials' AS name, 'General Ledger & Banking' AS display
    UNION ALL SELECT 'Purchasing' AS name, 'Purchases & Bills' AS display
    UNION ALL SELECT 'Inventory' AS name, 'Inventory & Items' AS display
    UNION ALL SELECT 'Reports' AS name, 'Reports & Analytics' AS display
    UNION ALL SELECT 'Donations' AS name, 'Donations & Non-Profit' AS display
    UNION ALL SELECT 'Audit' AS name, 'Audit & Compliance' AS display
    UNION ALL SELECT 'Admin' AS name, 'Administration' AS display
) v WHERE NOT EXISTS (SELECT 1 FROM "SecurityGroup" g WHERE g."Name" = v.name);

INSERT INTO "SecurityPermission" ("Name", "DisplayName", "SecurityGroupId")
SELECT v.name, v.display, g."Id" FROM (
    SELECT 'sales.customers.view' AS name, 'View Customers' AS display, 'Sales' AS grp
    UNION ALL SELECT 'sales.customers.manage' AS name, 'Manage Customers' AS display, 'Sales' AS grp
    UNION ALL SELECT 'sales.invoices.view' AS name, 'View Invoices' AS display, 'Sales' AS grp
    UNION ALL SELECT 'sales.invoices.create' AS name, 'Create Invoices' AS display, 'Sales' AS grp
    UNION ALL SELECT 'sales.invoices.post' AS name, 'Post Invoices' AS display, 'Sales' AS grp
    UNION ALL SELECT 'sales.receipts.view' AS name, 'View Receipts' AS display, 'Sales' AS grp
    UNION ALL SELECT 'sales.receipts.create' AS name, 'Create Receipts' AS display, 'Sales' AS grp
    UNION ALL SELECT 'sales.quotations.view' AS name, 'View Quotations' AS display, 'Sales' AS grp
    UNION ALL SELECT 'financials.accounts.view' AS name, 'View Accounts' AS display, 'Financials' AS grp
    UNION ALL SELECT 'financials.accounts.manage' AS name, 'Manage Accounts' AS display, 'Financials' AS grp
    UNION ALL SELECT 'financials.journal.view' AS name, 'View Journal' AS display, 'Financials' AS grp
    UNION ALL SELECT 'financials.journal.create' AS name, 'Create Journal Entries' AS display, 'Financials' AS grp
    UNION ALL SELECT 'financials.journal.post' AS name, 'Post Journal Entries' AS display, 'Financials' AS grp
    UNION ALL SELECT 'financials.cashbanks.view' AS name, 'View Cash & Banks' AS display, 'Financials' AS grp
    UNION ALL SELECT 'financials.cashbanks.manage' AS name, 'Manage Cash & Banks' AS display, 'Financials' AS grp
    UNION ALL SELECT 'purchases.vendors.view' AS name, 'View Vendors' AS display, 'Purchasing' AS grp
    UNION ALL SELECT 'purchases.vendors.manage' AS name, 'Manage Vendors' AS display, 'Purchasing' AS grp
    UNION ALL SELECT 'purchases.orders.view' AS name, 'View Orders' AS display, 'Purchasing' AS grp
    UNION ALL SELECT 'purchases.bills.view' AS name, 'View Bills' AS display, 'Purchasing' AS grp
    UNION ALL SELECT 'purchases.bills.create' AS name, 'Create Bills' AS display, 'Purchasing' AS grp
    UNION ALL SELECT 'purchases.bills.post' AS name, 'Post Bills' AS display, 'Purchasing' AS grp
    UNION ALL SELECT 'purchases.payments.create' AS name, 'Pay Bills' AS display, 'Purchasing' AS grp
    UNION ALL SELECT 'inventory.items.view' AS name, 'View Items' AS display, 'Inventory' AS grp
    UNION ALL SELECT 'inventory.items.manage' AS name, 'Manage Items' AS display, 'Inventory' AS grp
    UNION ALL SELECT 'inventory.adjustments.view' AS name, 'View Adjustments' AS display, 'Inventory' AS grp
    UNION ALL SELECT 'inventory.adjustments.post' AS name, 'Post Adjustments' AS display, 'Inventory' AS grp
    UNION ALL SELECT 'reports.view' AS name, 'View Reports' AS display, 'Reports' AS grp
    UNION ALL SELECT 'donations.view' AS name, 'View Donations' AS display, 'Donations' AS grp
    UNION ALL SELECT 'donations.manage' AS name, 'Manage Donations' AS display, 'Donations' AS grp
    UNION ALL SELECT 'audit.logs.view' AS name, 'View Audit Logs' AS display, 'Audit' AS grp
    UNION ALL SELECT 'admin.members.manage' AS name, 'Manage Members' AS display, 'Admin' AS grp
    UNION ALL SELECT 'admin.roles.manage' AS name, 'Manage Roles' AS display, 'Admin' AS grp
    UNION ALL SELECT 'admin.subscription.manage' AS name, 'Manage Subscription' AS display, 'Admin' AS grp
) v JOIN "SecurityGroup" g ON g."Name" = v.grp
WHERE NOT EXISTS (SELECT 1 FROM "SecurityPermission" p WHERE p."Name" = v.name);

INSERT INTO "SecurityRolePermission" ("SecurityRoleId", "SecurityPermissionId")
SELECT r."Id", p."Id" FROM (
    SELECT 'Accountant' AS role, 'sales.customers.view' AS perm
    UNION ALL SELECT 'Accountant' AS role, 'sales.customers.manage' AS perm
    UNION ALL SELECT 'Accountant' AS role, 'sales.invoices.view' AS perm
    UNION ALL SELECT 'Accountant' AS role, 'sales.invoices.create' AS perm
    UNION ALL SELECT 'Accountant' AS role, 'sales.invoices.post' AS perm
    UNION ALL SELECT 'Accountant' AS role, 'sales.receipts.view' AS perm
    UNION ALL SELECT 'Accountant' AS role, 'sales.receipts.create' AS perm
    UNION ALL SELECT 'Accountant' AS role, 'financials.accounts.view' AS perm
    UNION ALL SELECT 'Accountant' AS role, 'financials.accounts.manage' AS perm
    UNION ALL SELECT 'Accountant' AS role, 'financials.journal.view' AS perm
    UNION ALL SELECT 'Accountant' AS role, 'financials.journal.create' AS perm
    UNION ALL SELECT 'Accountant' AS role, 'financials.journal.post' AS perm
    UNION ALL SELECT 'Accountant' AS role, 'financials.cashbanks.view' AS perm
    UNION ALL SELECT 'Accountant' AS role, 'financials.cashbanks.manage' AS perm
    UNION ALL SELECT 'Accountant' AS role, 'purchases.vendors.view' AS perm
    UNION ALL SELECT 'Accountant' AS role, 'purchases.vendors.manage' AS perm
    UNION ALL SELECT 'Accountant' AS role, 'purchases.bills.view' AS perm
    UNION ALL SELECT 'Accountant' AS role, 'purchases.bills.create' AS perm
    UNION ALL SELECT 'Accountant' AS role, 'purchases.bills.post' AS perm
    UNION ALL SELECT 'Accountant' AS role, 'purchases.payments.create' AS perm
    UNION ALL SELECT 'Accountant' AS role, 'inventory.items.view' AS perm
    UNION ALL SELECT 'Accountant' AS role, 'inventory.items.manage' AS perm
    UNION ALL SELECT 'Accountant' AS role, 'reports.view' AS perm
    UNION ALL SELECT 'Sales Representative' AS role, 'sales.customers.view' AS perm
    UNION ALL SELECT 'Sales Representative' AS role, 'sales.customers.manage' AS perm
    UNION ALL SELECT 'Sales Representative' AS role, 'sales.quotations.view' AS perm
    UNION ALL SELECT 'Sales Representative' AS role, 'sales.invoices.view' AS perm
    UNION ALL SELECT 'Sales Representative' AS role, 'sales.invoices.create' AS perm
    UNION ALL SELECT 'Sales Representative' AS role, 'sales.receipts.create' AS perm
    UNION ALL SELECT 'Sales Representative' AS role, 'inventory.items.view' AS perm
    UNION ALL SELECT 'Purchasing Agent' AS role, 'purchases.vendors.view' AS perm
    UNION ALL SELECT 'Purchasing Agent' AS role, 'purchases.vendors.manage' AS perm
    UNION ALL SELECT 'Purchasing Agent' AS role, 'purchases.orders.view' AS perm
    UNION ALL SELECT 'Purchasing Agent' AS role, 'purchases.bills.view' AS perm
    UNION ALL SELECT 'Purchasing Agent' AS role, 'purchases.bills.create' AS perm
    UNION ALL SELECT 'Purchasing Agent' AS role, 'inventory.items.view' AS perm
    UNION ALL SELECT 'Auditor / Viewer' AS role, 'sales.customers.view' AS perm
    UNION ALL SELECT 'Auditor / Viewer' AS role, 'sales.invoices.view' AS perm
    UNION ALL SELECT 'Auditor / Viewer' AS role, 'financials.accounts.view' AS perm
    UNION ALL SELECT 'Auditor / Viewer' AS role, 'financials.journal.view' AS perm
    UNION ALL SELECT 'Auditor / Viewer' AS role, 'financials.cashbanks.view' AS perm
    UNION ALL SELECT 'Auditor / Viewer' AS role, 'purchases.vendors.view' AS perm
    UNION ALL SELECT 'Auditor / Viewer' AS role, 'purchases.bills.view' AS perm
    UNION ALL SELECT 'Auditor / Viewer' AS role, 'reports.view' AS perm
    UNION ALL SELECT 'Auditor / Viewer' AS role, 'audit.logs.view' AS perm
    UNION ALL SELECT 'Auditor / Viewer' AS role, 'inventory.items.view' AS perm
    UNION ALL SELECT 'GeneralUsers' AS role, 'reports.view' AS perm
) v JOIN "SecurityRole" r ON r."Name" = v.role JOIN "SecurityPermission" p ON p."Name" = v.perm
WHERE NOT EXISTS (SELECT 1 FROM "SecurityRolePermission" x WHERE x."SecurityRoleId" = r."Id" AND x."SecurityPermissionId" = p."Id");
