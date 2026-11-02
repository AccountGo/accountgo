import { useState } from 'react';
import { Link } from 'react-router-dom';
import { Bell } from 'lucide-react';
import { useAuth } from '../auth/AuthContext';
import { refreshIncomingReview, useIntegrations } from '../lib/integrations';

export function IncomingActivity({ orgSlug }: { orgSlug: string }) {
  const { capabilities, summary, error } = useIntegrations();
  const { authFetch } = useAuth();
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [readError, setReadError] = useState('');
  if (!capabilities?.canReview) return null;
  const markRead = async () => {
    if (!summary?.latestLineId || busy) return;
    setBusy(true);
    setReadError('');
    try {
      const res = await authFetch('/api/integrations/review/activity/read', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ throughLineId: summary.latestLineId }),
      });
      if (!res.ok) throw new Error();
      refreshIncomingReview();
    } catch { setReadError('Activity could not be marked read. Try again.'); }
    finally { setBusy(false); }
  };
  return (
    <div className="relative">
      <button type="button" aria-label={`Incoming activity${summary ? `: ${summary.unreadCount} unread` : ''}`}
        aria-expanded={open} aria-controls="incoming-activity" onClick={() => setOpen(!open)}
        className="relative p-3 rounded-xl hover:bg-[var(--bg-surface-hover)]">
        <Bell className="w-4 h-4" />
        {!!summary?.unreadCount && <span className="absolute right-0 top-0 rounded-full bg-[var(--brand-primary)] px-1 text-[10px] text-white">{summary.unreadCount}</span>}
      </button>
      {open && <section id="incoming-activity" aria-label="Incoming transaction activity"
        onKeyDown={event => { if (event.key === 'Escape') setOpen(false); }}
        className="absolute right-0 top-full z-30 w-72 rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] p-4 shadow-xl space-y-3">
        <h2 className="font-semibold text-sm">Incoming transactions</h2>
        {(error || readError) && <p role="alert" className="text-sm text-rose-500">{readError || error}</p>}
        <Link to={`/org/${orgSlug}/banking/needs-review`} onClick={() => setOpen(false)} className="block text-sm text-[var(--brand-primary)] underline">
          {summary ? `${summary.pendingCount} transaction${summary.pendingCount === 1 ? '' : 's'} need review` : 'Open Needs review'}
        </Link>
        <p className="text-xs text-[var(--text-muted)]">Reading activity does not accept a transaction or clear pending work.</p>
        {!!summary?.unreadCount && <button type="button" disabled={busy} onClick={() => void markRead()}
          className="text-sm underline disabled:opacity-50">Mark activity read</button>}
      </section>}
    </div>
  );
}
