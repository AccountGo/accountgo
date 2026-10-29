import React, { useEffect, useId, useMemo, useState } from 'react';
import { Landmark, X } from 'lucide-react';
import { ModalShell } from './ModalShell';
import { useAuth } from '../auth/AuthContext';
import { kindsForAmount } from '../lib/statementImport';
import type { BankStatementLine } from '../types/banking';

interface PostableAccount {
  id: number;
  accountCode: string;
  accountName: string;
  canPost: boolean;
  archived?: boolean;
  childAccounts?: PostableAccount[];
}

const labelClass = 'block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1';
const inputClass =
  'w-full h-10 px-3 rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-subtle)] text-sm text-[var(--text-main)]';

function flattenPostable(nodes: PostableAccount[], out: PostableAccount[] = []): PostableAccount[] {
  for (const node of nodes) {
    if (node.canPost && !node.archived) out.push(node);
    if (node.childAccounts?.length) flattenPostable(node.childAccounts, out);
  }
  return out;
}

interface StatementLineTransactionDialogProps {
  line: BankStatementLine;
  formatCurrency: (n: number) => string;
  onSaved: () => void | Promise<void>;
  onClose: () => void;
}

/** Posts a bank transaction for an unmatched statement line and links it. */
export const StatementLineTransactionDialog: React.FC<StatementLineTransactionDialogProps> = ({
  line,
  formatCurrency,
  onSaved,
  onClose,
}) => {
  const { authFetch } = useAuth();
  const titleId = useId();
  const kinds = useMemo(() => kindsForAmount(line.amount), [line.amount]);

  const [kind, setKind] = useState<number>(kinds[0].value);
  const [contraAccountId, setContraAccountId] = useState('');
  const [memo, setMemo] = useState(line.memo || line.payee || '');
  const [accounts, setAccounts] = useState<PostableAccount[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      const res = await authFetch('/api/Financials/Accounts');
      if (!res.ok || cancelled) return;
      const tree: PostableAccount[] = await res.json();
      if (!cancelled) setAccounts(flattenPostable(tree));
    })();
    return () => {
      cancelled = true;
    };
  }, [authFetch]);

  const needsContra = kinds.find((k) => k.value === kind)?.needsContra ?? true;

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      if (needsContra && !contraAccountId) throw new Error('Pick a contra account.');
      const res = await authFetch(`/api/Banking/StatementLines/${line.id}/CreateTransaction`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          kind,
          contraAccountId: contraAccountId ? Number(contraAccountId) : null,
          memo: memo.trim() || null,
        }),
      });
      if (!res.ok) {
        const body = await res.json().catch(() => null);
        throw new Error(body?.message || 'Could not record the transaction.');
      }
      await onSaved();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not record the transaction.');
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
          <Landmark className="w-5 h-5 text-[var(--brand-primary)]" />
          <h3 id={titleId} className="font-bold text-[var(--text-main)] text-base">
            Record from statement line
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

        <div>
          <label htmlFor={`${titleId}-kind`} className={labelClass}>
            Record as
          </label>
          <select
            id={`${titleId}-kind`}
            value={kind}
            onChange={(e) => setKind(Number(e.target.value))}
            className={inputClass}
          >
            {kinds.map((k) => (
              <option key={k.value} value={k.value}>
                {k.label}
              </option>
            ))}
          </select>
        </div>

        <div>
          <label htmlFor={`${titleId}-contra`} className={labelClass}>
            Contra account{needsContra ? ' *' : ' (optional)'}
          </label>
          <select
            id={`${titleId}-contra`}
            required={needsContra}
            value={contraAccountId}
            onChange={(e) => setContraAccountId(e.target.value)}
            className={inputClass}
          >
            <option value="">{needsContra ? 'Select account…' : 'Default account'}</option>
            {accounts.map((a) => (
              <option key={a.id} value={a.id}>
                {a.accountCode} — {a.accountName}
              </option>
            ))}
          </select>
        </div>

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
            disabled={busy}
            className="h-10 px-4 rounded-xl text-xs font-semibold text-white bg-[var(--brand-primary)] disabled:opacity-50"
          >
            {busy ? 'Saving…' : 'Record and match'}
          </button>
        </div>
      </form>
    </ModalShell>
  );
};

export default StatementLineTransactionDialog;
