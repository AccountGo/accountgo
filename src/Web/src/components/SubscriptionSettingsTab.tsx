import React, { useState } from 'react';
import {
  Sparkles,
  Check,
  Layers,
  ArrowRight,
  ShieldCheck,
  BookOpen,
  Receipt,
  ShoppingBag,
  Boxes,
  BarChart3,
  HeartHandshake,
  Calendar,
} from 'lucide-react';
import { ModuleKey, PlanKey, TenantContextType } from '../types';

interface SubscriptionSettingsTabProps {
  tenantContext: TenantContextType;
}

const MODULES_CATALOG: Array<{
  key: ModuleKey;
  name: string;
  description: string;
  icon: React.ComponentType<{ className?: string }>;
  isCore?: boolean;
}> = [
  {
    key: 'sales',
    name: 'Invoicing & Sales',
    description: 'Customers, quotations, sales orders, invoices, and payment receipts.',
    icon: Receipt,
    isCore: true,
  },
  {
    key: 'financials',
    name: 'General Ledger & Banking',
    description: 'Chart of accounts, cash & bank accounts, journal entries, and trial balance.',
    icon: BookOpen,
  },
  {
    key: 'purchases',
    name: 'Purchases & Bills',
    description: 'Vendors, purchase orders, vendor invoices, and bill payments.',
    icon: ShoppingBag,
  },
  {
    key: 'inventory',
    name: 'Inventory & Items',
    description: 'Product catalog, warehouse stock tracking, and inventory adjustments.',
    icon: Boxes,
  },
  {
    key: 'reports',
    name: 'Financial Reports',
    description: 'Income statements, balance sheets, customer/vendor aging, and tax summaries.',
    icon: BarChart3,
  },
  {
    key: 'donations',
    name: 'Donations & Non-Profit',
    description: 'Donor management, donation invoices, pledges, and tax receipts.',
    icon: HeartHandshake,
  },
  {
    key: 'audit',
    name: 'Audit Trail & Compliance',
    description: 'Comprehensive mutation auditing, field-level diffs, and security events.',
    icon: ShieldCheck,
  },
];

const PLANS: Array<{
  key: PlanKey;
  name: string;
  price: string;
  description: string;
  includedModules: ModuleKey[];
  badge?: string;
}> = [
  {
    key: 'invoicing',
    name: 'Invoicing',
    price: '$15',
    description: 'Simple, streamlined invoicing and billing for freelancers and solopreneurs.',
    includedModules: ['sales'],
  },
  {
    key: 'accounting',
    name: 'Accounting',
    price: '$39',
    description: 'Full double-entry bookkeeping, bank reconciliation, bills, and tax reports.',
    includedModules: ['sales', 'financials', 'purchases', 'reports'],
    badge: 'Most Popular',
  },
  {
    key: 'enterprise',
    name: 'Enterprise',
    price: '$99',
    description: 'Complete ERP suite with inventory, non-profit tools, audit logs, and whitelabeling.',
    includedModules: ['sales', 'financials', 'purchases', 'inventory', 'reports', 'donations', 'audit'],
  },
];

export const SubscriptionSettingsTab: React.FC<SubscriptionSettingsTabProps> = ({
  tenantContext,
}) => {
  const { subscription, updateSubscription } = tenantContext;
  const currentPlan = subscription?.planId || 'accounting';
  const activeModules = subscription?.activeModules || ['sales'];

  const [saving, setSaving] = useState(false);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);

  const handleToggleModule = async (moduleKey: ModuleKey) => {
    if (moduleKey === 'sales') return; // Core invoicing cannot be toggled off
    if (!updateSubscription) return;

    setSaving(true);
    let newModules: ModuleKey[];
    if (activeModules.includes(moduleKey)) {
      newModules = activeModules.filter((m) => m !== moduleKey);
    } else {
      newModules = [...activeModules, moduleKey];
    }

    try {
      await updateSubscription(currentPlan, newModules);
      setSuccessMessage(`Module "${moduleKey}" updated successfully.`);
      setTimeout(() => setSuccessMessage(null), 2500);
    } finally {
      setSaving(false);
    }
  };

  const handleSelectPlan = async (planKey: PlanKey) => {
    if (!updateSubscription) return;
    setSaving(true);

    try {
      await updateSubscription(planKey);
      setSuccessMessage(`Switched to the ${planKey.toUpperCase()} plan.`);
      setTimeout(() => setSuccessMessage(null), 2500);
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="space-y-6">
      {/* Current Plan Overview Card */}
      <div className="p-6 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md space-y-4">
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
          <div className="flex items-center gap-3">
            <div className="w-12 h-12 rounded-2xl bg-[var(--brand-subtle)] text-[var(--brand-primary)] flex items-center justify-center shadow-inner">
              <Sparkles className="w-6 h-6" />
            </div>
            <div>
              <div className="flex items-center gap-2">
                <h2 className="text-lg font-bold text-[var(--text-main)]">
                  {subscription?.planName || 'Accounting'} Plan
                </h2>
                <span className="px-2.5 py-0.5 rounded-full bg-emerald-500/10 text-emerald-400 text-xs font-semibold uppercase tracking-wider">
                  Active
                </span>
              </div>
              <p className="text-xs text-[var(--text-muted)] mt-0.5">
                Central Subscription &amp; Modular Capability Control
              </p>
            </div>
          </div>

          <div className="flex items-center gap-4 text-xs text-[var(--text-secondary)]">
            <div className="flex items-center gap-1.5">
              <Layers className="w-4 h-4 text-[var(--brand-primary)]" />
              <span>
                <strong>{activeModules.length}</strong> of 7 Modules Active
              </span>
            </div>
            <div className="flex items-center gap-1.5">
              <Calendar className="w-4 h-4 text-[var(--text-muted)]" />
              <span>Renews Dec 31, 2027</span>
            </div>
          </div>
        </div>

        {successMessage && (
          <div className="p-3 rounded-xl bg-emerald-500/10 text-emerald-400 text-xs font-semibold flex items-center gap-2 animate-fade-in">
            <Check className="w-4 h-4" />
            <span>{successMessage}</span>
          </div>
        )}
      </div>

      {/* Module Catalog & Interactive Switches */}
      <div className="p-6 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md space-y-4">
        <div>
          <h3 className="text-base font-bold text-[var(--text-main)]">Subscribed Modules &amp; Add-ons</h3>
          <p className="text-xs text-[var(--text-muted)] mt-0.5">
            Modules enabled below appear directly in your sidebar navigation and unlock corresponding API routes.
          </p>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-2 gap-3 pt-2">
          {MODULES_CATALOG.map((mod) => {
            const Icon = mod.icon;
            const isEnabled = activeModules.includes(mod.key);

            return (
              <div
                key={mod.key}
                className={`p-4 rounded-xl transition-all flex items-start justify-between gap-3 ${
                  isEnabled
                    ? 'bg-[var(--bg-subtle)] border border-[var(--brand-ring)]/20 shadow-sm'
                    : 'bg-[var(--bg-subtle)]/50 opacity-70 hover:opacity-100'
                }`}
              >
                <div className="flex items-start gap-3 min-w-0">
                  <div
                    className={`w-9 h-9 rounded-xl flex items-center justify-center shrink-0 transition-colors ${
                      isEnabled
                        ? 'bg-[var(--brand-subtle)] text-[var(--brand-primary)]'
                        : 'bg-[var(--bg-surface-hover)] text-[var(--text-muted)]'
                    }`}
                  >
                    <Icon className="w-4 h-4" />
                  </div>
                  <div className="min-w-0">
                    <div className="flex items-center gap-2">
                      <span className="text-xs font-bold text-[var(--text-main)] truncate">
                        {mod.name}
                      </span>
                      {mod.isCore && (
                        <span className="text-[9px] px-1.5 py-0.5 rounded bg-[var(--brand-subtle)] text-[var(--brand-primary)] font-mono font-bold uppercase">
                          Core
                        </span>
                      )}
                    </div>
                    <p className="text-[11px] text-[var(--text-muted)] mt-0.5 line-clamp-2 leading-relaxed">
                      {mod.description}
                    </p>
                  </div>
                </div>

                {/* Toggle switch */}
                <button
                  type="button"
                  disabled={mod.isCore || saving}
                  onClick={() => handleToggleModule(mod.key)}
                  className={`relative inline-flex h-6 w-11 shrink-0 cursor-pointer rounded-full transition-colors duration-200 ease-in-out focus:outline-none ${
                    mod.isCore ? 'opacity-40 cursor-not-allowed' : ''
                  } ${isEnabled ? 'bg-[var(--brand-primary)]' : 'bg-slate-700'}`}
                  title={mod.isCore ? 'Core feature cannot be disabled' : 'Toggle module'}
                >
                  <span
                    className={`pointer-events-none inline-block h-5 w-5 transform rounded-full bg-white shadow-lg ring-0 transition duration-200 ease-in-out ${
                      isEnabled ? 'translate-x-5' : 'translate-x-0.5'
                    } my-auto`}
                  />
                </button>
              </div>
            );
          })}
        </div>
      </div>

      {/* Plan Tier Comparison & Instant Upgrade */}
      <div className="p-6 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md space-y-4">
        <div>
          <h3 className="text-base font-bold text-[var(--text-main)]">Compare Plans &amp; Upgrade</h3>
          <p className="text-xs text-[var(--text-muted)] mt-0.5">
            Switch plans anytime. Upgrading instantly provisions additional modules.
          </p>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-3 gap-4 pt-2">
          {PLANS.map((plan) => {
            const isCurrent = currentPlan === plan.key;

            return (
              <div
                key={plan.key}
                className={`p-5 rounded-2xl flex flex-col justify-between transition-all relative ${
                  isCurrent
                    ? 'bg-[var(--brand-subtle)] border-2 border-[var(--brand-primary)] shadow-lg'
                    : 'bg-[var(--bg-subtle)] hover:bg-[var(--bg-surface-hover)] border border-transparent'
                }`}
              >
                {plan.badge && (
                  <div className="absolute -top-2.5 right-4 px-2 py-0.5 rounded-full bg-[var(--brand-primary)] text-white text-[10px] font-bold uppercase tracking-wider shadow">
                    {plan.badge}
                  </div>
                )}

                <div>
                  <div className="flex items-baseline justify-between mb-2">
                    <h4 className="text-sm font-bold text-[var(--text-main)]">{plan.name}</h4>
                    <span className="text-lg font-extrabold text-[var(--text-main)]">
                      {plan.price}
                      <span className="text-xs font-normal text-[var(--text-muted)]">/mo</span>
                    </span>
                  </div>

                  <p className="text-xs text-[var(--text-secondary)] leading-relaxed mb-4">
                    {plan.description}
                  </p>

                  <div className="space-y-1.5 mb-6 text-xs text-[var(--text-main)]">
                    <div className="font-semibold text-[10px] uppercase text-[var(--text-muted)] tracking-wider mb-1">
                      Included Modules:
                    </div>
                    {plan.includedModules.map((m) => (
                      <div key={m} className="flex items-center gap-1.5 text-xs text-[var(--text-secondary)]">
                        <Check className="w-3.5 h-3.5 text-emerald-400 shrink-0" />
                        <span className="capitalize">{m}</span>
                      </div>
                    ))}
                  </div>
                </div>

                <button
                  type="button"
                  disabled={isCurrent || saving}
                  onClick={() => handleSelectPlan(plan.key)}
                  className={`w-full h-10 rounded-xl text-xs font-bold transition-all flex items-center justify-center gap-1.5 ${
                    isCurrent
                      ? 'bg-[var(--brand-primary)] text-white cursor-default shadow-sm'
                      : 'bg-[var(--bg-surface)] hover:bg-[var(--bg-surface-hover)] text-[var(--text-main)] hover:text-[var(--brand-primary)] active:scale-[0.98]'
                  }`}
                >
                  {isCurrent ? (
                    <>
                      <Check className="w-3.5 h-3.5" />
                      Current Plan
                    </>
                  ) : (
                    <>
                      <span>Switch to {plan.name}</span>
                      <ArrowRight className="w-3.5 h-3.5" />
                    </>
                  )}
                </button>
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
};
