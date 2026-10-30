import React, { useEffect, useState } from 'react';
import { AlertCircle, CheckCircle2, Lock, Save } from 'lucide-react';
import { useAuth } from '../auth/AuthContext';
import { formatBooksLockDate, isBooksLockReopen } from '../lib/booksLock';

interface BooksLockHistoryItem {
  previousLockedThrough: string | null;
  newLockedThrough: string | null;
  reason: string | null;
  changedBy: string;
  changedAt: string;
}

interface BooksLockState {
  lockedThrough: string | null;
  history: BooksLockHistoryItem[];
}

const inputClass =
  'w-full h-11 px-3.5 rounded-xl bg-[var(--bg-subtle)] shadow-inner text-sm text-[var(--text-main)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-ring)] disabled:opacity-60';
const labelClass = 'block text-xs font-semibold text-[var(--text-secondary)] mb-1.5';

/** Books locked-through date with audited reopen. Visible on Financial Rules when Financials is enabled. */
export const BooksLockCard: React.FC<{ canClose: boolean }> = ({ canClose }) => {
  const { authFetch } = useAuth();
  const [state, setState] = useState<BooksLockState | null>(null);
  const [draft, setDraft] = useState('');
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ kind: 'ok' | 'error'; text: string } | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const res = await authFetch('/api/organizations/current/books-lock');
        if (res.ok && !cancelled) {
          const data = await res.json() as BooksLockState;
          setState(data);
          setDraft(data.lockedThrough ?? '');
        }
      } catch (err) {
        console.error('Failed to load books lock:', err);
      }
    })();
    return () => { cancelled = true; };
  }, [authFetch]);

  if (!state) return null;

  const reopen = isBooksLockReopen(state.lockedThrough, draft || null);
  const reasonRequired = canClose && reopen;

  const save = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!canClose) return;
    if (reasonRequired && !reason.trim()) {
      setMessage({ kind: 'error', text: 'A reason is required when reopening (moving the lock earlier or clearing it).' });
      return;
    }
    setBusy(true);
    setMessage(null);
    try {
      const res = await authFetch('/api/organizations/current/books-lock', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          lockedThrough: draft.trim() ? draft.trim() : null,
          reason: reason.trim() || undefined,
        }),
      });
      if (!res.ok) {
        const problem = await res.json().catch(() => null);
        throw new Error(problem?.message || problem?.detail || problem?.title || 'Could not save books lock.');
      }
      const data = await res.json() as BooksLockState;
      setState(data);
      setDraft(data.lockedThrough ?? '');
      setReason('');
      setMessage({ kind: 'ok', text: 'Books lock saved.' });
    } catch (err) {
      setMessage({ kind: 'error', text: err instanceof Error ? err.message : 'Could not save books lock.' });
    } finally {
      setBusy(false);
    }
  };

  return (
    <form onSubmit={save} className="p-4 sm:p-6 rounded-2xl bg-[var(--bg-surface)] shadow-md space-y-4">
      <div className="pb-2 border-b border-[var(--border-subtle)]">
        <div className="flex items-center gap-2.5">
          <Lock className="w-5 h-5 text-indigo-400" aria-hidden="true" />
          <div>
            <h3 className="text-sm font-bold text-[var(--text-main)]">Books locked through</h3>
            <p className="text-xs text-[var(--text-muted)]">
              Nothing dated on or before this date can be created, edited, voided or matched.
            </p>
          </div>
        </div>
      </div>

      <div className="text-sm text-[var(--text-main)]">
        Current:{' '}
        <span className="font-semibold">{formatBooksLockDate(state.lockedThrough)}</span>
      </div>

      {canClose && (
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
          <div>
            <label htmlFor="books-lock-date" className={labelClass}>Lock date</label>
            <input
              id="books-lock-date"
              type="date"
              className={inputClass}
              value={draft}
              onChange={(e) => setDraft(e.target.value)}
            />
            <p className="text-[11px] text-[var(--text-muted)] mt-1">Clear the date to unlock completely.</p>
          </div>
          <div>
            <label htmlFor="books-lock-reason" className={labelClass}>
              Reason{reasonRequired ? ' (required)' : ' (optional when locking)'}
            </label>
            <input
              id="books-lock-reason"
              type="text"
              className={inputClass}
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              required={reasonRequired}
              placeholder={reasonRequired ? 'Why are you reopening?' : 'Optional note'}
            />
          </div>
        </div>
      )}

      {message && (
        <div role={message.kind === 'error' ? 'alert' : 'status'} className={`flex items-start gap-2 p-3 rounded-xl text-xs font-medium ${message.kind === 'error' ? 'bg-rose-500/10 text-rose-400' : 'bg-emerald-500/10 text-emerald-400'}`}>
          {message.kind === 'error' ? <AlertCircle className="w-4 h-4 mt-0.5" aria-hidden="true" /> : <CheckCircle2 className="w-4 h-4 mt-0.5" aria-hidden="true" />}
          <span>{message.text}</span>
        </div>
      )}

      {canClose && (
        <div className="flex justify-end">
          <button type="submit" disabled={busy} className="btn-primary !h-11">
            <Save className="w-4 h-4" aria-hidden="true" /> {busy ? 'Saving…' : 'Save'}
          </button>
        </div>
      )}

      {state.history.length > 0 && (
        <div className="space-y-2 pt-2 border-t border-[var(--border-subtle)]">
          <h4 className="text-xs font-semibold text-[var(--text-secondary)]">Recent changes</h4>
          <ul className="space-y-2">
            {state.history.map((item, index) => (
              <li key={`${item.changedAt}-${index}`} className="text-xs text-[var(--text-secondary)] p-2 rounded-lg bg-[var(--bg-subtle)]">
                <div>
                  {formatBooksLockDate(item.previousLockedThrough)} → {formatBooksLockDate(item.newLockedThrough)}
                </div>
                <div className="text-[var(--text-muted)]">
                  {item.changedBy}
                  {item.reason ? ` · ${item.reason}` : ''}
                </div>
              </li>
            ))}
          </ul>
        </div>
      )}
    </form>
  );
};
