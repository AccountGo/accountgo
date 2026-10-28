import React, { useEffect, useRef, useState } from 'react';
import { Sparkles } from 'lucide-react';
import { getOrgSlugFromLocation } from '../auth/AuthContext';
import { ENTITLEMENT_DENIED_EVENT, type EntitlementProblem } from '../lib/entitlementEvents';
import { ModalShell } from './ModalShell';

const TITLES: Record<EntitlementProblem['code'], string> = {
  QUOTA_EXCEEDED: 'Free plan limit reached',
  MODULE_NOT_SUBSCRIBED: 'Not included in your plan',
  PLAN_NOT_AVAILABLE: 'Plan not available',
  SUBSCRIPTION_INACTIVE: 'Subscription inactive',
  FEATURE_NOT_ENTITLED: 'Feature not included in your plan',
};

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
  const isInactive = problem.code === 'SUBSCRIPTION_INACTIVE';
  const subscriptionHref = slug
    ? `/org/${encodeURIComponent(slug)}/settings?tab=subscription`
    : null;

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
        {isInactive ? (
          <p>Update billing to restore paid modules and features.</p>
        ) : (
          <p>
            Paid plans are coming soon.
            {problem.contact ? (
              <>
                {' '}
                Need more now?{' '}
                <a className="font-semibold text-[var(--brand-primary)] hover:underline" href={`mailto:${problem.contact}`}>
                  Contact support
                </a>
                .
              </>
            ) : null}
          </p>
        )}
      </div>
      <div className="flex justify-end gap-2">
        {subscriptionHref && (
          <a
            href={subscriptionHref}
            className={
              isInactive
                ? 'h-10 px-4 rounded-xl text-xs font-semibold text-white bg-[var(--brand-primary)] inline-flex items-center'
                : 'h-10 px-4 rounded-xl text-xs font-semibold text-[var(--text-secondary)] hover:bg-[var(--bg-surface-hover)] inline-flex items-center'
            }
          >
            {isInactive ? 'Update billing' : 'View plan usage'}
          </a>
        )}
        <button
          type="button"
          ref={closeRef}
          onClick={onClose}
          className={
            isInactive
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
