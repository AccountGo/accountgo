import React, { useEffect, useId, useMemo, useState } from 'react';
import { Coins, X } from 'lucide-react';
import { ModalShell } from './ModalShell';
import { useAuth } from '../auth/AuthContext';
import type { BankStatementLine } from '../types/banking';

interface IncomeCategory {
  accountId: number;
  name: string;
  curated: boolean;
}

interface IncomeCategories {
  curated: IncomeCategory[];
  more: IncomeCategory[];
}

interface ClientOption {
  id: number;
  name?: string | null;
}

const labelClass = 'block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1';
const inputClass =
  'w-full h-10 px-3 rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-subtle)] text-sm text-[var(--text-main)]';

interface StatementLineOtherIncomeDialogProps {
  line: BankStatementLine;
  formatCurrency: (n: number) => string;
  onSaved: () => void | Promise<void>;
  onClose: () => void;
}

/** Record other income as a one-line issued+paid invoice (#436 I5). */
export const StatementLineOtherIncomeDialog: React.FC<StatementLineOtherIncomeDialogProps> = ({
  line,
  formatCurrency,
  onSaved,
  onClose,
}) => {
  const { authFetch } = useAuth();
  const titleId = useId();
  const [categories, setCategories] = useState<IncomeCategories | null>(null);
  const [clients, setClients] = useState<ClientOption[]>([]);
  const [customerId, setCustomerId] = useState('');
  const [incomeAccountId, setIncomeAccountId] = useState('');
  const [memo, setMemo] = useState(line.memo || '');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      setLoading(true);
      try {
        const [catRes, clientRes] = await Promise.all([
          authFetch('/api/Banking/StatementIncomeCategories'),
          authFetch('/api/Sales/Customers'),
        ]);
        if (cancelled) return;
        if (catRes.ok) {
          const data = (await catRes.json()) as IncomeCategories;
          setCategories(data);
          const first = data.curated[0] ?? data.more[0];
          if (first) setIncomeAccountId(String(first.accountId));
        }
        if (clientRes.ok) setClients((await clientRes.json()) as ClientOption[]);
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [authFetch]);

  const options = useMemo(() => {
    if (!categories) return [];
    return [...categories.curated, ...categories.more];
  }, [categories]);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      if (!incomeAccountId) throw new Error('Pick an income category.');
      const res = await authFetch(`/api/Banking/StatementLines/${line.id}/RecordOtherIncome`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          customerId: customerId ? Number(customerId) : null,
          incomeAccountId: Number(incomeAccountId),
          description: line.payee || line.memo || null,
          memo: memo.trim() || null,
        }),
      });
      if (!res.ok) {
        const body = await res.json().catch(() => null);
        throw new Error(body?.message || 'Could not record other income.');
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
          <Coins className="w-5 h-5 text-[var(--brand-primary)]" />
          <h3 id={titleId} className="font-bold text-[var(--text-main)] text-base">
            Record other income
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
          <>
            <div>
              <label htmlFor={`${titleId}-client`} className={labelClass}>
                Client (optional)
              </label>
              <select
                id={`${titleId}-client`}
                value={customerId}
                onChange={(e) => setCustomerId(e.target.value)}
                className={inputClass}
              >
                <option value="">Other income (workspace)</option>
                {clients.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
              </select>
            </div>

            <div>
              <label htmlFor={`${titleId}-category`} className={labelClass}>
                Category
              </label>
              <select
                id={`${titleId}-category`}
                required
                value={incomeAccountId}
                onChange={(e) => setIncomeAccountId(e.target.value)}
                className={inputClass}
              >
                <option value="">Select category…</option>
                {categories && categories.curated.length > 0 && (
                  <optgroup label="Common">
                    {categories.curated.map((c) => (
                      <option key={c.accountId} value={c.accountId}>
                        {c.name}
                      </option>
                    ))}
                  </optgroup>
                )}
                {categories && categories.more.length > 0 && (
                  <optgroup label="More…">
                    {categories.more.map((c) => (
                      <option key={c.accountId} value={c.accountId}>
                        {c.name}
                      </option>
                    ))}
                  </optgroup>
                )}
                {!categories &&
                  options.map((c) => (
                    <option key={c.accountId} value={c.accountId}>
                      {c.name}
                    </option>
                  ))}
              </select>
            </div>
          </>
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
            {busy ? 'Saving…' : 'Record and match'}
          </button>
        </div>
      </form>
    </ModalShell>
  );
};

export default StatementLineOtherIncomeDialog;
