import React from 'react';
import { useOutletContext, Link } from 'react-router-dom';
import { Lock, Sparkles, ArrowRight, ShieldAlert } from 'lucide-react';
import { ModuleKey, TenantContextType } from '../types';
import {
  FEATURE_BANKING_STATEMENT_IMPORT,
  useCapabilities,
} from '../lib/entitlements';
import {
  ENTITLEMENT_DENIED_EVENT,
  type EntitlementProblem,
} from '../lib/entitlementEvents';

export function useTenantContext(): TenantContextType {
  return useOutletContext<TenantContextType>();
}

export function useSubscription() {
  const { subscription, hasModule, updateSubscription } = useTenantContext();
  return {
    subscription,
    hasModule,
    updateSubscription,
    activeModules: subscription?.activeModules || [],
    planId: subscription?.planId || 'accounting',
  };
}

/** Shown on list/detail pages when the module is visible but writes are blocked (read-only grace). */
export const ReadOnlyModuleBanner: React.FC<{ module: ModuleKey; orgSlug?: string }> = ({ module, orgSlug }) => {
  const { canWrite, hasModule, hasPermission } = useTenantContext();
  if (!hasModule(module) || canWrite(module)) return null;
  const canManageBilling = hasPermission('admin.subscription.manage');
  const href = orgSlug ? `/org/${encodeURIComponent(orgSlug)}/settings?tab=subscription` : null;
  return (
    <div
      role="status"
      className="p-3 rounded-xl bg-amber-500/10 text-amber-700 dark:text-amber-300 text-xs font-semibold flex flex-wrap items-center justify-between gap-2"
      data-testid={`readonly-${module}`}
    >
      <span>This module is read-only until billing is updated.</span>
      {href && canManageBilling ? (
        <Link to={href} className="underline hover:no-underline">Update billing</Link>
      ) : (
        <span>Ask your workspace owner to update billing.</span>
      )}
    </div>
  );
};

export function usePermissions() {
  const { userPermissions, hasPermission, canAccess } = useTenantContext();
  return {
    permissions: userPermissions,
    hasPermission,
    canAccess,
  };
}

interface RequiresModuleProps {
  /** With several modules, any one of them unlocks the page. */
  module: ModuleKey | ModuleKey[];
  fallback?: React.ReactNode;
  children: React.ReactNode;
}

export const RequiresModule: React.FC<RequiresModuleProps> = ({
  module,
  fallback,
  children,
}) => {
  const { hasModule, currentOrg } = useTenantContext();
  const modules = Array.isArray(module) ? module : [module];

  if (modules.some(hasModule)) {
    return <>{children}</>;
  }

  if (fallback !== undefined) {
    return <>{fallback}</>;
  }

  return <ModuleLockedCard module={modules[0]} orgSlug={currentOrg.slug} />;
};

/** Statement import page (#436): financials, hosted feature, or community — not financials alone. */
export const RequiresStatementImport: React.FC<{ children: React.ReactNode; fallback?: React.ReactNode }> = ({
  children,
  fallback,
}) => {
  const { currentOrg } = useTenantContext();
  const { canStatementImport } = useCapabilities();

  if (canStatementImport) {
    return <>{children}</>;
  }

  if (fallback !== undefined) {
    return <>{fallback}</>;
  }

  return <StatementImportUpgradeCard orgSlug={currentOrg.slug} />;
};

/** Opens the plan-limit dialog for hosted Free workspaces that lack statement import. */
export function announceStatementImportUpgrade(options?: {
  effectivePlan?: string | null;
  upgradeAvailable?: boolean;
  canManageBilling?: boolean;
}): void {
  if (typeof window === 'undefined') return;
  const detail: EntitlementProblem = {
    code: 'FEATURE_NOT_ENTITLED',
    title: 'Feature not included in your plan',
    detail: 'Statement import is available on Invoicing Pro and Accounting plans.',
    requiredFeature: FEATURE_BANKING_STATEMENT_IMPORT,
    effectivePlan: options?.effectivePlan ?? undefined,
    upgradeAvailable: !!options?.upgradeAvailable,
    canManageBilling: !!options?.canManageBilling,
  };
  window.dispatchEvent(new CustomEvent<EntitlementProblem>(ENTITLEMENT_DENIED_EVENT, { detail }));
}

interface HasPermissionProps {
  permission: string | string[];
  fallback?: React.ReactNode;
  children: React.ReactNode;
}

export const HasPermission: React.FC<HasPermissionProps> = ({
  permission,
  fallback = null,
  children,
}) => {
  const { hasPermission } = useTenantContext();

  if (hasPermission(permission)) {
    return <>{children}</>;
  }

  return <>{fallback}</>;
};

interface GateProps {
  module: ModuleKey;
  permission?: string | string[];
  fallback?: React.ReactNode;
  children: React.ReactNode;
}

export const Gate: React.FC<GateProps> = ({
  module,
  permission,
  fallback,
  children,
}) => {
  const { canAccess, currentOrg } = useTenantContext();

  if (canAccess(module, Array.isArray(permission) ? permission[0] : permission)) {
    return <>{children}</>;
  }

  if (fallback !== undefined) {
    return <>{fallback}</>;
  }

  return <ModuleLockedCard module={module} orgSlug={currentOrg.slug} />;
};

const MODULE_METADATA: Record<ModuleKey, { title: string; desc: string; requiredPlan: string }> = {
  sales: {
    title: 'Invoicing & Sales',
    desc: 'Create professional invoices, quotes, customer accounts, and record receipts.',
    requiredPlan: 'Invoicing',
  },
  financials: {
    title: 'General Ledger & Banking',
    desc: 'Full double-entry bookkeeping, chart of accounts, bank feeds, and journal entries.',
    requiredPlan: 'Accounting',
  },
  purchases: {
    title: 'Purchases & Bills',
    desc: 'Track vendors, purchase orders, incoming bills, and outgoing expense payments.',
    requiredPlan: 'Invoicing',
  },
  inventory: {
    title: 'Inventory & Items',
    desc: 'Track stock on hand and its cost: bills add stock, invoices use it up, adjustments record counts and write-offs.',
    requiredPlan: 'Enterprise',
  },
  reports: {
    title: 'Financial Reports',
    desc: 'Real-time Profit & Loss, Balance Sheets, Trial Balance, Aging, and Tax Reports.',
    requiredPlan: 'Accounting',
  },
  donations: {
    title: 'Donations & Non-Profit',
    desc: 'Manage charitable donors, donor receipts, fund allocations, and tax summaries.',
    requiredPlan: 'Enterprise',
  },
  audit: {
    title: 'Audit Trail & Compliance',
    desc: 'Field-level mutation tracking, compliance audit logs, and security event history.',
    requiredPlan: 'Enterprise',
  },
};

/** True when the snapshot lists at least one self-service plan change. */
export function shouldShowUpgradeAvailableBadge(
  entitlements: { allowedTransitions?: readonly string[] } | null | undefined,
): boolean {
  return (entitlements?.allowedTransitions?.length ?? 0) > 0;
}

/** Locked state for the statement-import route when the plan lacks the feature (#436 I1). */
export const StatementImportUpgradeCard: React.FC<{ orgSlug?: string }> = ({ orgSlug }) => {
  const { hasPermission } = useTenantContext();
  const { entitlements, canManageBilling } = useCapabilities();
  const upgradeAvailable = shouldShowUpgradeAvailableBadge(entitlements);
  const billingManage = canManageBilling || hasPermission('admin.subscription.manage');

  return (
    <div className="space-y-6 w-full animate-in fade-in duration-200 text-[var(--text-main)]">
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 p-4 sm:p-5 rounded-2xl bg-gradient-to-r from-[var(--bg-surface)] via-[var(--bg-surface)] to-[var(--brand-subtle)] shadow-xl">
        <div className="space-y-1">
          <div className="flex items-center gap-2">
            <span className="w-2 h-2 rounded-full bg-amber-400 animate-pulse" />
            <span className="text-[10px] sm:text-xs font-semibold uppercase tracking-wider text-amber-400">
              Plan Feature
            </span>
          </div>
          <h1 className="text-xl sm:text-2xl font-bold text-[var(--text-main)] tracking-tight">
            Statement import
          </h1>
          <p className="text-xs text-[var(--text-muted)]">
            Available on <strong className="text-[var(--text-main)]">Invoicing Pro</strong> and Accounting.
          </p>
        </div>
        {orgSlug && upgradeAvailable && (
          <div className="px-3 py-1.5 rounded-xl bg-amber-500/10 text-xs text-amber-400 font-medium">
            Upgrade Available
          </div>
        )}
      </div>

      <div className="w-full max-w-xl mx-auto my-8 p-8 rounded-3xl bg-[var(--bg-surface)] backdrop-blur-2xl shadow-2xl shadow-black/20 text-center animate-fade-in border border-transparent">
        <div className="w-16 h-16 mx-auto mb-6 rounded-2xl bg-[var(--brand-subtle)] text-[var(--brand-primary)] flex items-center justify-center shadow-inner">
          <Lock className="w-8 h-8" />
        </div>
        <div className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full bg-[var(--bg-subtle)] text-[var(--brand-primary)] text-xs font-semibold uppercase tracking-wider mb-3">
          <Sparkles className="w-3.5 h-3.5" />
          Invoicing Pro Feature
        </div>
        <h2 className="text-2xl font-bold tracking-tight text-[var(--text-main)] mb-2">
          Statement import is locked
        </h2>
        <p className="text-sm text-[var(--text-secondary)] leading-relaxed max-w-md mx-auto mb-8">
          Import CSV or OFX bank statements, match lines, and record expenses without leaving Banking.
          Upgrade to unlock statement import on this workspace.
        </p>
        <div className="flex flex-col sm:flex-row items-center justify-center gap-3">
          {orgSlug && billingManage && (
            <Link
              to={`/org/${orgSlug}/settings?tab=subscription`}
              className="w-full sm:w-auto inline-flex items-center justify-center gap-2 px-6 py-3 rounded-xl bg-[var(--brand-primary)] text-white text-sm font-semibold shadow-lg shadow-[var(--brand-primary)]/25 hover:brightness-110 active:scale-[0.98] transition-all"
            >
              Manage Subscription
              <ArrowRight className="w-4 h-4" />
            </Link>
          )}
          {orgSlug && (
            <Link
              to={`/org/${orgSlug}/banking`}
              className="w-full sm:w-auto px-6 py-3 rounded-xl text-sm font-medium text-[var(--text-secondary)] hover:text-[var(--text-main)] hover:bg-[var(--bg-surface-hover)] transition-all"
            >
              Back to Banking
            </Link>
          )}
        </div>
      </div>
    </div>
  );
};

export const ModuleLockedCard: React.FC<{ module: ModuleKey; orgSlug?: string }> = ({
  module,
  orgSlug,
}) => {
  const { hasModule } = useTenantContext();
  const { entitlements } = useCapabilities();
  const upgradeAvailable = shouldShowUpgradeAvailableBadge(entitlements);
  const info = MODULE_METADATA[module] || {
    title: module,
    desc: 'This module is not active in your current subscription plan.',
    requiredPlan: 'Accounting',
  };

  return (
    <div className="space-y-6 w-full animate-in fade-in duration-200 text-[var(--text-main)]">
      {/* Workspace Context Top Bar */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 p-4 sm:p-5 rounded-2xl bg-gradient-to-r from-[var(--bg-surface)] via-[var(--bg-surface)] to-[var(--brand-subtle)] shadow-xl">
        <div className="space-y-1">
          <div className="flex items-center gap-2">
            <span className="w-2 h-2 rounded-full bg-amber-400 animate-pulse" />
            <span className="text-[10px] sm:text-xs font-semibold uppercase tracking-wider text-amber-400">
              Subscription Gated Module
            </span>
          </div>
          <h1 className="text-xl sm:text-2xl font-bold text-[var(--text-main)] tracking-tight">
            {info.title}
          </h1>
          <p className="text-xs text-[var(--text-muted)]">
            This module requires the <strong className="text-[var(--text-main)]">{info.requiredPlan}</strong> tier.
          </p>
        </div>

        {orgSlug && (
          <div className="flex flex-wrap items-center gap-2 sm:self-center">
            <div className="px-3 py-1.5 rounded-xl bg-[var(--bg-subtle)] shadow-sm text-xs">
              <span className="text-[var(--text-muted)]">Slug: </span>
              <code className="text-[var(--brand-primary)] font-mono font-medium">/org/{orgSlug}</code>
            </div>
            {upgradeAvailable && (
              <div className="px-3 py-1.5 rounded-xl bg-amber-500/10 text-xs text-amber-400 font-medium">
                Upgrade Available
              </div>
            )}
          </div>
        )}
      </div>

      {/* Centered Locked Feature Card */}
      <div className="w-full max-w-xl mx-auto my-8 p-8 rounded-3xl bg-[var(--bg-surface)] backdrop-blur-2xl shadow-2xl shadow-black/20 text-center animate-fade-in border border-transparent">
        <div className="w-16 h-16 mx-auto mb-6 rounded-2xl bg-[var(--brand-subtle)] text-[var(--brand-primary)] flex items-center justify-center shadow-inner">
          <Lock className="w-8 h-8" />
        </div>

        <div className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full bg-[var(--bg-subtle)] text-[var(--brand-primary)] text-xs font-semibold uppercase tracking-wider mb-3">
          <Sparkles className="w-3.5 h-3.5" />
          {info.requiredPlan} Tier Feature
        </div>

        <h2 className="text-2xl font-bold tracking-tight text-[var(--text-main)] mb-2">
          {info.title} is locked
        </h2>

        <p className="text-sm text-[var(--text-secondary)] leading-relaxed max-w-md mx-auto mb-8">
          {info.desc} Upgrade your organization plan to unlock this module and enhance your workspace.
        </p>

        <div className="flex flex-col sm:flex-row items-center justify-center gap-3">
          {orgSlug && (
            <Link
              to={`/org/${orgSlug}/settings?tab=subscription`}
              className="w-full sm:w-auto inline-flex items-center justify-center gap-2 px-6 py-3 rounded-xl bg-[var(--brand-primary)] text-white text-sm font-semibold shadow-lg shadow-[var(--brand-primary)]/25 hover:brightness-110 active:scale-[0.98] transition-all"
            >
              Manage Subscription
              <ArrowRight className="w-4 h-4" />
            </Link>
          )}
          {orgSlug && (
            <Link
              to={`/org/${orgSlug}/dashboard`}
              className="w-full sm:w-auto px-6 py-3 rounded-xl text-sm font-medium text-[var(--text-secondary)] hover:text-[var(--text-main)] hover:bg-[var(--bg-surface-hover)] transition-all"
            >
              {hasModule('financials') ? 'Back to Dashboard' : 'Back to Invoices'}
            </Link>
          )}
        </div>
      </div>
    </div>
  );
};

export const PermissionDeniedCard: React.FC<{ permission?: string }> = ({ permission }) => {
  return (
    <div className="w-full max-w-md mx-auto my-12 p-8 rounded-3xl bg-[var(--bg-surface)] backdrop-blur-2xl shadow-xl text-center animate-fade-in">
      <div className="w-14 h-14 mx-auto mb-5 rounded-2xl bg-amber-500/10 text-amber-500 flex items-center justify-center">
        <ShieldAlert className="w-7 h-7" />
      </div>

      <h3 className="text-xl font-bold text-[var(--text-main)] mb-2">
        Access Restricted
      </h3>

      <p className="text-sm text-[var(--text-secondary)] leading-relaxed mb-6">
        Your role does not have the necessary permissions ({permission || 'authorized role'}) to access this action.
      </p>

      <p className="text-xs text-[var(--text-muted)]">
        Contact your workspace administrator to request access.
      </p>
    </div>
  );
};
