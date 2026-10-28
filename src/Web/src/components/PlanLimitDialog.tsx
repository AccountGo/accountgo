import React, { useEffect, useRef, useState } from 'react';
import { Sparkles } from 'lucide-react';
import { getOrgSlugFromLocation } from '../auth/AuthContext';
import { ENTITLEMENT_DENIED_EVENT, type EntitlementCode, type EntitlementProblem } from '../lib/entitlementEvents';
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
  return <PlanLimitDialogView problem={problem} onClose={() => setProblem(null)} />;
};

export const PlanLimitDialogView: React.FC<{ problem: EntitlementProblem; onClose: () => void }> = ({ problem, onClose }) => {
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
