// SPDX-License-Identifier: AGPL-3.0-only
import React, { useEffect, useRef } from 'react';
import { CreditCard } from 'lucide-react';
import type { PlanChangePreview } from '../lib/useBilling';
import { ModalShell } from './ModalShell';

export function formatMoney(amount: number, currency: string): string {
  try {
    return new Intl.NumberFormat(undefined, { style: 'currency', currency: currency.toUpperCase() }).format(amount);
  } catch {
    return `${amount.toFixed(2)} ${currency.toUpperCase()}`;
  }
}

function formatDate(value: string | null): string | null {
  if (!value) return null;
  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? null
    : date.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
}

/** Paid-to-paid changes charge the saved card; the owner sees the exact charge and confirms first. */
export const UpgradeConfirmDialog: React.FC<{
  preview: PlanChangePreview;
  busy: boolean;
  error?: string | null;
  onConfirm: () => void;
  onCancel: () => void;
}> = ({ preview, busy, error, onConfirm, onCancel }) => {
  const cancelRef = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    cancelRef.current?.focus();
  }, []);

  const now = formatMoney(preview.amountDueNow, preview.currency);
  const recurring = formatMoney(preview.recurringAmount, preview.currency);
  const next = formatDate(preview.nextBillingDate);
  const per = preview.interval === 'year' ? 'year' : 'month';

  return (
    <ModalShell
      open
      onClose={busy ? () => {} : onCancel}
      maxWidthClassName="max-w-md"
      aria-labelledby="upgrade-confirm-title"
      aria-describedby="upgrade-confirm-detail"
      panelClassName="p-6 sm:p-6 space-y-4"
      zClassName="z-[120]"
    >
      <div className="flex items-center gap-3">
        <div className="w-10 h-10 rounded-xl bg-[var(--brand-subtle)] text-[var(--brand-primary)] flex items-center justify-center shrink-0">
          <CreditCard className="w-5 h-5" aria-hidden="true" />
        </div>
        <h2 id="upgrade-confirm-title" className="text-base font-bold text-[var(--text-main)]">
          Upgrade to {preview.planName}?
        </h2>
      </div>
      <div id="upgrade-confirm-detail" className="space-y-3 text-sm text-[var(--text-secondary)]">
        <dl className="rounded-xl bg-[var(--bg-subtle)] p-4 space-y-2">
          <div className="flex items-baseline justify-between gap-3">
            <dt>Charged today</dt>
            <dd className="text-base font-bold text-[var(--text-main)]">{now}</dd>
          </div>
          <div className="flex items-baseline justify-between gap-3">
            <dt>Then</dt>
            <dd className="font-semibold text-[var(--text-main)]">{recurring} / {per}{next ? ` from ${next}` : ''}</dd>
          </div>
          {preview.paymentMethod && (
            <div className="flex items-baseline justify-between gap-3">
              <dt>Paid with</dt>
              <dd className="font-semibold text-[var(--text-main)]">{preview.paymentMethod}</dd>
            </div>
          )}
        </dl>
        <p className="text-xs">
          Today's charge covers the rest of your current billing period, less credit for the time left on your current plan.
          Your plan changes once the payment goes through.
        </p>
        {error && <p role="alert" className="text-xs text-red-400">{error}</p>}
      </div>
      <div className="flex justify-end gap-2">
        <button
          type="button"
          ref={cancelRef}
          disabled={busy}
          onClick={onCancel}
          className="h-10 px-4 rounded-xl text-xs font-semibold text-[var(--text-secondary)] hover:bg-[var(--bg-surface-hover)] disabled:opacity-50"
        >
          Cancel
        </button>
        <button
          type="button"
          disabled={busy}
          onClick={onConfirm}
          className="h-10 px-4 rounded-xl text-xs font-semibold text-white bg-[var(--brand-primary)] disabled:opacity-50"
        >
          {busy ? 'Processing…' : `Confirm and pay ${now}`}
        </button>
      </div>
    </ModalShell>
  );
};

export default UpgradeConfirmDialog;
