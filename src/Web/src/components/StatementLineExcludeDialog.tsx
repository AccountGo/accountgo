import React, { useId, useState } from 'react';
import { Ban, X } from 'lucide-react';
import { ModalShell } from './ModalShell';
import { useAuth } from '../auth/AuthContext';
import { STATEMENT_EXCLUSION_REASONS, type BankStatementLine } from '../types/banking';

const labelClass = 'block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1';
const inputClass =
  'w-full h-10 px-3 rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-subtle)] text-sm text-[var(--text-main)]';

interface StatementLineExcludeDialogProps {
  line: BankStatementLine;
  formatCurrency: (n: number) => string;
  onSaved: () => void | Promise<void>;
  onClose: () => void;
}

/** Exclude a statement line without posting (#436 I3). */
export const StatementLineExcludeDialog: React.FC<StatementLineExcludeDialogProps> = ({
  line,
  formatCurrency,
  onSaved,
  onClose,
}) => {
  const { authFetch } = useAuth();
  const titleId = useId();
  const [reason, setReason] = useState(1);
  const [note, setNote] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      if (reason === 4 && !note.trim()) throw new Error('Add a short note when the reason is Other.');
      const res = await authFetch(`/api/Banking/StatementLines/${line.id}/Exclude`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ reason, note: note.trim() || null }),
      });
      if (!res.ok) {
        const body = await res.json().catch(() => null);
        throw new Error(body?.message || 'Could not exclude the line.');
      }
      await onSaved();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not exclude.');
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
          <Ban className="w-5 h-5 text-[var(--brand-primary)]" />
          <h3 id={titleId} className="font-bold text-[var(--text-main)] text-base">
            Exclude statement line
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
          Nothing is posted. {String(line.postedDate).slice(0, 10)} ·{' '}
          {line.payee || line.memo || 'Statement line'} ·{' '}
          <span className="font-mono font-semibold text-[var(--text-main)]">{formatCurrency(line.amount)}</span>
        </p>

        {error && (
          <div role="alert" className="p-3 bg-rose-500/10 border border-rose-500/20 rounded-xl text-xs text-rose-500">
            {error}
          </div>
        )}

        <div>
          <label htmlFor={`${titleId}-reason`} className={labelClass}>
            Reason
          </label>
          <select
            id={`${titleId}-reason`}
            value={reason}
            onChange={(e) => setReason(Number(e.target.value))}
            className={inputClass}
          >
            {STATEMENT_EXCLUSION_REASONS.map((r) => (
              <option key={r.value} value={r.value}>
                {r.label}
              </option>
            ))}
          </select>
        </div>

        <div>
          <label htmlFor={`${titleId}-note`} className={labelClass}>
            Note{reason === 4 ? ' *' : ' (optional)'}
          </label>
          <input
            id={`${titleId}-note`}
            value={note}
            onChange={(e) => setNote(e.target.value)}
            required={reason === 4}
            className={inputClass}
          />
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
            {busy ? 'Saving…' : 'Exclude'}
          </button>
        </div>
      </form>
    </ModalShell>
  );
};

export default StatementLineExcludeDialog;
