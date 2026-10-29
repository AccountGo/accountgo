import React, { useEffect, useId, useMemo, useState } from 'react';
import { ArrowLeftRight, Landmark, X } from 'lucide-react';
import { ModalShell } from './ModalShell';
import { useAuth } from '../auth/AuthContext';
import { localToday } from '../lib/businessDays';
import type { BankAccount, BankReconciliation, CreateBankTransactionPayload } from '../types/banking';

interface PostableAccount {
  id: number;
  accountCode: string;
  accountName: string;
  canPost: boolean;
  archived?: boolean;
  childAccounts?: PostableAccount[];
}

type KindChoice = 1 | 2 | 3 | 4 | 5 | 6;

const KIND_OPTIONS: { value: KindChoice; label: string }[] = [
  { value: 1, label: 'Money in' },
  { value: 2, label: 'Money out' },
  { value: 3, label: 'Transfer' },
  { value: 4, label: 'Fee' },
  { value: 5, label: 'Interest' },
  { value: 6, label: 'Adjustment' },
];

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

interface BankTransactionDialogProps {
  bank: BankAccount;
  banks: BankAccount[];
  initialKind?: KindChoice;
  /** When true, Fee / Interest / Adjustment only (reconciliation adjustment). */
  adjustmentOnly?: boolean;
  /** When set with adjustmentOnly, posts via Reconciliations/{id}/Adjustments. */
  reconciliation?: { id: number; version: number };
  onSaved: (updatedRecon?: BankReconciliation) => void | Promise<void>;
  onClose: () => void;
}

export const BankTransactionDialog: React.FC<BankTransactionDialogProps> = ({
  bank,
  banks,
  initialKind = 1,
  adjustmentOnly = false,
  reconciliation,
  onSaved,
  onClose,
}) => {
  const { authFetch } = useAuth();
  const titleId = useId();
  const kindOptions = adjustmentOnly
    ? KIND_OPTIONS.filter((k) => k.value === 4 || k.value === 5 || k.value === 6)
    : KIND_OPTIONS;

  const [kind, setKind] = useState<KindChoice>(
    adjustmentOnly ? (initialKind === 4 || initialKind === 5 || initialKind === 6 ? initialKind : 4) : initialKind,
  );
  const [date, setDate] = useState(localToday());
  const [amount, setAmount] = useState('');
  const [contraAccountId, setContraAccountId] = useState('');
  const [toBankId, setToBankId] = useState('');
  const [increaseBalance, setIncreaseBalance] = useState(true);
  const [memo, setMemo] = useState('');
  const [referenceNo, setReferenceNo] = useState('');
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

  const otherBanks = useMemo(
    () => banks.filter((b) => b.id !== bank.id && !b.archivedAt),
    [banks, bank.id],
  );

  // Fee/Interest use default contra when omitted; Money in/out and Adjustment need an account.
  const contraRequired = kind === 1 || kind === 2 || kind === 6;

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const parsedAmount = Number(amount);
      if (!Number.isFinite(parsedAmount) || parsedAmount <= 0) {
        throw new Error('Enter an amount greater than zero.');
      }
      if (kind === 3 && !toBankId) {
        throw new Error('Pick a destination bank account.');
      }
      if (contraRequired && !contraAccountId) {
        throw new Error('Pick a contra account.');
      }

      if (adjustmentOnly && reconciliation) {
        const res = await authFetch(`/api/Banking/Reconciliations/${reconciliation.id}/Adjustments`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            kind,
            amount: parsedAmount,
            date,
            contraAccountId: contraAccountId ? Number(contraAccountId) : null,
            increaseBalance: kind === 6 ? increaseBalance : null,
            memo: memo.trim() || null,
            referenceNo: referenceNo.trim() || null,
            version: reconciliation.version,
          }),
        });
        if (!res.ok) {
          const body = await res.json().catch(() => null);
          throw new Error(body?.message || 'Could not add the adjustment.');
        }
        const updated: BankReconciliation = await res.json();
        await onSaved(updated);
        return;
      }

      const payload: CreateBankTransactionPayload = {
        kind,
        bankId: bank.id,
        date,
        amount: parsedAmount,
        memo: memo.trim() || null,
        referenceNo: referenceNo.trim() || null,
      };
      if (kind === 3) payload.toBankId = Number(toBankId);
      if (contraAccountId) payload.contraAccountId = Number(contraAccountId);
      if (kind === 6) payload.increaseBalance = increaseBalance;

      const res = await authFetch('/api/Banking/Transactions', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
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
          {kind === 3 ? (
            <ArrowLeftRight className="w-5 h-5 text-[var(--brand-primary)]" />
          ) : (
            <Landmark className="w-5 h-5 text-[var(--brand-primary)]" />
          )}
          <h3 id={titleId} className="font-bold text-[var(--text-main)] text-base">
            {adjustmentOnly ? 'Add fee / interest / adjustment' : 'Record transaction'}
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
        <p className="text-xs text-[var(--text-muted)]">
          On <span className="font-semibold text-[var(--text-main)]">{bank.name}</span>
        </p>

        {error && (
          <div role="alert" className="p-3 bg-rose-500/10 border border-rose-500/20 rounded-xl text-xs text-rose-500">
            {error}
          </div>
        )}

        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
          <div>
            <label htmlFor={`${titleId}-kind`} className={labelClass}>
              Type
            </label>
            <select
              id={`${titleId}-kind`}
              value={kind}
              onChange={(e) => setKind(Number(e.target.value) as KindChoice)}
              className={inputClass}
            >
              {kindOptions.map((opt) => (
                <option key={opt.value} value={opt.value}>
                  {opt.label}
                </option>
              ))}
            </select>
          </div>
          <div>
            <label htmlFor={`${titleId}-date`} className={labelClass}>
              Date
            </label>
            <input
              id={`${titleId}-date`}
              type="date"
              required
              value={date}
              onChange={(e) => setDate(e.target.value)}
              className={inputClass}
            />
          </div>
        </div>

        <div>
          <label htmlFor={`${titleId}-amount`} className={labelClass}>
            Amount
          </label>
          <input
            id={`${titleId}-amount`}
            type="number"
            min="0.01"
            step="0.01"
            required
            value={amount}
            onChange={(e) => setAmount(e.target.value)}
            className={inputClass}
          />
        </div>

        {kind === 3 && (
          <div>
            <label htmlFor={`${titleId}-to`} className={labelClass}>
              Transfer to
            </label>
            <select
              id={`${titleId}-to`}
              required
              value={toBankId}
              onChange={(e) => setToBankId(e.target.value)}
              className={inputClass}
            >
              <option value="">Select account…</option>
              {otherBanks.map((b) => (
                <option key={b.id} value={b.id}>
                  {b.name}
                </option>
              ))}
            </select>
          </div>
        )}

        {kind !== 3 && (contraRequired || kind === 4 || kind === 5) && (
          <div>
            <label htmlFor={`${titleId}-contra`} className={labelClass}>
              Contra account{contraRequired ? ' *' : ' (optional for Fee / Interest)'}
            </label>
            <select
              id={`${titleId}-contra`}
              required={contraRequired}
              value={contraAccountId}
              onChange={(e) => setContraAccountId(e.target.value)}
              className={inputClass}
            >
              <option value="">{contraRequired ? 'Select account…' : 'Default account'}</option>
              {accounts.map((a) => (
                <option key={a.id} value={a.id}>
                  {a.accountCode} — {a.accountName}
                </option>
              ))}
            </select>
          </div>
        )}

        {kind === 6 && (
          <fieldset className="space-y-2">
            <legend className={labelClass}>Direction</legend>
            <label className="flex items-center gap-2 text-sm text-[var(--text-main)]">
              <input type="radio" checked={increaseBalance} onChange={() => setIncreaseBalance(true)} />
              Increase bank balance
            </label>
            <label className="flex items-center gap-2 text-sm text-[var(--text-main)]">
              <input type="radio" checked={!increaseBalance} onChange={() => setIncreaseBalance(false)} />
              Decrease bank balance
            </label>
          </fieldset>
        )}

        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
          <div>
            <label htmlFor={`${titleId}-ref`} className={labelClass}>
              Reference
            </label>
            <input
              id={`${titleId}-ref`}
              value={referenceNo}
              onChange={(e) => setReferenceNo(e.target.value)}
              className={inputClass}
            />
          </div>
          <div>
            <label htmlFor={`${titleId}-memo`} className={labelClass}>
              Memo
            </label>
            <input id={`${titleId}-memo`} value={memo} onChange={(e) => setMemo(e.target.value)} className={inputClass} />
          </div>
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
            {busy ? 'Saving…' : 'Save'}
          </button>
        </div>
      </form>
    </ModalShell>
  );
};

export default BankTransactionDialog;
