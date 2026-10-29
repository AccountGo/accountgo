import React, { useId } from 'react';
import { ArrowRight, Landmark, X } from 'lucide-react';
import { ModalShell } from './ModalShell';
import type { UnassignedCash } from '../types/banking';

const labelClass = 'block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1';

/**
 * One-time step when the first bank is added and past payments sit on more than one cash account.
 * The user assigns each group to this bank or leaves it unassigned.
 */
export const CashHistoryStep: React.FC<{
  groups: UnassignedCash[];
  bankName: string;
  showCodes: boolean;
  formatCurrency: (n: number) => string;
  onAssign: (accountId: number) => void;
  onSkipAll: () => void;
  onClose: () => void;
}> = ({ groups, bankName, showCodes, formatCurrency, onAssign, onSkipAll, onClose }) => {
  const titleId = useId();

  return (
    <ModalShell
      open
      onClose={onClose}
      bare
      aria-labelledby={titleId}
      panelClassName="bg-[var(--bg-surface)] rounded-2xl max-w-lg w-full max-h-[min(90vh,calc(100dvh-2rem))] overflow-y-auto border border-[var(--border-subtle)] shadow-2xl"
    >
      <div className="px-6 py-4 border-b border-[var(--border-subtle)] flex items-center justify-between">
        <div className="min-w-0">
          <div className="flex items-center gap-2">
            <Landmark className="w-5 h-5 text-[var(--brand-primary)]" />
            <h3 id={titleId} className="font-bold text-[var(--text-main)] text-base">
              Assign past cash activity
            </h3>
          </div>
          <p className="mt-0.5 text-xs text-[var(--text-secondary)]">
            Payments were posted to more than one cash account. Pick which ones belong to{' '}
            <strong>{bankName}</strong>, or leave them as unassigned cash.
          </p>
        </div>
        <button type="button" onClick={onClose} aria-label="Close" className="text-[var(--text-muted)] hover:text-[var(--text-main)] p-1 rounded-lg">
          <X className="w-5 h-5" />
        </button>
      </div>

      <div className="p-6 space-y-3">
        {groups.map((g) => (
          <div
            key={g.accountId}
            className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-subtle)] p-4 space-y-2"
          >
            <div className="flex items-start justify-between gap-3">
              <div className="min-w-0">
                <p className="text-sm font-semibold text-[var(--text-main)] truncate">
                  {showCodes && g.accountCode ? `${g.accountCode} · ` : ''}
                  {g.accountName || g.description}
                </p>
                <p className="text-xs text-[var(--text-muted)] mt-0.5">{g.description}</p>
                <p className="text-xs text-[var(--text-secondary)] mt-1">
                  Receipts {g.receiptCount} · Payments {g.paymentCount} · Other {g.otherLineCount}
                </p>
              </div>
              <span className="font-mono text-sm font-bold text-[var(--text-main)] whitespace-nowrap">
                {formatCurrency(g.balance)}
              </span>
            </div>
            <div className="flex flex-wrap gap-2 pt-1">
              <button
                type="button"
                onClick={() => onAssign(g.accountId)}
                className="inline-flex items-center gap-1.5 h-9 px-3 rounded-xl text-xs font-semibold text-white bg-[var(--brand-primary)] hover:opacity-90"
              >
                Assign to {bankName}
                <ArrowRight className="w-3.5 h-3.5" />
              </button>
            </div>
          </div>
        ))}

        <p className={labelClass}>Or leave everything unassigned for now</p>
        <button
          type="button"
          onClick={onSkipAll}
          className="w-full h-10 rounded-xl text-sm font-semibold border border-[var(--border-subtle)] bg-[var(--bg-subtle)] text-[var(--text-main)] hover:bg-[var(--bg-surface-hover)]"
        >
          Continue without assigning
        </button>
      </div>
    </ModalShell>
  );
};
