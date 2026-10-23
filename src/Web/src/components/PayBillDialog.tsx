import React, { useEffect, useId, useRef, useState } from 'react';
import { Loader2, Wallet, X } from 'lucide-react';
import { Bill, BillAccount } from '../types';
import { useAuth } from '../auth/AuthContext';

const inputClass =
  'w-full px-3.5 py-2.5 text-sm bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-xl focus:ring-2 focus:ring-[var(--brand-ring)] focus:outline-none text-[var(--text-main)] placeholder-[var(--text-muted)] transition';
const labelClass = 'block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1';

const today = () => new Date().toISOString().slice(0, 10);

/**
 * Records a payment on an approved bill, from a cash or bank account. A payment can't be more than what is still
 * owed; the server checks that too.
 */
export const PayBillDialog: React.FC<{
  bill: Bill;
  currencySymbol: string;
  formatCurrency: (value: number) => string;
  /** The bill with the payment recorded. */
  onPaid: (bill: Bill) => void;
  onClose: () => void;
}> = ({ bill, currencySymbol, formatCurrency, onPaid, onClose }) => {
  const { authFetch } = useAuth();
  const [accounts, setAccounts] = useState<BillAccount[] | null>(null);
  const [accountId, setAccountId] = useState<number | ''>('');
  const [amount, setAmount] = useState(bill.balanceDue.toFixed(2));
  const [date, setDate] = useState(today());
  const [method, setMethod] = useState('Bank Transfer');
  const [reference, setReference] = useState('');
  const [memo, setMemo] = useState('');
  const [isSaving, setIsSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const amountRef = useRef<HTMLInputElement>(null);
  const titleId = useId();

  useEffect(() => {
    const trigger = document.activeElement as HTMLElement | null;
    amountRef.current?.focus();
    amountRef.current?.select();
    return () => trigger?.focus();
  }, []);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      const res = await authFetch('/api/Purchasing/PaymentAccounts');
      if (cancelled) return;
      const list: BillAccount[] = res.ok ? await res.json() : [];
      setAccounts(list);
      if (list.length > 0) setAccountId(list[0].id);
    })();
    return () => {
      cancelled = true;
    };
  }, [authFetch]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    const value = Number(amount);
    if (!(value > 0)) {
      setError('Enter an amount more than zero.');
      return;
    }
    if (value > bill.balanceDue + 0.005) {
      setError(`The payment can't be more than the ${formatCurrency(bill.balanceDue)} still owed.`);
      return;
    }

    setIsSaving(true);
    setError(null);
    try {
      const res = await authFetch('/api/Purchasing/PayBill', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          billId: bill.id,
          amount: value,
          date,
          accountId: accountId || null,
          paymentMethod: method,
          referenceNo: reference.trim() || null,
          memo: memo.trim() || null,
        }),
      });
      const data = await res.json().catch(() => null);
      if (!res.ok) {
        throw new Error(res.status === 403 ? "You don't have permission to pay bills." : data?.message || 'The payment could not be recorded.');
      }
      onPaid(data as Bill);
    } catch (err: any) {
      setError(err.message);
      setIsSaving(false);
    }
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/60 backdrop-blur-sm animate-fade-in"
      onKeyDown={(e) => {
        if (e.key === 'Escape' && !isSaving) {
          e.preventDefault();
          onClose();
        }
      }}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        className="bg-[var(--bg-surface)] rounded-2xl max-w-md w-full max-h-[calc(100dvh-2rem)] overflow-y-auto border border-[var(--border-subtle)] shadow-2xl"
      >
        <div className="px-6 py-4 border-b border-[var(--border-subtle)] flex items-center justify-between">
          <div className="min-w-0">
            <div className="flex items-center gap-2">
              <Wallet className="w-5 h-5 text-[var(--brand-primary)]" />
              <h3 id={titleId} className="font-bold text-[var(--text-main)] text-base">
                Pay bill
              </h3>
            </div>
            <p className="mt-0.5 truncate text-xs text-[var(--text-secondary)]">
              {[bill.vendorInvoiceNo, bill.vendorName].filter(Boolean).join(' · ')}
            </p>
          </div>
          <button
            type="button"
            onClick={onClose}
            disabled={isSaving}
            aria-label="Close"
            className="text-[var(--text-muted)] hover:text-[var(--text-main)] p-1 rounded-lg transition"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        <form onSubmit={handleSubmit} className="p-6 space-y-4">
          {error && (
            <div role="alert" className="p-3 bg-rose-500/10 border border-rose-500/20 rounded-xl text-xs text-rose-500">
              {error}
            </div>
          )}

          <div className="flex items-center justify-between rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-subtle)] px-4 py-3 text-sm">
            <span className="text-[var(--text-secondary)]">Still owed</span>
            <span className="font-mono font-bold text-[var(--text-main)]">{formatCurrency(bill.balanceDue)}</span>
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <div>
              <label htmlFor={`${titleId}-amount`} className={labelClass}>
                Amount ({currencySymbol}) <span className="text-rose-500">*</span>
              </label>
              <input
                id={`${titleId}-amount`}
                ref={amountRef}
                type="number"
                inputMode="decimal"
                min="0.01"
                step="0.01"
                required
                value={amount}
                onChange={(e) => setAmount(e.target.value)}
                className={`${inputClass} font-mono`}
              />
            </div>
            <div>
              <label htmlFor={`${titleId}-date`} className={labelClass}>
                Date paid <span className="text-rose-500">*</span>
              </label>
              <input id={`${titleId}-date`} type="date" required value={date} onChange={(e) => setDate(e.target.value)} className={inputClass} />
            </div>
          </div>

          <div>
            <label htmlFor={`${titleId}-account`} className={labelClass}>
              Paid from
            </label>
            {accounts === null ? (
              <div className="flex h-[42px] items-center gap-2 text-xs text-[var(--text-muted)]">
                <Loader2 className="w-3.5 h-3.5 animate-spin" />
                Loading accounts...
              </div>
            ) : accounts.length === 0 ? (
              <p className="text-xs text-rose-500">No cash or bank account is set up to pay from.</p>
            ) : (
              <select
                id={`${titleId}-account`}
                value={accountId}
                onChange={(e) => setAccountId(e.target.value ? Number(e.target.value) : '')}
                className={inputClass}
              >
                {accounts.map((a) => (
                  <option key={a.id} value={a.id}>
                    {a.code} · {a.name}
                  </option>
                ))}
              </select>
            )}
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <div>
              <label htmlFor={`${titleId}-method`} className={labelClass}>
                Method
              </label>
              <select id={`${titleId}-method`} value={method} onChange={(e) => setMethod(e.target.value)} className={inputClass}>
                <option value="Bank Transfer">Bank transfer</option>
                <option value="Check">Check</option>
                <option value="Cash">Cash</option>
                <option value="Credit Card">Credit card</option>
                <option value="Other">Other</option>
              </select>
            </div>
            <div>
              <label htmlFor={`${titleId}-reference`} className={labelClass}>
                Reference
              </label>
              <input
                id={`${titleId}-reference`}
                type="text"
                maxLength={100}
                placeholder="Check or transfer #"
                value={reference}
                onChange={(e) => setReference(e.target.value)}
                className={inputClass}
              />
            </div>
          </div>

          <div>
            <label htmlFor={`${titleId}-memo`} className={labelClass}>
              Memo
            </label>
            <input id={`${titleId}-memo`} type="text" maxLength={500} value={memo} onChange={(e) => setMemo(e.target.value)} className={inputClass} />
          </div>

          <div className="pt-2 flex items-center justify-end gap-3">
            <button
              type="button"
              onClick={onClose}
              disabled={isSaving}
              className="px-4 py-2.5 text-sm font-medium text-[var(--text-secondary)] bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-xl hover:bg-[var(--bg-surface-hover)] hover:text-[var(--text-main)] transition"
            >
              Cancel
            </button>
            <button
              type="submit"
              disabled={isSaving || accounts?.length === 0}
              className="inline-flex items-center gap-2 px-5 py-2.5 text-sm font-semibold text-[var(--brand-text)] bg-[var(--brand-primary)] rounded-xl hover:bg-[var(--brand-hover)] transition disabled:opacity-50 shadow-sm"
            >
              {isSaving && <Loader2 className="w-4 h-4 animate-spin" />}
              Record payment
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};
