import React, { useEffect, useRef, useState } from 'react';
import { Sparkles } from 'lucide-react';
import { getOrgSlugFromLocation } from '../auth/AuthContext';
import { ENTITLEMENT_DENIED_EVENT, type EntitlementProblem } from '../lib/entitlementEvents';

const TITLES: Record<EntitlementProblem['code'], string> = {
  QUOTA_EXCEEDED: 'Free plan limit reached',
  MODULE_NOT_SUBSCRIBED: 'Not included in your plan',
  PLAN_NOT_AVAILABLE: 'Plan not available',
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
  const dialogRef = useRef<HTMLDivElement>(null);
  const slug = typeof window === 'undefined' ? null : getOrgSlugFromLocation();

  useEffect(() => {
    const trigger = document.activeElement as HTMLElement | null;
    closeRef.current?.focus();
    return () => trigger?.focus();
  }, []);

  const handleKeyDown = (event: React.KeyboardEvent) => {
    if (event.key === 'Escape') {
      event.preventDefault();
      onClose();
      return;
    }
    if (event.key !== 'Tab') return;
    const focusable = dialogRef.current?.querySelectorAll<HTMLElement>('a[href], button:not(:disabled)');
    if (!focusable?.length) return;
    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  };

  const usage = problem.code === 'QUOTA_EXCEEDED' && typeof problem.limit === 'number'
    ? `${problem.used ?? problem.limit} of ${problem.limit} used${problem.period === 'month' ? ' this month' : ''}.`
    : null;

  return (
    <div className="fixed inset-0 z-[60] flex items-center justify-center bg-black/60 p-4">
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="plan-limit-title"
        aria-describedby="plan-limit-detail"
        onKeyDown={handleKeyDown}
        className="w-full max-w-md rounded-2xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] p-6 shadow-2xl space-y-4"
      >
        <div className="flex items-center gap-3">
          <div className="w-10 h-10 rounded-xl bg-[var(--brand-subtle)] text-[var(--brand-primary)] flex items-center justify-center shrink-0">
            <Sparkles className="w-5 h-5" aria-hidden="true" />
          </div>
          <h2 id="plan-limit-title" className="text-base font-bold text-[var(--text-main)]">{TITLES[problem.code]}</h2>
        </div>
        <div id="plan-limit-detail" className="space-y-2 text-sm text-[var(--text-secondary)]">
          {problem.detail && <p>{problem.detail}</p>}
          {usage && <p>{usage}</p>}
          <p>
            Paid plans are coming soon.
            {problem.contact ? (
              <> Need more now? <a className="font-semibold text-[var(--brand-primary)] hover:underline" href={`mailto:${problem.contact}`}>Contact support</a>.</>
            ) : null}
          </p>
        </div>
        <div className="flex justify-end gap-2">
          {slug && (
            <a
              href={`/org/${encodeURIComponent(slug)}/settings?tab=subscription`}
              className="h-10 px-4 rounded-xl text-xs font-semibold text-[var(--text-secondary)] hover:bg-[var(--bg-surface-hover)] inline-flex items-center"
            >
              View plan usage
            </a>
          )}
          <button
            type="button"
            ref={closeRef}
            onClick={onClose}
            className="h-10 px-4 rounded-xl text-xs font-semibold text-white bg-[var(--brand-primary)]"
          >
            OK
          </button>
        </div>
      </div>
    </div>
  );
};

export default PlanLimitDialog;
