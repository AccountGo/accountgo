/**
 * Plain-language names for the areas a permission opens, so a "No access" page can say "Vendors" instead of a permission key.
 *
 * Keyed by the permission's resource: its first two dot segments (`purchases.vendors.view` → `purchases.vendors`; `reports.view`
 * is its own resource). Only names live here. The roles that grant a permission are read from the server's role catalog when the
 * page is shown, so they cannot drift. A test reads the server's permission list and fails if a permission has no name here
 * (or a name here has no permission), so adding a permission without a label breaks the build.
 */
export const PERMISSION_AREA_LABELS: Record<string, string> = {
  'sales.customers': 'Clients',
  'sales.invoices': 'Invoices',
  'sales.receipts': 'Receipts',
  'sales.quotations': 'Quotations',
  'sales.credits': 'Sales credits',
  'sales.returns': 'Sales returns',
  'sales.refunds': 'Sales refunds',
  'sales.write_offs': 'Write-offs',
  'purchases.vendors': 'Vendors',
  'purchases.bills': 'Bills',
  'purchases.orders': 'Purchase orders',
  'purchases.payments': 'Bill payments',
  'purchases.advances': 'Vendor advances',
  'purchases.credits': 'Vendor credits',
  'purchases.returns': 'Purchase returns',
  'purchases.refunds': 'Vendor refunds',
  'financials.accounts': 'Chart of accounts',
  'financials.journal': 'Journal entries',
  'financials.periods': 'Financial periods',
  'financials.settlements': 'Settlements',
  'banking.accounts': 'Bank accounts',
  'banking.reconcile': 'Bank reconciliation',
  'banking.transactions': 'Bank transactions',
  'inventory.items': 'Products and services',
  'inventory.adjustments': 'Stock adjustments',
  'reports.view': 'Reports',
  'donations.view': 'Donations',
  'donations.manage': 'Donations',
  'audit.logs': 'Audit log',
  'admin.members': 'Members and access',
  'admin.roles': 'Roles',
  'admin.subscription': 'Billing',
  'admin.taxes': 'Taxes',
};

/** `purchases.vendors.view` → `purchases.vendors`. */
export function permissionResource(permission: string): string {
  return permission.split('.').slice(0, 2).join('.');
}

/** The area name for a permission, or null when it has none (callers then say "this page", never the raw key). */
export function areaForPermission(permission: string | null | undefined): string | null {
  if (!permission) return null;
  return PERMISSION_AREA_LABELS[permissionResource(permission)] ?? null;
}
