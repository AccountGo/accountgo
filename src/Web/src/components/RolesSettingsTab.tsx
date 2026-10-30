import React, { useState, useEffect } from 'react';
import {
  Shield,
  ShieldCheck,
  Check,
  Lock,
  Users,
  Search,
  RefreshCw,
  AlertCircle,
  Save,
} from 'lucide-react';
import { ModuleKey, TenantContextType } from '../types';
import { useAuth } from '../auth/AuthContext';
import { SecurityCapabilities } from '../types/auth';

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

interface WorkspaceSecurityRole {
  id: number;
  name: string;
  displayName?: string;
  permissions: string[];
  memberCount: number;
}

// `module` marks permissions that only mean something when that module is on; the rest always apply.
// A group's `module` may list several modules; any one of them shows it.
const PERMISSION_GROUPS: Array<{ group: string; module?: ModuleKey | ModuleKey[]; permissions: Array<{ key: string; label: string; module?: ModuleKey }> }> = [
  {
    group: 'Sales & Invoicing',
    module: 'sales',
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
    group: 'General Ledger',
    module: 'financials',
    permissions: [
      { key: 'financials.accounts.view', label: 'View Chart of Accounts' },
      { key: 'financials.accounts.manage', label: 'Manage Accounts' },
      { key: 'financials.journal.view', label: 'View Journal Entries' },
      { key: 'financials.journal.create', label: 'Create Journal Entries' },
      { key: 'financials.journal.post', label: 'Post to General Ledger' },
      { key: 'financials.periods.close', label: 'Close / Lock Accounting Periods' },
    ],
  },
  {
    group: 'Banking',
    module: ['sales', 'purchases', 'financials'],
    permissions: [
      { key: 'banking.accounts.view', label: 'View Bank Accounts' },
      { key: 'banking.accounts.manage', label: 'Manage Bank Accounts' },
      { key: 'banking.transactions.create', label: 'Record Bank Transactions' },
      { key: 'banking.reconcile', label: 'Reconcile Bank Accounts' },
      { key: 'banking.reconcile.undo', label: 'Undo Bank Reconciliation' },
    ],
  },
  {
    group: 'Purchases & Bills',
    module: 'purchases',
    permissions: [
      { key: 'purchases.vendors.view', label: 'View Vendors' },
      { key: 'purchases.vendors.manage', label: 'Create & Edit Vendors' },
      { key: 'purchases.orders.view', label: 'View Purchase Orders' },
      { key: 'purchases.bills.view', label: 'View Bills' },
      { key: 'purchases.bills.create', label: 'Draft Bills' },
      { key: 'purchases.bills.post', label: 'Post / Approve Bills' },
      { key: 'purchases.payments.create', label: 'Pay Bills' },
    ],
  },
  {
    group: 'Products & Services',
    module: ['sales', 'purchases'],
    permissions: [
      { key: 'inventory.items.view', label: 'View Products & Services' },
      { key: 'inventory.items.manage', label: 'Create & Edit Products & Services' },
    ],
  },
  {
    group: 'Inventory',
    module: 'inventory',
    permissions: [
      { key: 'inventory.adjustments.post', label: 'Adjust stock' },
    ],
  },
  {
    group: 'Governance & Administration',
    permissions: [
      { key: 'reports.view', label: 'View Financial Reports', module: 'reports' },
      { key: 'audit.logs.view', label: 'View Audit Logs', module: 'audit' },
      { key: 'admin.members.manage', label: 'Manage Team Members' },
      { key: 'admin.roles.manage', label: 'Manage Roles & Permissions' },
      { key: 'admin.subscription.manage', label: 'Manage Subscription & Plan' },
      { key: 'admin.taxes.manage', label: 'Manage Taxes' },
    ],
  },
];

export const RolesSettingsTab: React.FC<RolesSettingsTabProps> = ({ tenantContext }) => {
  const { hasModule } = tenantContext;
  const groupShown = (module?: ModuleKey | ModuleKey[]) => !module || (Array.isArray(module) ? module : [module]).some(hasModule);
  const shownKeys = new Set(
    PERMISSION_GROUPS.filter((g) => groupShown(g.module)).flatMap((g) =>
      g.permissions.filter((p) => !p.module || hasModule(p.module)).map((p) => p.key),
    ),
  );
  const shownCount = (role: RoleDefinition) => role.permissions.filter((key) => shownKeys.has(key)).length;
  const { authFetch } = useAuth();
  const [roles, setRoles] = useState<RoleDefinition[]>([]);
  const [selectedRoleId, setSelectedRoleId] = useState<string>('');
  const [searchQuery, setSearchQuery] = useState('');
  const [loadFailed, setLoadFailed] = useState(false);

  // Security capabilities (open-core gating)
  const [capabilities, setCapabilities] = useState<SecurityCapabilities | null>(null);

  // Workspace MFA Policy State
  const [mfaPolicy, setMfaPolicy] = useState<'Optional' | 'RequiredForAdmins' | 'RequiredForAll'>('Optional');
  const [gracePeriodDays, setGracePeriodDays] = useState<number>(0);
  const [requireMfaEveryLogin, setRequireMfaEveryLogin] = useState<boolean>(false);
  const [isLoadingPolicy, setIsLoadingPolicy] = useState(false);
  const [isSavingPolicy, setIsSavingPolicy] = useState(false);
  const [policySuccess, setPolicySuccess] = useState<string | null>(null);
  const [policyError, setPolicyError] = useState<string | null>(null);

  useEffect(() => {
    let mounted = true;

    authFetch('/api/security/capabilities')
      .then(async (res) => {
        if (res.ok && mounted) {
          const caps: SecurityCapabilities = await res.json();
          setCapabilities(caps);
          if (caps.mfaAvailable) {
            fetchPolicy();
          }
        }
      })
      .catch(() => {});

    const fetchPolicy = async () => {
      setIsLoadingPolicy(true);
      try {
        const res = await authFetch('/api/security/policy');
        if (res.ok) {
          const data = await res.json();
          if (data.mfaPolicy && mounted) setMfaPolicy(data.mfaPolicy);
          if (data.requireMfaEveryLogin !== undefined && mounted) setRequireMfaEveryLogin(data.requireMfaEveryLogin);
        }
      } catch (err) {
        console.error('Failed to load security policy:', err);
      } finally {
        if (mounted) setIsLoadingPolicy(false);
      }
    };
    const fetchRoles = async () => {
      try {
        // Workspace-safe role templates (platform roles excluded); memberCount is per workspace.
        const res = await authFetch('/api/organizations/current/security-roles');
        if (!res.ok) {
          if (mounted) setLoadFailed(true);
          return;
        }
        const data = await res.json() as WorkspaceSecurityRole[];
        if (Array.isArray(data) && data.length > 0 && mounted) {
          const liveRoles: RoleDefinition[] = data.map((r) => ({
            id: r.name.toLowerCase(),
            name: r.displayName || r.name,
            description: 'Built-in role template. Assign it to Members from Team & Members.',
            userCount: r.memberCount,
            isSystem: true,
            permissions: r.permissions,
          }));

          setRoles(liveRoles);
          setSelectedRoleId((prev) => {
            if (!prev || !liveRoles.some((lr) => lr.id === prev)) {
              return liveRoles[0].id;
            }
            return prev;
          });
        }
      } catch (err) {
        console.error('Failed to load roles from API:', err);
        if (mounted) setLoadFailed(true);
      }
    };

    fetchRoles();
    return () => {
      mounted = false;
    };
  }, [authFetch]);

  const selectedRole = roles.find((r) => r.id === selectedRoleId) || roles[0];

  const handleSavePolicy = async (e: React.FormEvent) => {
    e.preventDefault();
    setIsSavingPolicy(true);
    setPolicySuccess(null);
    setPolicyError(null);
    try {
      const res = await authFetch('/api/security/policy', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          mfaPolicy,
          gracePeriodDays: Number(gracePeriodDays),
          requireMfaEveryLogin,
        }),
      });
      if (res.ok) {
        setPolicySuccess('Workspace security policy updated successfully.');
        setTimeout(() => setPolicySuccess(null), 4000);
      } else {
        const err = await res.json().catch(() => null);
        setPolicyError(err?.detail || 'Failed to update security policy.');
      }
    } catch (err: any) {
      setPolicyError(err.message || 'Network error saving security policy.');
    } finally {
      setIsSavingPolicy(false);
    }
  };

  if (loadFailed) {
    return (
      <div role="alert" className="p-12 text-center text-xs text-[var(--text-muted)] rounded-2xl bg-[var(--bg-surface)] shadow-md">
        Could not load role definitions. Refresh the page to try again.
      </div>
    );
  }

  if (roles.length === 0 || !selectedRole) {
    return (
      <div className="p-12 text-center text-xs text-[var(--text-muted)] rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md flex items-center justify-center gap-2">
        <RefreshCw className="w-4 h-4 animate-spin text-[var(--brand-primary)]" />
        <span>Loading role security definitions from live database...</span>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {/* Role Selection List */}
      <div className="p-6 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md space-y-4">
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
          <div>
            <h3 className="text-base font-bold text-[var(--text-main)]">Tenant Roles &amp; Permissions</h3>
            <p className="text-xs text-[var(--text-muted)] mt-0.5">
              Select a role to inspect its permissions. Owners and Admins always have full access.
            </p>
          </div>
        </div>

        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3 pt-2">
          {roles.map((role) => {
            const isSelected = selectedRoleId === role.id;

            return (
              <button
                key={role.id}
                type="button"
                aria-pressed={isSelected}
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
                  <span>
                    {shownCount(role)} permission{shownCount(role) !== 1 ? 's' : ''}
                  </span>
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
            <Search className="w-4 h-4 text-[var(--text-muted)] absolute left-3 top-2.5 pointer-events-none" />
            <input
              type="text"
              aria-label="Search permissions"
              placeholder="Search permissions..."
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              className="w-full h-9 pl-9 pr-3 rounded-xl bg-[var(--bg-subtle)] text-xs text-[var(--text-main)] focus:outline-none focus:ring-1 focus:ring-[var(--brand-ring)]"
            />
          </div>
        </div>

        <div className="space-y-5 pt-1">
          {PERMISSION_GROUPS.filter((group) => groupShown(group.module)).map((group) => {
            const filteredPermissions = group.permissions.filter((p) =>
              (!p.module || hasModule(p.module)) && (p.label.toLowerCase().includes(searchQuery.toLowerCase()) ||
              p.key.toLowerCase().includes(searchQuery.toLowerCase()))
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

      {/* Workspace Multi-Factor Authentication (MFA) Policy Card - Capability Gated */}
      {capabilities?.mfaAvailable && (
        <div className="p-6 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md space-y-4">
          <div className="flex items-center gap-2.5 pb-2 border-b border-[var(--border-subtle)]">
            <ShieldCheck className="w-5 h-5 text-emerald-400" />
            <div>
              <h2 className="text-base font-bold text-[var(--text-main)]">
                Workspace MFA Enforcement Policy
              </h2>
              <p className="text-xs text-[var(--text-muted)]">
                Require Multi-Factor Authentication for members of this workspace.
              </p>
            </div>
          </div>

          {policySuccess && (
            <div className="p-3 rounded-xl bg-emerald-500/10 border border-emerald-500/20 text-emerald-400 text-xs flex items-center gap-2">
              <Check className="w-4 h-4 shrink-0" />
              <span>{policySuccess}</span>
            </div>
          )}

          {policyError && (
            <div className="p-3 rounded-xl bg-red-500/10 border border-red-500/20 text-red-400 text-xs flex items-center gap-2">
              <AlertCircle className="w-4 h-4 shrink-0" />
              <span>{policyError}</span>
            </div>
          )}

          <form onSubmit={handleSavePolicy} className="space-y-4">
            <div className="space-y-2">
              <label className="text-xs font-semibold text-[var(--text-main)]">Enforcement Level</label>
              <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
                {[
                  { value: 'Optional', label: 'Optional (Default)', desc: 'Users choose whether to enable MFA' },
                  { value: 'RequiredForAdmins', label: 'Required for Admins', desc: 'Workspace Owners and Admins must enable MFA' },
                  { value: 'RequiredForAll', label: 'Required for All', desc: 'All workspace members must enable MFA to access' },
                ].map((opt) => (
                  <label
                    key={opt.value}
                    className={`p-3.5 rounded-xl border cursor-pointer transition-all flex flex-col justify-between gap-1.5 ${
                      mfaPolicy === opt.value
                        ? 'border-[var(--brand-primary)] bg-[var(--brand-subtle)]/30 text-[var(--text-main)]'
                        : 'border-[var(--border-subtle)] bg-[var(--bg-subtle)] text-[var(--text-secondary)] hover:bg-[var(--bg-surface-hover)]'
                    }`}
                  >
                    <div className="flex items-center justify-between">
                      <span className="text-xs font-bold">{opt.label}</span>
                      <input
                        type="radio"
                        name="mfaPolicy"
                        value={opt.value}
                        checked={mfaPolicy === opt.value}
                        onChange={() => setMfaPolicy(opt.value as any)}
                        className="accent-[var(--brand-primary)]"
                      />
                    </div>
                    <span className="text-[11px] text-[var(--text-muted)] leading-tight">{opt.desc}</span>
                  </label>
                ))}
              </div>
            </div>

            {mfaPolicy !== 'Optional' && (
              <div className="space-y-1.5">
                <label className="text-xs font-semibold text-[var(--text-main)]">Grace Period for Existing Members</label>
                <select
                  value={gracePeriodDays}
                  onChange={(e) => setGracePeriodDays(Number(e.target.value))}
                  className="w-full sm:w-64 h-10 px-3 rounded-xl bg-[var(--bg-subtle)] text-xs text-[var(--text-main)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-ring)]"
                >
                  <option value={0}>Immediate (Enforce upon next login)</option>
                  <option value={3}>3 Days Grace Period</option>
                  <option value={7}>7 Days Grace Period</option>
                  <option value={14}>14 Days Grace Period</option>
                </select>
                <p className="text-[11px] text-[var(--text-muted)]">
                  Members logging in during the grace period will be allowed in without enrollment. Once expired, they are redirected to MFA enrollment.
                </p>
              </div>
            )}

            <div className="pt-2 border-t border-[var(--border-subtle)]">
              <label className="flex items-start gap-2.5 cursor-pointer">
                <input
                  type="checkbox"
                  checked={requireMfaEveryLogin}
                  onChange={(e) => setRequireMfaEveryLogin(e.target.checked)}
                  className="mt-0.5 rounded accent-[var(--brand-primary)]"
                />
                <div>
                  <span className="text-xs font-semibold text-[var(--text-main)]">
                    Require MFA code on every login
                  </span>
                  <p className="text-[11px] text-[var(--text-muted)]">
                    Disable the 30-day &quot;Remember this device&quot; bypass for all workspace members.
                  </p>
                </div>
              </label>
            </div>

            <div className="flex justify-end pt-2">
              <button
                type="submit"
                disabled={isSavingPolicy || isLoadingPolicy}
                className="h-10 px-5 rounded-xl bg-[var(--brand-primary)] hover:brightness-110 text-white text-xs font-semibold transition-all shadow-md active:scale-[0.98] flex items-center gap-2 cursor-pointer disabled:opacity-50"
              >
                {isSavingPolicy ? (
                  <>
                    <RefreshCw className="w-3.5 h-3.5 animate-spin" />
                    <span>Saving...</span>
                  </>
                ) : (
                  <>
                    <Save className="w-3.5 h-3.5" />
                    <span>Save Security Policy</span>
                  </>
                )}
              </button>
            </div>
          </form>
        </div>
      )}
    </div>
  );
};
