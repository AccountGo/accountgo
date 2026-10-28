import React, { useEffect, useState } from 'react';
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
  Lock,
  CreditCard,
  Loader2,
} from 'lucide-react';
import { ModuleKey, PlanKey, TenantContextType } from '../types';
import { normalizePlanKey } from '../lib/plans';
import { usePlanCatalog } from '../lib/usePlanCatalog';
import { useCapabilities } from '../lib/entitlements';
import { useBilling } from '../lib/useBilling';
import { UsageHint } from './UsageHint';

/** Safe outside <Router> (unit tests / SSR) — reads checkout return flag from the location bar. */
function readCheckoutParam(): string | null {
  if (typeof window === 'undefined') return null;
  return new URLSearchParams(window.location.search).get('checkout');
}

function clearCheckoutParam(): void {
  if (typeof window === 'undefined') return;
  const url = new URL(window.location.href);
  if (!url.searchParams.has('checkout')) return;
  url.searchParams.delete('checkout');
  const next = `${url.pathname}${url.search}${url.hash}`;
  window.history.replaceState({}, '', next);
}

type HostedPlanKey = 'invoicing_free' | 'invoicing_pro' | 'accounting_free' | 'accounting_pro';

const HOSTED_PLAN_COPY: Record<HostedPlanKey, {
  name: string;
  description: string;
  monthly: number | null;
  annual: number | null;
}> = {
  invoicing_free: {
    name: 'Invoicing Free',
    description: 'Core invoicing and bills with free-tier limits — no full ledger.',
    monthly: null,
    annual: null,
  },
  invoicing_pro: {
    name: 'Invoicing Pro',
    description: 'Unlimited invoices and bills for solo operators — without full double-entry accounting.',
    monthly: 15,
    annual: 150,
  },
  accounting_free: {
    name: 'Accounting Free',
    description: 'Full ledger modules with free-tier limits.',
    monthly: null,
    annual: null,
  },
  accounting_pro: {
    name: 'Accounting Pro',
    description: 'Full ledger, reports, multi-seat access, and hosted security for growing teams.',
    monthly: 99,
    annual: 990,
  },
};

function cancelLandingPlan(planKey: string): HostedPlanKey {
  const key = normalizePlanKey(planKey);
  return key.startsWith('invoicing') ? 'invoicing_free' : 'accounting_free';
}

/** Mirrors Core Company.IsAllowedSelfServicePlanChange. */
function isAllowedSelfServicePlanChange(fromPlan: string, toPlan: string): boolean {
  const from = normalizePlanKey(fromPlan);
  const to = normalizePlanKey(toPlan);
  if (!to || from === to) return false;
  const allowed: Record<string, string[]> = {
    accounting_free: ['accounting_pro', 'invoicing_free'],
    invoicing_free: ['invoicing_pro', 'accounting_free'],
    invoicing_pro: ['accounting_pro'],
  };
  return (allowed[from] || []).includes(to);
}

type PlanCardRole = 'current' | 'landing' | 'upgrade' | 'switch';

function planCardsFor(
  currentPlan: string,
  allowedTransitions: string[],
): Array<{ key: HostedPlanKey; role: PlanCardRole }> {
  const current = normalizePlanKey(currentPlan) as HostedPlanKey;
  let cards: Array<{ key: HostedPlanKey; role: PlanCardRole }>;
  switch (current) {
    case 'accounting_pro':
      cards = [
        { key: 'accounting_pro', role: 'current' },
        { key: 'accounting_free', role: 'landing' },
      ];
      break;
    case 'invoicing_pro':
      cards = [
        { key: 'invoicing_pro', role: 'current' },
        { key: 'invoicing_free', role: 'landing' },
        { key: 'accounting_pro', role: 'upgrade' },
      ];
      break;
    case 'accounting_free':
      cards = [
        { key: 'accounting_free', role: 'current' },
        { key: 'accounting_pro', role: 'upgrade' },
        { key: 'invoicing_free', role: 'switch' },
      ];
      break;
    case 'invoicing_free':
      cards = [
        { key: 'invoicing_free', role: 'current' },
        { key: 'invoicing_pro', role: 'upgrade' },
        { key: 'accounting_free', role: 'switch' },
      ];
      break;
    default:
      cards = isAllowedSelfServicePlanChange(current, 'accounting_pro')
        ? [{ key: current as HostedPlanKey, role: 'current' }]
        : [{ key: 'accounting_free', role: 'current' }, { key: 'accounting_pro', role: 'upgrade' }];
  }
  // Engine allowedTransitions is authoritative for upgrade/switch; current + cancel landing stay visible.
  if (allowedTransitions.length === 0) {
    return cards.filter((card) => card.role === 'current' || card.role === 'landing');
  }
  return cards.filter(
    (card) => card.role === 'current' || card.role === 'landing' || allowedTransitions.includes(card.key),
  );
}

function actionLabel(role: PlanCardRole): string {
  if (role === 'upgrade') return 'Upgrade';
  if (role === 'switch') return 'Switch';
  return '';
}

interface SubscriptionSettingsTabProps {
  tenantContext: TenantContextType;
}

/** First subscription tier that includes the module (Invoicing → Accounting → Enterprise). */
export type ModuleTier = 'invoicing' | 'accounting' | 'enterprise';

const TIER_LABEL: Record<ModuleTier, string> = {
  invoicing: 'Invoicing',
  accounting: 'Accounting',
  enterprise: 'Enterprise',
};

const MODULES_CATALOG: Array<{
  key: ModuleKey;
  name: string;
  description: string;
  icon: React.ComponentType<{ className?: string }>;
  /** Lowest plan that includes this module. */
  minPlan: ModuleTier;
  isCore?: boolean;
}> = [
  {
    key: 'sales',
    name: 'Invoicing & Sales',
    description: 'Customers, quotations, sales orders, invoices, and payment receipts.',
    icon: Receipt,
    minPlan: 'invoicing',
    isCore: true,
  },
  {
    key: 'financials',
    name: 'General Ledger & Banking',
    description: 'Chart of accounts, cash & bank accounts, journal entries, and trial balance.',
    icon: BookOpen,
    minPlan: 'accounting',
  },
  {
    key: 'purchases',
    name: 'Purchases & Bills',
    description: 'Vendors, purchase orders, vendor invoices, and bill payments.',
    icon: ShoppingBag,
    minPlan: 'invoicing',
  },
  {
    key: 'reports',
    name: 'Financial Reports',
    description: 'Income statements, balance sheets, customer/vendor aging, and tax summaries.',
    icon: BarChart3,
    minPlan: 'accounting',
  },
  {
    key: 'inventory',
    name: 'Inventory & Items',
    description: 'Track stock on hand and its cost: bills add stock, invoices use it up, adjustments record counts and write-offs.',
    icon: Boxes,
    minPlan: 'enterprise',
  },
  {
    key: 'donations',
    name: 'Donations & Non-Profit',
    description: 'Donor management, donation invoices, pledges, and tax receipts.',
    icon: HeartHandshake,
    minPlan: 'enterprise',
  },
  {
    key: 'audit',
    name: 'Audit Trail & Compliance',
    description: 'Comprehensive mutation auditing, field-level diffs, and security events.',
    icon: ShieldCheck,
    minPlan: 'enterprise',
  },
];

/** Display-only extras; names, descriptions, modules and prices come from the server catalog. */
const PLAN_BADGES: Partial<Record<PlanKey, string>> = {
  accounting: 'Most Popular',
};

export const SubscriptionSettingsTab: React.FC<SubscriptionSettingsTabProps> = ({
  tenantContext,
}) => {
  const { subscription, updateSubscription } = tenantContext;
  const catalog = usePlanCatalog();
  const {
    entitlements,
    reload: reloadEntitlements,
    allowedTransitions,
    startBillingReturnPoll,
    version: entitlementsVersion,
  } = useCapabilities();
  const billing = useBilling();
  const currentPlan = (billing.plan || entitlements?.effectivePlan || entitlements?.plan || subscription?.planId || 'accounting') as PlanKey;
  const effectiveTier = entitlements?.tier || billing.billing?.tier;
  const isProTier = effectiveTier === 'pro' || effectiveTier === 'enterprise';
  const tierBadge = billing.billingEnabled
    ? (billing.status === 'past_due'
      ? 'Past due'
      : billing.cancelAtPeriodEnd
        ? 'Canceling'
        : (effectiveTier === 'comp' ? 'Managed' : isProTier ? 'Pro' : 'Free'))
    : !entitlements?.enabled
      ? 'Active'
      : entitlements.tier === 'comp'
        ? 'Managed'
        : entitlements.tier === 'free' ? 'Free' : entitlements.tier;
  // Only self-service plans can be picked; an operator-assigned current plan stays visible.
  const visiblePlans = (catalog.plans || []).filter((plan) => plan.selfService || plan.key === currentPlan);
  const planModules = catalog.includedModules(currentPlan);
  // Plan-required cores always show as on; the server enforces the same rule.
  const activeModules = planModules.filter(
    (m) => catalog.isRequired(currentPlan, m) || (subscription?.activeModules || []).includes(m),
  );

  const [saving, setSaving] = useState(false);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [billingInterval, setBillingInterval] = useState<'month' | 'year'>('month');
  const [checkoutProcessing, setCheckoutProcessing] = useState(false);
  const planBeforeCheckoutRef = React.useRef<string | null>(null);
  const versionBeforeCheckoutRef = React.useRef<string | null>(null);

  // After Checkout/Portal return: poll capabilities (and billing state) until the webhook lands —
  // every 5s for up to 2 min, stopping as soon as version or effectivePlan changes.
  useEffect(() => {
    const checkout = readCheckoutParam();
    if (checkout !== 'success' || !billing.billingEnabled) return;

    setCheckoutProcessing(true);
    planBeforeCheckoutRef.current = String(currentPlan);
    versionBeforeCheckoutRef.current = entitlementsVersion;
    startBillingReturnPoll();
    void billing.reload();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- poll once when checkout=success is present
  }, [billing.billingEnabled]);

  useEffect(() => {
    if (!checkoutProcessing) return;
    const planBefore = planBeforeCheckoutRef.current;
    const versionBefore = versionBeforeCheckoutRef.current;
    const planNow = String(entitlements?.effectivePlan ?? entitlements?.plan ?? billing.plan ?? '');
    const versionChanged = entitlementsVersion != null && entitlementsVersion !== versionBefore;
    const planChanged = planBefore != null && planNow !== planBefore;
    if (!versionChanged && !planChanged) return;
    setCheckoutProcessing(false);
    clearCheckoutParam();
    void billing.reload();
    if (planChanged || versionChanged) {
      setSuccessMessage('Payment received — your plan is now active.');
      setTimeout(() => setSuccessMessage(null), 4000);
    }
  }, [checkoutProcessing, entitlementsVersion, entitlements?.effectivePlan, entitlements?.plan, billing.plan, billing]);

  const handleToggleModule = async (moduleKey: ModuleKey) => {
    if (catalog.isRequired(currentPlan, moduleKey)) return;
    if (!catalog.isIncluded(currentPlan, moduleKey)) return;
    if (!updateSubscription) return;

    setSaving(true);
    setErrorMessage(null);
    const newModules = activeModules.includes(moduleKey)
      ? activeModules.filter((m) => m !== moduleKey)
      : [...activeModules, moduleKey];

    try {
      await updateSubscription(currentPlan, newModules);
      setSuccessMessage(`Module "${moduleKey}" updated successfully.`);
      setTimeout(() => setSuccessMessage(null), 2500);
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to update modules.');
    } finally {
      setSaving(false);
    }
  };

  const handleSelectPlan = async (planKey: PlanKey) => {
    if (!updateSubscription) return;
    setSaving(true);
    setErrorMessage(null);

    try {
      // No module list: the server resets to that plan's included modules.
      await updateSubscription(planKey);
      void reloadEntitlements();
      setSuccessMessage(`Switched to the ${planKey.toUpperCase()} plan.`);
      setTimeout(() => setSuccessMessage(null), 2500);
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to switch plan.');
    } finally {
      setSaving(false);
    }
  };

  const handlePlanChange = async (planKey: HostedPlanKey) => {
    setSaving(true);
    setErrorMessage(null);
    try {
      await billing.upgradePlan(planKey, billingInterval);
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to change plan.');
      setSaving(false);
    }
  };

  const handleCancelSubscription = async () => {
    setSaving(true);
    setErrorMessage(null);
    try {
      await billing.openPortal();
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to open billing portal to cancel.');
      setSaving(false);
    }
  };

  const handleManageBilling = async () => {
    setSaving(true);
    setErrorMessage(null);
    try {
      await billing.openPortal();
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to open billing portal.');
      setSaving(false);
    }
  };

  const periodEndLabel = billing.currentPeriodEnd
    ? new Date(billing.currentPeriodEnd).toLocaleDateString(undefined, {
        year: 'numeric', month: 'short', day: 'numeric',
      })
    : null;

  return (
    <div className="space-y-6">
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
                  {tierBadge}
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
                <strong>{activeModules.length}</strong> of {planModules.length} plan modules active
              </span>
            </div>
          </div>
        </div>

        {(entitlements?.limited || (billing.billingEnabled && effectiveTier === 'free')) && entitlements && (
          <div className="pt-2 space-y-2" aria-label="Plan usage">
            <h3 className="text-xs font-bold uppercase tracking-wider text-[var(--text-muted)]">Plan usage</h3>
            {(['salesDocuments', 'bills', 'seats', 'bankAccounts'] as const).map((resource) => (
              <UsageHint key={resource} entitlements={entitlements} resource={resource} />
            ))}
            {!billing.billingEnabled && (
              <p className="text-xs text-[var(--text-muted)]">
                Paid plans are coming soon.
                {entitlements.upgrade.contact && (
                  <> Need more now? <a className="font-semibold text-[var(--brand-primary)] hover:underline" href={`mailto:${entitlements.upgrade.contact}`}>Contact support</a>.</>
                )}
              </p>
            )}
          </div>
        )}

        {(billing.billingEnabled || billing.portalAvailable || periodEndLabel) && (
          <div className="flex flex-wrap items-center gap-3 pt-1 text-xs text-[var(--text-secondary)]">
            {periodEndLabel && (
              <span>
                {billing.cancelAtPeriodEnd ? 'Access until' : 'Renews'}{' '}
                <strong className="text-[var(--text-main)]">{periodEndLabel}</strong>
              </span>
            )}
            {billing.portalAvailable && (
              <button
                type="button"
                disabled={saving}
                onClick={() => void handleManageBilling()}
                className="inline-flex items-center gap-1.5 h-8 px-3 rounded-lg bg-[var(--bg-subtle)] hover:bg-[var(--bg-surface-hover)] text-[var(--text-main)] font-semibold transition-colors"
              >
                <CreditCard className="w-3.5 h-3.5" />
                Manage billing
              </button>
            )}
          </div>
        )}

        {checkoutProcessing && (
          <div className="p-3 rounded-xl bg-[var(--brand-subtle)] text-[var(--brand-primary)] text-xs font-semibold flex items-center gap-2">
            <Loader2 className="w-4 h-4 animate-spin" />
            <span>Processing payment… confirming your plan.</span>
          </div>
        )}

        {successMessage && (
          <div className="p-3 rounded-xl bg-emerald-500/10 text-emerald-400 text-xs font-semibold flex items-center gap-2 animate-fade-in">
            <Check className="w-4 h-4" />
            <span>{successMessage}</span>
          </div>
        )}
        {errorMessage && (
          <div role="alert" className="p-3 rounded-xl bg-red-500/10 text-red-400 text-xs font-semibold">
            {errorMessage}
          </div>
        )}
      </div>

      {billing.billingEnabled && (() => {
        const cards = planCardsFor(String(currentPlan), allowedTransitions);
        const showInterval = cards.some((c) => c.role === 'upgrade');
        const landingKey = cancelLandingPlan(String(currentPlan));
        return (
          <div className="p-6 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md space-y-4">
            <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
              <div>
                <h3 className="text-base font-bold text-[var(--text-main)]">Change plan</h3>
                <p className="text-xs text-[var(--text-muted)] mt-0.5">
                  One plan per workspace. Upgrade along the path, switch free products anytime, or cancel a Pro
                  subscription (access continues until the period ends, then you land on{' '}
                  {HOSTED_PLAN_COPY[landingKey].name}).
                </p>
              </div>
              {showInterval && (
                <div className="inline-flex rounded-xl bg-[var(--bg-subtle)] p-1" role="group" aria-label="Billing interval">
                  <button
                    type="button"
                    onClick={() => setBillingInterval('month')}
                    className={`h-8 px-3 rounded-lg text-xs font-bold transition-colors ${
                      billingInterval === 'month' ? 'bg-[var(--brand-primary)] text-white' : 'text-[var(--text-secondary)]'
                    }`}
                  >
                    Monthly
                  </button>
                  <button
                    type="button"
                    onClick={() => setBillingInterval('year')}
                    className={`h-8 px-3 rounded-lg text-xs font-bold transition-colors ${
                      billingInterval === 'year' ? 'bg-[var(--brand-primary)] text-white' : 'text-[var(--text-secondary)]'
                    }`}
                  >
                    Annual
                  </button>
                </div>
              )}
            </div>

            <div className={`grid grid-cols-1 ${cards.length >= 3 ? 'md:grid-cols-3' : 'md:grid-cols-2'} gap-4`}>
              {cards.map(({ key, role }) => {
                const plan = HOSTED_PLAN_COPY[key];
                const isPaid = plan.monthly != null;
                const price = isPaid
                  ? (billingInterval === 'year' ? plan.annual : plan.monthly)
                  : null;
                const priceLabel = isPaid
                  ? (billingInterval === 'year' ? `$${price}/yr` : `$${price}/mo`)
                  : 'Free';
                const isCurrent = role === 'current';
                const isLanding = role === 'landing';
                const landingCopy = isLanding
                  ? `You'll land here after cancel — access stays on your Pro plan until ${periodEndLabel || 'the period ends'}.`
                  : plan.description;

                return (
                  <div
                    key={key}
                    className={`p-5 rounded-2xl flex flex-col justify-between transition-all ${
                      isCurrent
                        ? 'bg-[var(--brand-subtle)] border-2 border-[var(--brand-primary)] shadow-lg'
                        : isLanding
                          ? 'bg-[var(--bg-subtle)]/60 border border-dashed border-[var(--border-subtle,rgba(255,255,255,0.12))]'
                          : 'bg-[var(--bg-subtle)] border border-transparent'
                    }`}
                  >
                    <div>
                      <div className="flex items-baseline justify-between mb-2 gap-2">
                        <h4 className="text-sm font-bold text-[var(--text-main)]">{plan.name}</h4>
                        <span className="text-sm font-bold text-[var(--text-main)]">{priceLabel}</span>
                      </div>
                      <p className="text-xs text-[var(--text-secondary)] leading-relaxed mb-4">{landingCopy}</p>
                    </div>

                    {isCurrent ? (
                      <div className="space-y-2">
                        <div className="w-full h-10 rounded-xl text-xs font-bold flex items-center justify-center gap-1.5 bg-[var(--brand-primary)] text-white">
                          <Check className="w-3.5 h-3.5" />
                          Current plan
                        </div>
                        {isProTier && (
                          <button
                            type="button"
                            disabled={saving || checkoutProcessing || !billing.portalAvailable}
                            onClick={() => void handleCancelSubscription()}
                            title={!billing.portalAvailable ? 'Billing portal is not available yet for this workspace.' : undefined}
                            className="w-full h-10 rounded-xl text-xs font-bold transition-all bg-[var(--bg-surface)] hover:bg-red-500/10 text-[var(--text-secondary)] hover:text-red-400 border border-transparent hover:border-red-500/30 disabled:opacity-40 disabled:cursor-not-allowed"
                          >
                            {billing.cancelAtPeriodEnd ? 'Cancellation scheduled' : 'Cancel subscription'}
                          </button>
                        )}
                      </div>
                    ) : isLanding ? (
                      <div className="w-full h-10 rounded-xl text-xs font-semibold flex items-center justify-center text-[var(--text-muted)] bg-transparent">
                        After cancel
                      </div>
                    ) : (
                      <button
                        type="button"
                        disabled={saving || checkoutProcessing}
                        onClick={() => void handlePlanChange(key)}
                        className="w-full h-10 rounded-xl text-xs font-bold transition-all flex items-center justify-center gap-1.5 bg-[var(--bg-surface)] hover:bg-[var(--bg-surface-hover)] text-[var(--text-main)] hover:text-[var(--brand-primary)]"
                      >
                        <span>{actionLabel(role)}</span>
                        <ArrowRight className="w-3.5 h-3.5" />
                      </button>
                    )}
                  </div>
                );
              })}
            </div>
          </div>
        );
      })()}

      <div className="p-6 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md space-y-4">
        <div>
          <h3 className="text-base font-bold text-[var(--text-main)]">Subscribed Modules &amp; Add-ons</h3>
          <p className="text-xs text-[var(--text-muted)] mt-0.5">
            Modules are listed by plan tier. Only modules included in your current plan can be toggled;
            others stay locked until you upgrade.
          </p>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-2 gap-3 pt-2">
          {MODULES_CATALOG.map((mod) => {
            const Icon = mod.icon;
            const inPlan = catalog.isIncluded(currentPlan, mod.key);
            const locked = !inPlan;
            const required = catalog.isRequired(currentPlan, mod.key);
            const isEnabled = inPlan && (activeModules.includes(mod.key) || required);
            const toggleDisabled = locked || mod.isCore || required || saving;

            return (
              <div
                key={mod.key}
                className={`p-4 rounded-xl transition-all flex items-start justify-between gap-3 ${
                  locked
                    ? 'bg-[var(--bg-subtle)]/40 opacity-60'
                    : isEnabled
                      ? 'bg-[var(--bg-subtle)] border border-[var(--brand-ring)]/20 shadow-sm'
                      : 'bg-[var(--bg-subtle)]/50'
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
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="text-xs font-bold text-[var(--text-main)] truncate">
                        {mod.name}
                      </span>
                      {(mod.isCore || required) && (
                        <span className="text-[9px] px-1.5 py-0.5 rounded bg-[var(--brand-subtle)] text-[var(--brand-primary)] font-mono font-bold uppercase">
                          Core
                        </span>
                      )}
                      <span className="text-[9px] px-1.5 py-0.5 rounded bg-[var(--bg-surface-hover)] text-[var(--text-muted)] font-mono font-bold uppercase">
                        {TIER_LABEL[mod.minPlan]}+
                      </span>
                      {locked && (
                        <span className="inline-flex items-center gap-1 text-[9px] px-1.5 py-0.5 rounded bg-amber-500/10 text-amber-500 font-mono font-bold uppercase">
                          <Lock className="w-2.5 h-2.5" aria-hidden="true" />
                          Upgrade
                        </span>
                      )}
                    </div>
                    <p className="text-[11px] text-[var(--text-muted)] mt-0.5 line-clamp-2 leading-relaxed">
                      {mod.description}
                    </p>
                  </div>
                </div>

                <button
                  type="button"
                  role="switch"
                  aria-checked={isEnabled}
                  aria-disabled={toggleDisabled}
                  aria-label={
                    locked
                      ? `${mod.name} requires the ${TIER_LABEL[mod.minPlan]} plan or higher`
                      : mod.isCore || required
                        ? `${mod.name} is always on`
                        : `Toggle ${mod.name} module`
                  }
                  disabled={toggleDisabled}
                  onClick={() => handleToggleModule(mod.key)}
                  className={`relative inline-flex h-6 w-11 shrink-0 rounded-full transition-colors duration-200 ease-in-out focus:outline-none focus-visible:ring-2 focus-visible:ring-[var(--brand-ring)] ${
                    toggleDisabled ? 'opacity-40 cursor-not-allowed' : 'cursor-pointer'
                  } ${isEnabled ? 'bg-[var(--brand-primary)]' : 'bg-slate-700'}`}
                  title={
                    locked
                      ? `Included from ${TIER_LABEL[mod.minPlan]} plan — upgrade to enable`
                      : mod.isCore || required
                        ? 'Core feature cannot be disabled'
                        : `Toggle ${mod.name}`
                  }
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

      {!billing.billingEnabled && (
        <div className="p-6 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md space-y-4">
          <div>
            <h3 className="text-base font-bold text-[var(--text-main)]">Compare Plans</h3>
            <p className="text-xs text-[var(--text-muted)] mt-0.5">
              Switch products anytime. Switching provisions that plan&apos;s modules; modules outside the plan stay locked.
            </p>
          </div>

          {catalog.error && (
            <div role="alert" className="p-3 rounded-xl bg-red-500/10 text-red-400 text-xs font-semibold">
              {catalog.error}
            </div>
          )}
          <div className={`grid grid-cols-1 ${visiblePlans.length >= 3 ? 'md:grid-cols-3' : 'md:grid-cols-2'} gap-4 pt-2`}>
            {visiblePlans.map((plan) => {
              const isCurrent = currentPlan === plan.key;
              const includedModules = plan.includedModules;
              const badge = PLAN_BADGES[plan.key];

              return (
                <div
                  key={plan.key}
                  className={`p-5 rounded-2xl flex flex-col justify-between transition-all relative ${
                    isCurrent
                      ? 'bg-[var(--brand-subtle)] border-2 border-[var(--brand-primary)] shadow-lg'
                      : 'bg-[var(--bg-subtle)] hover:bg-[var(--bg-surface-hover)] border border-transparent'
                  }`}
                >
                  {badge && (
                    <div className="absolute -top-2.5 right-4 px-2 py-0.5 rounded-full bg-[var(--brand-primary)] text-white text-[10px] font-bold uppercase tracking-wider shadow">
                      {badge}
                    </div>
                  )}

                  <div>
                    <div className="flex items-baseline justify-between mb-2">
                      <h4 className="text-sm font-bold text-[var(--text-main)]">{plan.name}</h4>
                      {entitlements?.limited && (
                        <span className="text-xs font-bold uppercase tracking-wider text-[var(--text-muted)]">Free</span>
                      )}
                    </div>

                    <p className="text-xs text-[var(--text-secondary)] leading-relaxed mb-4">
                      {plan.description}
                    </p>

                    <div className="space-y-1.5 mb-6 text-xs text-[var(--text-main)]">
                      <div className="font-semibold text-[10px] uppercase text-[var(--text-muted)] tracking-wider mb-1">
                        Included Modules:
                      </div>
                      {includedModules.map((m) => (
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
      )}
    </div>
  );
};
