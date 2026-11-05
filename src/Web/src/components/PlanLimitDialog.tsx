import React, { useEffect, useRef, useState } from 'react';
import { Sparkles } from 'lucide-react';
import { getOrgSlugFromLocation, useAuth } from '../auth/AuthContext';
import { ENTITLEMENT_DENIED_EVENT, type EntitlementCode, type EntitlementProblem } from '../lib/entitlementEvents';
import { usePlanCatalog } from '../lib/usePlanCatalog';
import { useBilling } from '../lib/useBilling';
import { eligibleUpgradePlans, planPriceLabel } from '../lib/planCards';
import { ModalShell } from './ModalShell';

const TITLES: Record<EntitlementCode, string> = {
  QUOTA_EXCEEDED: 'Free plan limit reached',
  MODULE_NOT_SUBSCRIBED: 'Not included in your plan',
  PLAN_NOT_AVAILABLE: 'Plan not available',
  SUBSCRIPTION_INACTIVE: 'Subscription inactive',
  FEATURE_NOT_ENTITLED: 'Feature not included in your plan',
};

export type PlanLimitCta = {
  label: string;
  href: string | null;
  primary: boolean;
};

/**
 * CTA matrix from the problem body only (code × canManageBilling × upgradeAvailable).
 * Ignores the entitlements payload's hard-coded upgrade.available.
 */
export function planLimitCta(problem: EntitlementProblem, subscriptionHref: string | null): PlanLimitCta {
  const canManage = !!problem.canManageBilling;
  const upgradeAvailable = !!problem.upgradeAvailable;

  if (problem.resource === 'owned_workspaces') {
    return { label: 'Manage workspaces', href: '/select-org', primary: true };
  }

  if (!canManage) {
    return { label: 'Ask your workspace owner', href: null, primary: true };
  }

  if (problem.code === 'SUBSCRIPTION_INACTIVE') {
    return { label: 'Update billing', href: subscriptionHref, primary: true };
  }

  if (upgradeAvailable) {
    return { label: 'Upgrade plan', href: subscriptionHref, primary: true };
  }

  return { label: 'View plan usage', href: subscriptionHref, primary: false };
}

export function planLimitSupportCopy(problem: EntitlementProblem): string | null {
  if (problem.resource === 'owned_workspaces') {
    return null; // detail already names the free-slot limit; CTA links to workspace list
  }
  if (problem.code === 'SUBSCRIPTION_INACTIVE') {
    return 'Update billing to restore paid modules and features.';
  }
  if (problem.code === 'FEATURE_NOT_ENTITLED') {
    return problem.requiredFeature
      ? `The “${problem.requiredFeature}” feature is not included in your current plan.`
      : 'This feature is not included in your current plan.';
  }
  if (!problem.canManageBilling) {
    return 'Ask your workspace owner to update the subscription if you need more capacity.';
  }
  if (problem.upgradeAvailable) {
    return 'Upgrade your plan to unlock this capability.';
  }
  if (problem.contact) {
    return null; // contact link rendered separately
  }
  return 'Paid plans are coming soon.';
}

/**
 * One explanation for every plan denial (limits, modules, plans), wherever the request came from.
 * Mounted once at the app root; listens for the event raised by authFetch.
 */
export const PlanLimitDialog: React.FC = () => {
  const [problem, setProblem] = useState<EntitlementProblem | null>(null);

  useEffect(() => {
    const onDenied = (event: Event) => setProblem((event as CustomEvent<EntitlementProblem>).detail);
    window.addEventListener(ENTITLEMENT_DENIED_EVENT, onDenied);
    return () => window.removeEventListener(ENTITLEMENT_DENIED_EVENT, onDenied);
  }, []);

  if (!problem) return null;
  return <PlanLimitDialogView problem={problem} onClose={() => setProblem(null)}>
    {problem.canManageBilling && problem.upgradeAvailable && <UpgradeOptions problem={problem} />}
  </PlanLimitDialogView>;
};

const UpgradeOptions: React.FC<{ problem: EntitlementProblem }> = ({ problem }) => {
  const { authFetch } = useAuth();
  const catalog = usePlanCatalog();
  const billing = useBilling();
  const [allowed, setAllowed] = useState<string[]>([]);
  const [interval, setInterval] = useState<'month' | 'year'>('month');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let mounted = true;
    void authFetch('/api/entitlements').then(async response => {
      if (!response.ok) throw new Error('Unable to load available plans.');
      const data = await response.json();
      if (mounted) setAllowed(Array.isArray(data.allowedTransitions) ? data.allowedTransitions.filter((key: unknown): key is string => typeof key === 'string') : []);
    }).catch(() => { if (mounted) setError('Unable to load available plans.'); });
    return () => { mounted = false; };
  }, [authFetch]);

  const options = billing.billingEnabled ? eligibleUpgradePlans(catalog.plans || [], allowed, problem) : [];
  if (!options.length && !error) return null;
  const selectPlan = async (key: string) => {
    setBusy(true);
    setError(null);
    try { await billing.upgradePlan(key, interval); }
    catch (err) { setError(err instanceof Error ? err.message : 'Unable to start checkout.'); }
    finally { setBusy(false); }
  };
  return <div className="space-y-3">
    {options.length > 0 && <>
      <div className="flex items-center justify-between gap-2">
        <h3 className="text-sm font-bold text-[var(--text-main)]">Available plans</h3>
        <div role="group" aria-label="Billing interval" className="flex gap-2 text-xs">
          {(['month', 'year'] as const).map(value => <button key={value} type="button" aria-pressed={interval === value} onClick={() => setInterval(value)} className={interval === value ? 'font-bold text-[var(--brand-primary)]' : 'text-[var(--text-muted)]'}>{value === 'month' ? 'Monthly' : 'Annual'}</button>)}
        </div>
      </div>
      {options.map(plan => <div key={plan.key} className="rounded-xl bg-[var(--bg-subtle)] p-3 flex items-center justify-between gap-3">
        <div><p className="text-sm font-bold text-[var(--text-main)]">{plan.name}</p><p className="text-xs text-[var(--text-secondary)]">{plan.description}</p><p className="text-xs font-semibold text-[var(--text-main)]">{planPriceLabel(plan.monthlyPrice, interval, plan.annualPrice) ?? 'Price shown at checkout'}</p></div>
        <button type="button" disabled={busy} onClick={() => void selectPlan(plan.key)} className="shrink-0 rounded-lg bg-[var(--brand-primary)] px-3 py-2 text-xs font-bold text-white disabled:opacity-50">Continue</button>
      </div>)}
    </>}
    {error && <p role="alert" className="text-xs text-red-400">{error}</p>}
  </div>;
};

export const PlanLimitDialogView: React.FC<{ problem: EntitlementProblem; onClose: () => void; children?: React.ReactNode }> = ({ problem, onClose, children }) => {
  const closeRef = useRef<HTMLButtonElement>(null);
  const slug = typeof window === 'undefined' ? null : getOrgSlugFromLocation();
  const subscriptionHref = slug
    ? `/org/${encodeURIComponent(slug)}/settings?tab=subscription`
    : null;
  const cta = planLimitCta(problem, subscriptionHref);
  const supportCopy = planLimitSupportCopy(problem);

  useEffect(() => {
    closeRef.current?.focus();
  }, []);

  const usage = problem.code === 'QUOTA_EXCEEDED' && typeof problem.limit === 'number'
    ? `${problem.used ?? problem.limit} of ${problem.limit} used${problem.period === 'month' ? ' this month' : ''}.`
    : null;

  return (
    <ModalShell
      open
      onClose={onClose}
      maxWidthClassName="max-w-md"
      aria-labelledby="plan-limit-title"
      aria-describedby="plan-limit-detail"
      panelClassName="p-6 sm:p-6 space-y-4"
      zClassName="z-[110]"
    >
      <div className="flex items-center gap-3">
        <div className="w-10 h-10 rounded-xl bg-[var(--brand-subtle)] text-[var(--brand-primary)] flex items-center justify-center shrink-0">
          <Sparkles className="w-5 h-5" aria-hidden="true" />
        </div>
        <h2 id="plan-limit-title" className="text-base font-bold text-[var(--text-main)]">
          {TITLES[problem.code]}
        </h2>
      </div>
      <div id="plan-limit-detail" className="space-y-2 text-sm text-[var(--text-secondary)]">
        {problem.detail && <p>{problem.detail}</p>}
        {usage && <p>{usage}</p>}
        {supportCopy && <p>{supportCopy}</p>}
        {!supportCopy && problem.canManageBilling && !problem.upgradeAvailable && problem.contact ? (
          <p>
            Paid plans are coming soon. Need more now?{' '}
            <a className="font-semibold text-[var(--brand-primary)] hover:underline" href={`mailto:${problem.contact}`}>
              Contact support
            </a>
            .
          </p>
        ) : null}
      </div>
      {children}
      <div className="flex justify-end gap-2">
        {cta.href ? (
          <a
            href={cta.href}
            className={
              cta.primary
                ? 'h-10 px-4 rounded-xl text-xs font-semibold text-white bg-[var(--brand-primary)] inline-flex items-center'
                : 'h-10 px-4 rounded-xl text-xs font-semibold text-[var(--text-secondary)] hover:bg-[var(--bg-surface-hover)] inline-flex items-center'
            }
          >
            {cta.label}
          </a>
        ) : (
          <span className="h-10 px-4 rounded-xl text-xs font-semibold text-[var(--text-secondary)] inline-flex items-center">
            {cta.label}
          </span>
        )}
        <button
          type="button"
          ref={closeRef}
          onClick={onClose}
          className={
            cta.primary && cta.href
              ? 'h-10 px-4 rounded-xl text-xs font-semibold text-[var(--text-secondary)] hover:bg-[var(--bg-surface-hover)]'
              : 'h-10 px-4 rounded-xl text-xs font-semibold text-white bg-[var(--brand-primary)]'
          }
        >
          OK
        </button>
      </div>
    </ModalShell>
  );
};

export default PlanLimitDialog;
