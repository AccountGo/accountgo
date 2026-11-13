import React, { useId, useState } from 'react';
import { X } from 'lucide-react';
import { ModalShell } from './ModalShell';
import { useAuth } from '../auth/AuthContext';
import { withConfirmReconciledPeriod } from '../lib/reconciledPeriod';
import { useReconciledPeriodConfirm } from '../lib/useReconciledPeriodConfirm';
import type { BankStatementLine, StatementOwnerAction } from '../types/banking';

interface Props {
  lines: BankStatementLine[];
  undo?: boolean;
  action: StatementOwnerAction;
  formatCurrency: (n: number) => string;
  onSaved: () => void | Promise<void>;
  onClose: () => void;
}

export const StatementLineOwnerMovementDialog: React.FC<Props> = ({ lines, action, undo = false, formatCurrency, onSaved, onClose }) => {
  const { authFetch } = useAuth();
  const titleId = useId();
  const [memo, setMemo] = useState('');
  const [rememberRule, setRememberRule] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const periodConfirm = useReconciledPeriodConfirm();
  const drawing = lines[0].amount < 0;
  const bulk = lines.length > 1;
  const description = lines[0].payee || lines[0].memo || '';

  const submit = async (event: React.FormEvent | null, confirmed = false) => {
    event?.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const route = undo ? `${lines[0].id}/UndoOwnerMovement` : bulk ? 'BulkRecordOwnerMovement' : `${lines[0].id}/RecordOwnerMovement`;
      const response = await authFetch(withConfirmReconciledPeriod(`/api/Banking/StatementLines/${route}`, confirmed), {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(undo ? { reason: memo.trim() || null } : { ...(bulk ? { lineIds: lines.map(l => l.id) } : {}), memo: memo.trim() || null, rememberRule }),
      });
      if (await periodConfirm.handleConflict(response, () => void submit(null, true))) return;
      if (!response.ok) {
        const body = await response.json().catch(() => null);
        throw new Error(body?.message || (undo ? 'Could not undo the owner movement.' : 'Could not record the owner movement.'));
      }
      await onSaved();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not save.');
    } finally { setBusy(false); }
  };

  return <>
    <ModalShell open onClose={() => { if (!busy) onClose(); }} closeOnEscape={!busy} closeOnBackdrop={!busy}
      bare aria-labelledby={titleId}
      panelClassName="bg-[var(--bg-surface)] rounded-2xl max-w-lg w-full max-h-[min(90vh,calc(100dvh-2rem))] overflow-y-auto border border-[var(--border-subtle)] shadow-2xl">
      <div className="px-6 py-4 border-b border-[var(--border-subtle)] flex items-center justify-between">
        <h3 id={titleId} className="font-bold text-[var(--text-main)] text-base">{undo ? `Undo owner ${lines[0].matchedOwnerAction}?` : action.label}{bulk ? ` (${lines.length})` : ''}</h3>
        <button type="button" onClick={onClose} disabled={busy} aria-label="Close" className="p-1 rounded-lg text-[var(--text-muted)]"><X className="w-5 h-5" /></button>
      </div>
      <form onSubmit={e => void submit(e)} className="p-6 space-y-4">
        {!undo && <><ul className="text-sm text-[var(--text-secondary)] space-y-1">
          {lines.map(line => <li key={line.id}>{String(line.postedDate).slice(0, 10)} · {line.payee || line.memo || 'Statement line'} · <span className="font-mono">{formatCurrency(line.amount)}</span></li>)}
        </ul>
        <p className="text-sm text-[var(--text-secondary)]">Account: {action.accountName} ({action.accountCode})</p>
        <p className="text-sm text-[var(--text-secondary)]">{drawing
          ? "Money you took out of the business for personal use. It isn't an expense and doesn't reduce profit."
          : "Money you put into the business from personal funds. It isn't income."}</p>
        </>}
        {undo && <p className="text-sm text-[var(--text-secondary)]">Undo this owner {lines[0].matchedOwnerAction}? A reversing entry is posted and the line goes back to unmatched.</p>}
        {error && <p role="alert" className="p-3 bg-rose-500/10 rounded-xl text-sm text-rose-500">{error}</p>}
        <label className="block text-sm text-[var(--text-secondary)]" htmlFor={`${titleId}-memo`}>{undo ? 'Reason (optional)' : 'Memo (optional)'}</label>
        <input id={`${titleId}-memo`} value={memo} onChange={e => setMemo(e.target.value)} maxLength={500}
          className="w-full h-10 px-3 rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-subtle)] text-sm text-[var(--text-main)]" />
        {!undo && <label className="flex items-start gap-2 text-sm text-[var(--text-secondary)]">
          <input type="checkbox" checked={rememberRule} onChange={e => setRememberRule(e.target.checked)} className="mt-1" />
          {bulk ? 'Always suggest this for these descriptions' : `Always suggest this for “${description}”`}
        </label>}
        <div className="flex justify-end gap-2 pt-2">
          <button type="button" disabled={busy} onClick={onClose} className="h-10 px-4 rounded-xl text-xs font-semibold text-[var(--text-secondary)] hover:bg-[var(--bg-surface-hover)]">Cancel</button>
          <button type="submit" disabled={busy} className="h-10 px-4 rounded-xl text-xs font-semibold text-white bg-[var(--brand-primary)] disabled:opacity-50">{busy ? 'Saving…' : undo ? 'Undo' : 'Record'}</button>
        </div>
      </form>
    </ModalShell>
    {periodConfirm.dialog}
  </>;
};
