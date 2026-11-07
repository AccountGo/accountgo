import React, { useEffect, useState } from 'react';
import { AlertCircle, CheckCircle2, Save } from 'lucide-react';
import { useAuth } from '../auth/AuthContext';

interface SessionPolicy {
  idleTimeoutMinutes: number;
  allowedIdleTimeoutMinutes: number[];
  absoluteSessionHours: number;
}

const ENDPOINT = '/api/organizations/current/session-policy';

export function idleOptionLabel(minutes: number): string {
  if (minutes < 60) return `${minutes} minutes`;
  const hours = minutes / 60;
  return hours === 1 ? '1 hour' : `${hours} hours`;
}

/** Workspace setting: sign members out after a period without activity. */
export const AutomaticSignOutCard: React.FC<{ canEdit: boolean }> = ({ canEdit }) => {
  const { authFetch, refreshSession } = useAuth();
  const [policy, setPolicy] = useState<SessionPolicy | null>(null);
  const [selected, setSelected] = useState<number | null>(null);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ kind: 'ok' | 'error'; text: string } | null>(null);

  useEffect(() => {
    let mounted = true;
    authFetch(ENDPOINT)
      .then(async (res) => {
        if (!res.ok || !mounted) return;
        const data: SessionPolicy = await res.json();
        setPolicy(data);
        setSelected(data.idleTimeoutMinutes);
      })
      .catch(() => {});
    return () => {
      mounted = false;
    };
  }, [authFetch]);

  if (!policy || selected === null) return null;

  const save = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setMessage(null);
    try {
      const res = await authFetch(ENDPOINT, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ idleTimeoutMinutes: selected }),
      });
      const data = await res.json().catch(() => null);
      if (!res.ok) throw new Error(data?.detail || 'Could not save the automatic sign-out setting.');
      setPolicy(data);
      setMessage({ kind: 'ok', text: 'Saved. Members pick up the new limit within a few minutes.' });
      // Apply it to this browser now rather than at the next routine refresh.
      void refreshSession();
    } catch (err) {
      setMessage({ kind: 'error', text: err instanceof Error ? err.message : 'Could not save the automatic sign-out setting.' });
    } finally {
      setBusy(false);
    }
  };

  return (
    <form onSubmit={save} className="p-4 sm:p-6 rounded-2xl bg-[var(--bg-surface)] shadow-md space-y-4">
      <div className="pb-2 border-b border-[var(--border-subtle)]">
        <h3 className="text-sm font-bold text-[var(--text-main)]">Automatic sign-out</h3>
        <p className="text-xs text-[var(--text-muted)]">
          Sign members out when they have been inactive. Any mouse, keyboard or touch activity counts.
        </p>
      </div>

      <div className="max-w-xs">
        <label htmlFor="idle-timeout" className="block text-xs font-semibold text-[var(--text-secondary)] mb-1.5">
          Sign out after inactivity of
        </label>
        <select
          id="idle-timeout"
          className="w-full h-11 px-3.5 rounded-xl bg-[var(--bg-subtle)] shadow-inner text-sm text-[var(--text-main)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-ring)] disabled:opacity-60"
          disabled={!canEdit || busy}
          value={selected}
          onChange={(e) => setSelected(Number(e.target.value))}
        >
          {policy.allowedIdleTimeoutMinutes.map((m) => (
            <option key={m} value={m}>{idleOptionLabel(m)}</option>
          ))}
        </select>
      </div>

      <p className="text-[11px] text-[var(--text-muted)]">
        Members see a one-minute warning first. Every session also ends {policy.absoluteSessionHours} hours after
        sign-in. Members who belong to stricter workspaces may be signed out sooner.
      </p>

      {message && (
        <div role={message.kind === 'error' ? 'alert' : 'status'} className={`flex items-start gap-2 p-3 rounded-xl text-xs font-medium ${message.kind === 'error' ? 'bg-rose-500/10 text-rose-400' : 'bg-emerald-500/10 text-emerald-400'}`}>
          {message.kind === 'error' ? <AlertCircle className="w-4 h-4 mt-0.5" aria-hidden="true" /> : <CheckCircle2 className="w-4 h-4 mt-0.5" aria-hidden="true" />}
          <span>{message.text}</span>
        </div>
      )}

      {canEdit && (
        <div className="flex justify-end">
          <button type="submit" disabled={busy || selected === policy.idleTimeoutMinutes} className="btn-primary !h-11">
            <Save className="w-4 h-4" aria-hidden="true" /> {busy ? 'Saving…' : 'Save'}
          </button>
        </div>
      )}
    </form>
  );
};

export default AutomaticSignOutCard;
