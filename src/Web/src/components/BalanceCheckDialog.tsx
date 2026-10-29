import React, { useId, useState } from 'react';
import { Scale, X } from 'lucide-react';
import { ModalShell } from './ModalShell';
import { useAuth } from '../auth/AuthContext';
import { localToday } from '../lib/businessDays';
import { parseBalanceInput } from '../lib/balanceCheck';
import type { BankAccount, BankBalanceCheck } from '../types/banking';

const labelClass = 'block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1';
const inputClass =
  'w-full h-10 px-3 rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-subtle)] text-sm text-[var(--text-main)]';

/** Type in the balance the bank shows; nothing is posted. */
export const BalanceCheckDialog: React.FC<{
  bank: BankAccount;
  formatCurrency: (n: number) => string;
  onClose: () => void;
}> = ({ bank, formatCurrency, onClose }) => {
  const titleId = useId();
  const { authFetch } = useAuth();
  const [balance, setBalance] = useState('');
  const [asOfDate, setAsOfDate] = useState(localToday());
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<BankBalanceCheck | null>(null);

  const check = async (e: React.FormEvent) => {
    e.preventDefault();
    const parsed = parseBalanceInput(balance);
    if (parsed === null) {
      setError('Enter the balance the bank shows.');
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const res = await authFetch(`/api/Banking/Accounts/${bank.id}/BalanceCheck`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ asOfBalance: parsed, asOfDate }),
      });
      if (!res.ok) {
        const body = await res.json().catch(() => null);
        throw new Error(body?.message || 'Could not check the balance.');
      }
      setResult((await res.json()) as BankBalanceCheck);
    } catch (err: unknown) {
      setResult(null);
      setError(err instanceof Error ? err.message : 'Could not check the balance.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <ModalShell
      open
      onClose={() => {
        if (!busy) onClose();
      }}
      closeOnEscape={!busy}
      closeOnBackdrop={!busy}
      bare
      aria-labelledby={titleId}
      panelClassName="bg-[var(--bg-surface)] rounded-2xl max-w-lg w-full max-h-[min(90vh,calc(100dvh-2rem))] overflow-y-auto border border-[var(--border-subtle)] shadow-2xl"
    >
      <div className="px-6 py-4 border-b border-[var(--border-subtle)] flex items-center justify-between">
        <div className="flex items-center gap-2">
          <Scale className="w-5 h-5 text-[var(--brand-primary)]" />
          <h3 id={titleId} className="font-bold text-[var(--text-main)] text-base">
            Check balance
          </h3>
        </div>
        <button type="button" onClick={onClose} disabled={busy} aria-label="Close" className="text-[var(--text-muted)] p-1 rounded-lg">
          <X className="w-5 h-5" />
        </button>
      </div>

      <form onSubmit={(e) => void check(e)} className="p-6 space-y-4">
        <p className="text-xs text-[var(--text-muted)]">
          Compare <span className="font-semibold text-[var(--text-main)]">{bank.name}</span> with the balance your bank
          shows. Nothing is changed in your books.
        </p>
        {error && (
          <div role="alert" className="p-3 bg-rose-500/10 border border-rose-500/20 rounded-xl text-xs text-rose-500">
            {error}
          </div>
        )}
        <div className="grid grid-cols-2 gap-3">
          <div>
            <label htmlFor={`${titleId}-balance`} className={labelClass}>
              Bank balance
            </label>
            <input
              id={`${titleId}-balance`}
              inputMode="decimal"
              value={balance}
              onChange={(e) => setBalance(e.target.value)}
              className={inputClass}
              autoFocus
            />
          </div>
          <div>
            <label htmlFor={`${titleId}-date`} className={labelClass}>
              As of
            </label>
            <input
              id={`${titleId}-date`}
              type="date"
              value={asOfDate}
              max={localToday()}
              onChange={(e) => setAsOfDate(e.target.value)}
              className={inputClass}
            />
          </div>
        </div>

        {result && (
          <div className="space-y-3" data-testid="balance-check-result">
            <div
              className={`p-3 rounded-xl text-sm ${result.isMatch ? 'bg-emerald-500/10 text-emerald-700' : 'bg-amber-500/10 text-amber-700'}`}
            >
              <p className="font-semibold">{result.message}</p>
              <p className="text-xs mt-1 font-mono">
                Bank {formatCurrency(result.bankBalance)} · Books {formatCurrency(result.bookBalance)}
                {!result.isMatch ? ` · Difference ${formatCurrency(result.difference)}` : ''}
              </p>
            </div>
            {result.recentLines.length > 0 && (
              <div>
                <p className={labelClass}>Recent activity</p>
                <ul className="divide-y divide-[var(--border-subtle)] border border-[var(--border-subtle)] rounded-xl text-xs">
                  {result.recentLines.map((line) => (
                    <li
                      key={line.lineId}
                      className={`px-3 py-2 flex items-center justify-between gap-3 ${line.mayExplainDifference ? 'bg-amber-500/10' : ''}`}
                    >
                      <span className="min-w-0">
                        <span className="text-[var(--text-muted)]">{String(line.date).slice(0, 10)}</span>{' '}
                        <span className="font-medium text-[var(--text-main)]">{line.type}</span>
                        {(line.payee || line.reference) && (
                          <span className="text-[var(--text-muted)]"> · {line.payee || line.reference}</span>
                        )}
                        {line.mayExplainDifference && <span className="ml-2 font-semibold text-amber-700">Same as the difference</span>}
                      </span>
                      <span className="font-mono shrink-0">
                        {line.in > 0 ? `+${formatCurrency(line.in)}` : `-${formatCurrency(line.out)}`}
                      </span>
                    </li>
                  ))}
                </ul>
              </div>
            )}
          </div>
        )}

        <div className="flex justify-end gap-2 pt-2">
          <button
            type="button"
            disabled={busy}
            onClick={onClose}
            className="h-10 px-4 rounded-xl text-xs font-semibold text-[var(--text-secondary)] hover:bg-[var(--bg-surface-hover)]"
          >
            Close
          </button>
          <button
            type="submit"
            disabled={busy}
            className="h-10 px-4 rounded-xl text-xs font-semibold text-white bg-[var(--brand-primary)] disabled:opacity-50"
          >
            {busy ? 'Checking…' : 'Check'}
          </button>
        </div>
      </form>
    </ModalShell>
  );
};

export default BalanceCheckDialog;
