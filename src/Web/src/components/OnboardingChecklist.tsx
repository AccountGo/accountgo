import React, { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { CheckCircle2, Circle, X } from 'lucide-react';
import { useAuth } from '../auth/AuthContext';
import { buildChecklist, ChecklistItem } from '../lib/onboardingChecklist';

interface Props {
  orgSlug: string;
  plan?: string;
  canManage: boolean;
}

/** "Get books ready" card shown on the dashboard until an admin dismisses it. */
export const OnboardingChecklist: React.FC<Props> = ({ orgSlug, plan, canManage }) => {
  const { authFetch } = useAuth();
  const [items, setItems] = useState<ChecklistItem[] | null>(null);

  useEffect(() => {
    if (!canManage) return;
    let cancelled = false;
    (async () => {
      try {
        const [policyRes, profileRes] = await Promise.all([
          authFetch('/api/organizations/current/accounting-policy'),
          authFetch('/api/organizations/current/business-profile'),
        ]);
        if (!policyRes.ok || cancelled) return;
        const policy = await policyRes.json();
        if (policy.onboardingChecklistDismissedAt) return;
        const profile = profileRes.ok ? await profileRes.json() : null;
        setItems(buildChecklist({ plan, taxRegime: policy.taxRegime, hasLegalName: Boolean(profile?.legalName) }));
      } catch (err) {
        console.error('Failed to load onboarding checklist:', err);
      }
    })();
    return () => { cancelled = true; };
  }, [authFetch, canManage, plan]);

  const dismiss = useCallback(async () => {
    setItems(null);
    try {
      await authFetch('/api/organizations/current/onboarding-checklist/dismiss', { method: 'POST' });
    } catch (err) {
      console.error('Failed to dismiss onboarding checklist:', err);
    }
  }, [authFetch]);

  if (!items) return null;

  const remaining = items.filter((i) => !i.done).length;

  return (
    <section aria-labelledby="onboarding-checklist-title" className="p-4 sm:p-5 rounded-2xl bg-[var(--bg-surface)] shadow-md">
      <div className="flex items-start justify-between gap-3 mb-3">
        <div>
          <h2 id="onboarding-checklist-title" className="text-sm font-bold text-[var(--text-main)]">Get books ready</h2>
          <p className="text-xs text-[var(--text-muted)]">{remaining === 0 ? 'All set.' : `${remaining} left — none of these block you from posting.`}</p>
        </div>
        <button
          type="button"
          onClick={dismiss}
          aria-label="Dismiss checklist"
          className="p-2 -m-2 rounded-lg text-[var(--text-muted)] hover:text-[var(--text-main)] hover:bg-[var(--bg-surface-hover)] cursor-pointer"
        >
          <X className="w-4 h-4" aria-hidden="true" />
        </button>
      </div>
      <ul className="space-y-1.5">
        {items.map((item) => (
          <li key={item.id}>
            <Link
              to={`/org/${orgSlug}/${item.to}`}
              className="flex items-start gap-3 p-2.5 rounded-xl bg-[var(--bg-subtle)] hover:bg-[var(--bg-surface-hover)] transition-colors"
            >
              {item.done
                ? <CheckCircle2 className="w-4 h-4 mt-0.5 text-[var(--brand-primary)] flex-shrink-0" aria-label="Done" />
                : <Circle className="w-4 h-4 mt-0.5 text-[var(--text-muted)] flex-shrink-0" aria-hidden="true" />}
              <span className="min-w-0">
                <span className={`block text-sm font-semibold ${item.done ? 'line-through text-[var(--text-muted)]' : 'text-[var(--text-main)]'}`}>{item.label}</span>
                <span className="block text-xs text-[var(--text-muted)]">{item.hint}</span>
              </span>
            </Link>
          </li>
        ))}
      </ul>
    </section>
  );
};
