import React from 'react';
import { AlertTriangle } from 'lucide-react';
import { useBilling } from '../lib/useBilling';

/** Persistent warning when the workspace subscription is past_due (within grace). */
export const PastDueBanner: React.FC = () => {
  const { billingEnabled, isPastDue, portalAvailable, openPortal } = useBilling();

  if (!billingEnabled || !isPastDue) return null;

  return (
    <div
      role="status"
      className="mb-4 flex flex-col sm:flex-row sm:items-center justify-between gap-3 rounded-xl border border-amber-500/30 bg-amber-500/10 px-4 py-3 text-amber-700 dark:text-amber-300"
    >
      <div className="flex items-start gap-2.5 min-w-0">
        <AlertTriangle className="w-5 h-5 shrink-0 mt-0.5" aria-hidden="true" />
        <div className="min-w-0">
          <p className="text-sm font-semibold">Payment past due</p>
          <p className="text-xs opacity-90 mt-0.5">
            Update your payment method to keep paid features. Access continues during a short grace period.
          </p>
        </div>
      </div>
      {portalAvailable && (
        <button
          type="button"
          onClick={() => void openPortal().catch(() => undefined)}
          className="shrink-0 h-9 px-3 rounded-lg bg-amber-600 text-white text-xs font-bold hover:bg-amber-500 transition-colors"
        >
          Manage billing
        </button>
      )}
    </div>
  );
};

export default PastDueBanner;
