import React from 'react';
import { Link } from 'react-router-dom';
import type { Plan } from '../lib/policyDefaults';
import { usePlanCatalog } from '../../../lib/usePlanCatalog';
import {
  buildIntentPlanOptions,
  freeSlotBlockedMessage,
  type FreeSlotState,
} from '../../../lib/createOrgPlanOptions';
import { ChoiceCards, type ChoiceOption } from './ui';

const EMPTY_FREE_SLOT: FreeSlotState = {
  used: false,
  freeWorkspaceName: null,
  freeWorkspaceSlug: null,
};

export const IntentStep: React.FC<{
  plan: Plan;
  onChange: (plan: Plan) => void;
  freeSlot?: FreeSlotState;
  billingInterval?: 'month' | 'year';
  onBillingIntervalChange?: (interval: 'month' | 'year') => void;
}> = ({
  plan,
  onChange,
  freeSlot = EMPTY_FREE_SLOT,
  billingInterval = 'month',
  onBillingIntervalChange,
}) => {
  const { plans, loading } = usePlanCatalog();
  const built = buildIntentPlanOptions(plans, freeSlot);
  const options: ChoiceOption<string>[] = built.map((opt) => ({
    value: opt.value,
    title: opt.title,
    description: opt.description,
    badge: opt.badge,
    disabled: opt.disabled,
  }));
  const selectedPaid = built.find((o) => o.value === plan && !o.free);

  if (loading || !plans) {
    return (
      <div className="space-y-2.5" aria-busy="true" aria-label="Loading plans">
        {[0, 1, 2].map((i) => (
          <div key={i} className="h-20 rounded-xl bg-[var(--bg-subtle)] animate-pulse" />
        ))}
      </div>
    );
  }

  return (
    <div className="space-y-4">
      {freeSlot.used && (
        <div role="status" className="p-3 rounded-xl bg-amber-500/10 text-amber-800 dark:text-amber-200 text-xs font-medium ring-1 ring-amber-500/20 space-y-1">
          <p>{freeSlotBlockedMessage(freeSlot.freeWorkspaceName, !!freeSlot.freeWorkspaceSlug)}</p>
          {freeSlot.freeWorkspaceSlug && (
            <p>
              <Link
                className="font-semibold underline underline-offset-2"
                to={`/org/${encodeURIComponent(freeSlot.freeWorkspaceSlug)}/settings?tab=subscription`}
              >
                Upgrade {freeSlot.freeWorkspaceName || freeSlot.freeWorkspaceSlug}
              </Link>
              {' '}to free up your free slot.
            </p>
          )}
        </div>
      )}

      <ChoiceCards<string>
        legend="What do you need?"
        name="plan"
        // A disabled (free) choice must not look selected.
        value={built.some((o) => o.value === plan && !o.disabled) ? plan : ''}
        onChange={onChange}
        columns={options.length >= 3 ? 'sm:grid-cols-3' : 'sm:grid-cols-2'}
        options={options}
      />

      {selectedPaid && (
        <>
          <p className="text-[11px] text-[var(--text-muted)]">
            You&apos;ll pay on the next screen. Nothing is charged until you confirm.
          </p>
          <div className="inline-flex rounded-xl bg-[var(--bg-subtle)] p-1" role="group" aria-label="Billing interval">
            {(['month', 'year'] as const).map((value) => (
              <button
                key={value}
                type="button"
                aria-pressed={billingInterval === value}
                onClick={() => onBillingIntervalChange?.(value)}
                className={`h-8 px-3 rounded-lg text-xs font-semibold transition-colors ${
                  billingInterval === value
                    ? 'bg-[var(--bg-surface)] text-[var(--text-main)] shadow-sm'
                    : 'text-[var(--text-muted)] hover:text-[var(--text-main)]'
                }`}
              >
                {value === 'month' ? 'Monthly' : 'Annual'}
              </button>
            ))}
          </div>
        </>
      )}
    </div>
  );
};
