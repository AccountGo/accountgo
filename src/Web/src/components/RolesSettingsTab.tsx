import React, { useState, useEffect } from 'react';
import {
  Shield,
  Check,
  Lock,
  Users,
  Search,
} from 'lucide-react';
import { TenantContextType } from '../types';
import { useAuth } from '../auth/AuthContext';

interface RolesSettingsTabProps {
  tenantContext: TenantContextType;
}

interface RoleDefinition {
  id: string;
  name: string;
  description: string;
  userCount: number;
  permissions: string[];
  isSystem?: boolean;
}

const PREDEFINED_ROLES: RoleDefinition[] = [
  {
    id: 'sysadmin',
    name: 'Owner / Administrator',
    description: 'Full administrative and operational privileges across all subscribed modules.',
    userCount: 1,
    isSystem: true,
    permissions: [
      'sales.customers.view',
      'sales.customers.manage',
      'sales.invoices.view',
      'sales.invoices.create',
      'sales.invoices.post',
      'sales.receipts.view',
      'sales.receipts.create',
      'sales.quotations.view',
      'financials.accounts.view',
      'financials.accounts.manage',
      'financials.journal.view',
      'financials.journal.create',
      'financials.journal.post',
      'financials.cashbanks.view',
      'financials.cashbanks.manage',
      'purchases.vendors.view',
      'purchases.vendors.manage',
      'purchases.orders.view',
      'purchases.bills.view',
      'purchases.bills.create',
      'purchases.bills.post',
      'inventory.items.view',
      'inventory.items.manage',
      'reports.view',
      'audit.logs.view',
      'admin.members.manage',
      'admin.roles.manage',
      'admin.subscription.manage',
    ],
  },
  {
    id: 'accountant',
    name: 'Accountant',
    description: 'Full write access to General Ledger, Banking, Invoices, Bills, and Financial Reports.',
    userCount: 1,
    permissions: [
      'sales.customers.view',
      'sales.invoices.view',
      'sales.invoices.create',
      'sales.invoices.post',
      'sales.receipts.view',
      'sales.receipts.create',
      'financials.accounts.view',
      'financials.journal.view',
      'financials.journal.create',
      'financials.journal.post',
      'financials.cashbanks.view',
      'purchases.vendors.view',
      'purchases.bills.view',
      'purchases.bills.create',
      'purchases.bills.post',
      'reports.view',
    ],
  },
  {
    id: 'sales_rep',
    name: 'Sales Representative',
    description: 'Create and manage quotations, customer accounts, and draft sales invoices.',
    userCount: 2,
    permissions: [
      'sales.customers.view',
      'sales.customers.manage',
      'sales.invoices.view',
      'sales.invoices.create',
      'sales.quotations.view',
    ],
  },
  {
    id: 'purchasing_agent',
    name: 'Purchasing Agent',
    description: 'Manage vendor catalog, draft purchase orders, and record received bills.',
    userCount: 1,
    permissions: [
      'purchases.vendors.view',
      'purchases.vendors.manage',
      'purchases.orders.view',
      'purchases.bills.view',
      'purchases.bills.create',
    ],
  },
  {
    id: 'viewer',
    name: 'Auditor / Viewer',
    description: 'Strictly read-only access to records and financial summaries for reporting/compliance.',
    userCount: 1,
    permissions: [
      'sales.customers.view',
      'sales.invoices.view',
      'financials.accounts.view',
      'financials.journal.view',
      'purchases.vendors.view',
      'purchases.bills.view',
      'reports.view',
      'audit.logs.view',
    ],
  },
];

const PERMISSION_GROUPS = [
  {
    group: 'Sales & Invoicing',
    permissions: [
      { key: 'sales.customers.view', label: 'View Customers' },
      { key: 'sales.customers.manage', label: 'Create & Edit Customers' },
      { key: 'sales.quotations.view', label: 'View Quotations' },
      { key: 'sales.invoices.view', label: 'View Sales Invoices' },
      { key: 'sales.invoices.create', label: 'Draft Invoices' },
      { key: 'sales.invoices.post', label: 'Post / Issue Invoices' },
      { key: 'sales.receipts.create', label: 'Record Customer Receipts' },
    ],
  },
  {
    group: 'General Ledger & Banking',
    permissions: [
      { key: 'financials.accounts.view', label: 'View Chart of Accounts' },
      { key: 'financials.accounts.manage', label: 'Manage Accounts' },
      { key: 'financials.journal.view', label: 'View Journal Entries' },
      { key: 'financials.journal.create', label: 'Create Journal Entries' },
      { key: 'financials.journal.post', label: 'Post to General Ledger' },
      { key: 'financials.cashbanks.view', label: 'View Cash & Bank Balances' },
    ],
  },
  {
    group: 'Purchases & Bills',
    permissions: [
      { key: 'purchases.vendors.view', label: 'View Vendors' },
      { key: 'purchases.vendors.manage', label: 'Create & Edit Vendors' },
      { key: 'purchases.orders.view', label: 'View Purchase Orders' },
      { key: 'purchases.bills.view', label: 'View Bills' },
      { key: 'purchases.bills.create', label: 'Draft Bills' },
      { key: 'purchases.bills.post', label: 'Post / Approve Bills' },
    ],
  },
  {
    group: 'Governance & Administration',
    permissions: [
      { key: 'reports.view', label: 'View Financial Reports' },
      { key: 'audit.logs.view', label: 'View Audit Logs' },
      { key: 'admin.members.manage', label: 'Manage Team Members' },
      { key: 'admin.roles.manage', label: 'Manage Roles & Permissions' },
      { key: 'admin.subscription.manage', label: 'Manage Subscription & Plan' },
    ],
  },
];

export const RolesSettingsTab: React.FC<RolesSettingsTabProps> = () => {
  const { authFetch } = useAuth();
  const [roles, setRoles] = useState<RoleDefinition[]>(PREDEFINED_ROLES);
  const [selectedRoleId, setSelectedRoleId] = useState<string>('sysadmin');
  const [searchQuery, setSearchQuery] = useState('');

  useEffect(() => {
    let mounted = true;
    const fetchRoles = async () => {
      try {
        const res = await authFetch('/api/Administration/roles');
        if (res.ok) {
          const data = await res.json();
          if (Array.isArray(data) && data.length > 0 && mounted) {
            // Map live database roles
            const liveRoles: RoleDefinition[] = data.map((r: any) => {
              const perms = (r.permissions || []).map((p: any) => p.name || p);
              return {
                id: r.name?.toLowerCase() || r.id?.toString(),
                name: r.displayName || r.name,
                description: r.name === 'SystemAdministrators'
                  ? 'Full administrative and operational privileges across all subscribed modules.'
                  : 'Standard operational permissions across subscribed modules.',
                userCount: r.users?.length || 1,
                isSystem: true,
                permissions: perms.length > 0 ? perms : PREDEFINED_ROLES[0].permissions,
              };
            });

            // Merge with standard definitions
            setRoles([...liveRoles, ...PREDEFINED_ROLES.slice(1)]);
          }
        }
      } catch {}
    };

    fetchRoles();
    return () => {
      mounted = false;
    };
  }, [authFetch]);

  const selectedRole = roles.find((r) => r.id === selectedRoleId) || roles[0];

  return (
    <div className="space-y-6">
      {/* Role Selection List */}
      <div className="p-6 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md space-y-4">
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
          <div>
            <h3 className="text-base font-bold text-[var(--text-main)]">Tenant Roles &amp; Permissions</h3>
            <p className="text-xs text-[var(--text-muted)] mt-0.5">
              Select a role to inspect its granular permission matrix across subscribed modules.
            </p>
          </div>
          <button
            type="button"
            className="h-9 px-3.5 self-start sm:self-auto rounded-xl bg-[var(--brand-primary)] text-white text-xs font-semibold shadow-sm hover:brightness-110 active:scale-[0.98] transition-all cursor-pointer"
          >
            + Create Custom Role
          </button>
        </div>

        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3 pt-2">
          {roles.map((role) => {
            const isSelected = selectedRoleId === role.id;

            return (
              <button
                key={role.id}
                type="button"
                onClick={() => setSelectedRoleId(role.id)}
                className={`p-4 rounded-xl text-left transition-all cursor-pointer flex flex-col justify-between ${
                  isSelected
                    ? 'bg-[var(--brand-subtle)] border border-[var(--brand-ring)] shadow-sm'
                    : 'bg-[var(--bg-subtle)] hover:bg-[var(--bg-surface-hover)] border border-transparent'
                }`}
              >
                <div>
                  <div className="flex items-center justify-between mb-1.5">
                    <span className="text-xs font-bold text-[var(--text-main)] flex items-center gap-1.5">
                      <Shield className="w-3.5 h-3.5 text-[var(--brand-primary)]" />
                      {role.name}
                    </span>
                    {role.isSystem && (
                      <span className="text-[9px] px-1.5 py-0.5 rounded bg-[var(--bg-surface)] text-[var(--text-muted)] font-mono uppercase font-bold">
                        Built-in
                      </span>
                    )}
                  </div>
                  <p className="text-[11px] text-[var(--text-secondary)] leading-relaxed line-clamp-2">
                    {role.description}
                  </p>
                </div>

                <div className="mt-3 pt-2 border-t border-[var(--bg-surface-hover)] flex items-center justify-between text-[10px] text-[var(--text-muted)]">
                  <span className="flex items-center gap-1">
                    <Users className="w-3 h-3" />
                    {role.userCount} member{role.userCount !== 1 ? 's' : ''}
                  </span>
                  <span>{role.permissions.length} permissions</span>
                </div>
              </button>
            );
          })}
        </div>
      </div>

      {/* Permissions Matrix for Selected Role */}
      <div className="p-6 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md space-y-5">
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
          <div>
            <h3 className="text-base font-bold text-[var(--text-main)]">
              Permissions for <span className="text-[var(--brand-primary)]">{selectedRole.name}</span>
            </h3>
            <p className="text-xs text-[var(--text-muted)] mt-0.5">
              Permissions are actively enforced on both API calls and UI buttons.
            </p>
          </div>

          <div className="relative w-full sm:w-64">
            <Search className="w-4 h-4 text-[var(--text-muted)] absolute left-3 top-2.5" />
            <input
              type="text"
              placeholder="Search permissions..."
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              className="w-full h-9 pl-9 pr-3 rounded-xl bg-[var(--bg-subtle)] text-xs text-[var(--text-main)] focus:outline-none focus:ring-1 focus:ring-[var(--brand-ring)]"
            />
          </div>
        </div>

        <div className="space-y-5 pt-1">
          {PERMISSION_GROUPS.map((group) => {
            const filteredPermissions = group.permissions.filter((p) =>
              p.label.toLowerCase().includes(searchQuery.toLowerCase()) ||
              p.key.toLowerCase().includes(searchQuery.toLowerCase())
            );

            if (filteredPermissions.length === 0) return null;

            return (
              <div key={group.group} className="space-y-2">
                <div className="text-[11px] font-bold uppercase tracking-wider text-[var(--brand-primary)] px-1">
                  {group.group}
                </div>

                <div className="grid grid-cols-1 md:grid-cols-2 gap-2">
                  {filteredPermissions.map((perm) => {
                    const isGranted = selectedRole.permissions.includes(perm.key);

                    return (
                      <div
                        key={perm.key}
                        className={`p-3 rounded-xl flex items-center justify-between gap-3 ${
                          isGranted
                            ? 'bg-[var(--bg-subtle)] text-[var(--text-main)]'
                            : 'bg-[var(--bg-subtle)]/40 text-[var(--text-muted)] opacity-60'
                        }`}
                      >
                        <div className="min-w-0">
                          <div className="text-xs font-semibold truncate">{perm.label}</div>
                          <div className="text-[10px] font-mono text-[var(--text-muted)] truncate">
                            {perm.key}
                          </div>
                        </div>

                        <div
                          className={`w-5 h-5 rounded-lg flex items-center justify-center shrink-0 ${
                            isGranted
                              ? 'bg-emerald-500/15 text-emerald-400'
                              : 'bg-slate-800 text-slate-500'
                          }`}
                        >
                          {isGranted ? (
                            <Check className="w-3.5 h-3.5" />
                          ) : (
                            <Lock className="w-3 h-3" />
                          )}
                        </div>
                      </div>
                    );
                  })}
                </div>
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
};
