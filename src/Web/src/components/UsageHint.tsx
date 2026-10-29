import React from 'react';
import { RESOURCE_LABELS, type Entitlements, type MeteredResource } from '../lib/entitlements';

/** "3 of 5 invoices & quotes used this month" — rendered only when the workspace's plan limits the resource. */
export const UsageHint: React.FC<{ entitlements: Entitlements | null; resource: MeteredResource }> = ({ entitlements, resource }) => {
  const usage = entitlements?.limits[resource];
  if (!entitlements?.limited || !usage || usage.limit === null) return null;

  const { limit, used } = usage;
  const percent = limit > 0 ? Math.min(100, Math.round((used / limit) * 100)) : 100;
  const reached = used >= limit;
  const label = `${used} of ${limit} ${RESOURCE_LABELS[resource]} used${usage.period === 'month' ? ' this month' : ''}`;

  return (
    <div className="flex items-center gap-3 text-xs text-[var(--text-secondary)]" data-testid={`usage-${resource}`}>
      <div
        role="progressbar"
        aria-label={label}
        aria-valuemin={0}
        aria-valuemax={limit}
        aria-valuenow={Math.min(used, limit)}
        className="h-1.5 w-28 rounded-full overflow-hidden"
        // Track tinted from the text colour so it shows on light and dark surfaces alike.
        style={{ background: 'color-mix(in srgb, var(--text-muted) 30%, transparent)' }}
      >
        <div
          className={`h-full rounded-full ${reached ? 'bg-amber-500' : 'bg-[var(--brand-primary)]'}`}
          style={{ width: `${percent}%` }}
        />
      </div>
      <span>
        {label}
        {reached ? ' · Free plan limit reached' : ''}
      </span>
    </div>
  );
};

export default UsageHint;
