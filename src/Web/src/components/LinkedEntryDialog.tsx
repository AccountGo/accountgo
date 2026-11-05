import React, { useEffect, useMemo, useState } from 'react';
import { useAuth } from '../auth/AuthContext';
import type { BankStatementLine } from '../types/banking';

interface LinkedAccount { id: number; accountCode: string; accountName: string; accountClassId: number; isCash: boolean }
interface LinkedAccountsResponse { accounts: LinkedAccount[]; workExpenseAccounts: LinkedAccount[]; personalCurrency: string }

export const LinkedEntryDialog: React.FC<{
  line: BankStatementLine;
  workBankId: number;
  initialKind: 'Personal' | 'MoneyIn';
  workCurrency: string;
  onClose: () => void;
  onSaved: (warning?: string) => void;
}> = ({ line, workBankId, initialKind, workCurrency, onClose, onSaved }) => {
  const { authFetch } = useAuth();
  const [accounts, setAccounts] = useState<LinkedAccountsResponse | null>(null);
  const [kind, setKind] = useState<'Personal' | 'MoveToPersonal' | 'MoneyIn'>(initialKind);
  const [personalAccountId, setPersonalAccountId] = useState('');
  const [split, setSplit] = useState(false);
  const [workExpenseAccountId, setWorkExpenseAccountId] = useState('');
  const [workAmount, setWorkAmount] = useState('');
  const [mirrorAmount, setMirrorAmount] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [warning, setWarning] = useState('');
  const amount = Math.abs(line.amount);
  const accountChoices = useMemo(() => (accounts?.accounts ?? []).filter(a =>
    kind === 'Personal' ? a.accountClassId === 5 : a.accountClassId === 1 && a.isCash), [accounts, kind]);

  useEffect(() => {
    let live = true;
    void authFetch('/api/linked-entries/accounts').then(async response => {
      if (!response.ok) throw new Error('Could not load personal accounts.');
      const data = await response.json() as LinkedAccountsResponse;
      if (live) {
        setAccounts(data);
        const first = data.accounts.find(a => initialKind === 'Personal' ? a.accountClassId === 5 : a.accountClassId === 1 && a.isCash);
        setPersonalAccountId(first ? String(first.id) : '');
      }
    }).catch(reason => { if (live) setError(reason instanceof Error ? reason.message : 'Could not load personal accounts.'); });
    return () => { live = false; };
  }, [authFetch, initialKind]);

  useEffect(() => {
    const first = accountChoices[0];
    if (!accountChoices.some(a => String(a.id) === personalAccountId)) setPersonalAccountId(first ? String(first.id) : '');
  }, [accountChoices, personalAccountId]);

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    setBusy(true); setError(''); setWarning('');
    try {
      const body: Record<string, unknown> = {
        kind, date: line.postedDate.slice(0, 10), amount, workBankAccountId: workBankId,
        personalAccountId: Number(personalAccountId), statementLineId: line.id,
        memo: line.memo || line.payee || undefined,
      };
      if (accounts && accounts.personalCurrency !== workCurrency) body.mirrorAmount = Number(mirrorAmount);
      if (split && kind === 'Personal') {
        body.splitWorkExpenseAccountId = Number(workExpenseAccountId);
        body.splitWorkAmount = Number(workAmount);
      }
      const response = await authFetch('/api/linked-entries', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
      const result = await response.json().catch(() => ({})) as { detail?: string; warning?: string };
      if (!response.ok) throw new Error(result.detail || 'Could not create the linked entry.');
      if (result.warning) setWarning(result.warning);
      onSaved(result.warning);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not create the linked entry.');
    } finally { setBusy(false); }
  };

  const inputClass = 'w-full rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-base)] px-3 py-2 text-sm';
  return <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4" role="presentation">
    <section role="dialog" aria-modal="true" aria-labelledby="linked-entry-title" className="w-full max-w-lg space-y-4 rounded-2xl bg-[var(--bg-surface)] p-5 shadow-xl">
      <div><h2 id="linked-entry-title" className="text-lg font-semibold">Link this bank line</h2><p className="text-sm text-[var(--text-secondary)]">This creates a balanced journal in each workspace.</p></div>
      <form className="space-y-4" onSubmit={event => void submit(event)}>
        {initialKind === 'Personal' && <fieldset className="space-y-2"><legend className="text-sm font-medium">How did you use the money?</legend>
          <label className="flex gap-2 text-sm"><input type="radio" checked={kind === 'Personal'} onChange={() => setKind('Personal')} /> Personal spending</label>
          <label className="flex gap-2 text-sm"><input type="radio" checked={kind === 'MoveToPersonal'} onChange={() => setKind('MoveToPersonal')} /> Move to a personal bank account</label>
        </fieldset>}
        <label className="grid gap-1 text-sm">{kind === 'Personal' ? 'Household spending category' : 'Personal bank account'}
          <select required className={inputClass} value={personalAccountId} onChange={e => setPersonalAccountId(e.target.value)}>
            <option value="">Choose an account</option>{accountChoices.map(account => <option key={account.id} value={account.id}>{account.accountCode} · {account.accountName}</option>)}
          </select>
        </label>
        <p className="text-sm text-[var(--text-secondary)]">Amount: {amount.toFixed(2)} {workCurrency}</p>
        {accounts && accounts.personalCurrency !== workCurrency && <label className="grid gap-1 text-sm">Amount in {accounts.personalCurrency}
          <input required min="0.01" step="0.01" type="number" className={inputClass} value={mirrorAmount} onChange={e => setMirrorAmount(e.target.value)} />
        </label>}
        {kind === 'Personal' && <div className="space-y-2 rounded-lg border border-[var(--border-subtle)] p-3">
          <label className="flex gap-2 text-sm"><input type="checkbox" checked={split} onChange={e => setSplit(e.target.checked)} /> Split part of this purchase as a work expense</label>
          {split && <div className="grid gap-3 sm:grid-cols-2">
            <label className="grid gap-1 text-sm">Work expense category<select required className={inputClass} value={workExpenseAccountId} onChange={e => setWorkExpenseAccountId(e.target.value)}>
              <option value="">Choose a category</option>{accounts?.workExpenseAccounts.map(a => <option key={a.id} value={a.id}>{a.accountCode} · {a.accountName}</option>)}
            </select></label>
            <label className="grid gap-1 text-sm">Work amount<input required type="number" min="0.01" step="0.01" max={(amount - 0.01).toFixed(2)} className={inputClass} value={workAmount} onChange={e => setWorkAmount(e.target.value)} /></label>
          </div>}
        </div>}
        {error && <p role="alert" className="text-sm text-red-600">{error}</p>}
        {warning && <p role="status" className="text-sm text-amber-700">{warning}</p>}
        <div className="flex justify-end gap-2"><button type="button" className="rounded-lg px-3 py-2 text-sm" onClick={onClose}>Cancel</button>
          <button type="submit" disabled={busy || !personalAccountId || (split && (!workExpenseAccountId || Number(workAmount) <= 0 || Number(workAmount) >= amount)) || (!!accounts && accounts.personalCurrency !== workCurrency && Number(mirrorAmount) <= 0)} className="rounded-lg bg-[var(--brand-primary)] px-4 py-2 text-sm font-semibold text-white disabled:opacity-50">
            {busy ? 'Saving…' : 'Create linked entry'}</button></div>
      </form>
    </section>
  </div>;
};
