import React, { useEffect, useId, useState } from 'react';
import { FileText, X } from 'lucide-react';
import { ModalShell } from './ModalShell';
import { useAuth } from '../auth/AuthContext';
import type { BankStatementLine } from '../types/banking';

interface OpenInvoice {
  id: number;
  no?: string | null;
  customerName?: string | null;
  balanceDue?: number;
  amountDue?: number;
  totalAmount?: number;
  status?: string | number;
}

const labelClass = 'block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1';
const inputClass =
  'w-full h-10 px-3 rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-subtle)] text-sm text-[var(--text-main)]';

interface StatementLineApplyInvoiceDialogProps {
  line: BankStatementLine;
  formatCurrency: (n: number) => string;
  onSaved: () => void | Promise<void>;
  onClose: () => void;
}

/** Apply a money-in statement line to an open invoice (#436 I3). */
export const StatementLineApplyInvoiceDialog: React.FC<StatementLineApplyInvoiceDialogProps> = ({
  line,
  formatCurrency,
  onSaved,
  onClose,
}) => {
  const { authFetch } = useAuth();
  const titleId = useId();
  const amount = Math.abs(line.amount);

  const [invoices, setInvoices] = useState<OpenInvoice[]>([]);
  const [invoiceId, setInvoiceId] = useState('');
  const [memo, setMemo] = useState(line.memo || '');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      setLoading(true);
      try {
        const res = await authFetch('/api/Sales/SalesInvoices');
        if (!res.ok || cancelled) return;
        const data = await res.json();
        const list = (Array.isArray(data) ? data : data?.items ?? data?.invoices ?? []) as OpenInvoice[];
        const open = list.filter((inv) => {
          const due = inv.balanceDue ?? inv.amountDue ?? 0;
          return due > 0.005;
        });
        if (!cancelled) {
          setInvoices(open);
          const exact = open.find((inv) => Math.abs((inv.balanceDue ?? inv.amountDue ?? 0) - amount) < 0.005);
          if (exact) setInvoiceId(String(exact.id));
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [authFetch, amount]);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      if (!invoiceId) throw new Error('Pick an open invoice.');
      const res = await authFetch(`/api/Banking/StatementLines/${line.id}/ApplyOpenInvoice`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          invoiceId: Number(invoiceId),
          memo: memo.trim() || null,
        }),
      });
      if (!res.ok) {
        const body = await res.json().catch(() => null);
        throw new Error(body?.message || 'Could not apply the payment.');
      }
      await onSaved();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not save.');
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
          <FileText className="w-5 h-5 text-[var(--brand-primary)]" />
          <h3 id={titleId} className="font-bold text-[var(--text-main)] text-base">
            Apply to open invoice
          </h3>
        </div>
        <button
          type="button"
          onClick={onClose}
          disabled={busy}
          aria-label="Close"
          className="text-[var(--text-muted)] hover:text-[var(--text-main)] p-1 rounded-lg"
        >
          <X className="w-5 h-5" />
        </button>
      </div>

      <form onSubmit={(e) => void submit(e)} className="p-6 space-y-4">
        <p className="text-sm text-[var(--text-secondary)]">
          {String(line.postedDate).slice(0, 10)} · {line.payee || line.memo || 'Statement line'} ·{' '}
          <span className="font-mono font-semibold text-[var(--text-main)]">{formatCurrency(line.amount)}</span>
        </p>

        {error && (
          <div role="alert" className="p-3 bg-rose-500/10 border border-rose-500/20 rounded-xl text-xs text-rose-500">
            {error}
          </div>
        )}

        {loading ? (
          <p className="text-sm text-[var(--text-muted)]">Loading…</p>
        ) : (
          <div>
            <label htmlFor={`${titleId}-invoice`} className={labelClass}>
              Open invoice
            </label>
            <select
              id={`${titleId}-invoice`}
              required
              value={invoiceId}
              onChange={(e) => setInvoiceId(e.target.value)}
              className={inputClass}
            >
              <option value="">Select invoice…</option>
              {invoices.map((inv) => (
                <option key={inv.id} value={inv.id}>
                  {inv.no || `#${inv.id}`} · {inv.customerName || 'Customer'} · due{' '}
                  {formatCurrency(inv.balanceDue ?? inv.amountDue ?? 0)}
                </option>
              ))}
            </select>
            {invoices.length === 0 && (
              <p className="mt-1 text-xs text-[var(--text-muted)]">No open invoices with a balance due.</p>
            )}
          </div>
        )}

        <div>
          <label htmlFor={`${titleId}-memo`} className={labelClass}>
            Memo
          </label>
          <input id={`${titleId}-memo`} value={memo} onChange={(e) => setMemo(e.target.value)} className={inputClass} />
        </div>

        <div className="flex justify-end gap-2 pt-2">
          <button
            type="button"
            disabled={busy}
            onClick={onClose}
            className="h-10 px-4 rounded-xl text-xs font-semibold text-[var(--text-secondary)] hover:bg-[var(--bg-surface-hover)]"
          >
            Cancel
          </button>
          <button
            type="submit"
            disabled={busy || loading}
            className="h-10 px-4 rounded-xl text-xs font-semibold text-white bg-[var(--brand-primary)] disabled:opacity-50"
          >
            {busy ? 'Saving…' : 'Apply and match'}
          </button>
        </div>
      </form>
    </ModalShell>
  );
};

export default StatementLineApplyInvoiceDialog;
