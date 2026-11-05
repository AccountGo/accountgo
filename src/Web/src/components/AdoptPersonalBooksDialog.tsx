import { useState } from 'react';
import { useAuth } from '../auth/AuthContext';
import type { OrgDetails } from '../types';
import { ModalShell } from './ModalShell';

/** Records an existing personal workspace as the user's personal books. Nothing in the workspace changes. */
export function AdoptPersonalBooksDialog({ org, onClose, onAdopted }: {
  org: OrgDetails; onClose: () => void; onAdopted: () => void;
}) {
  const { authFetch } = useAuth();
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  async function confirm() {
    setSaving(true); setError('');
    try {
      const response = await authFetch(`/api/organizations/personal/adopt/${Number(org.id)}`, { method: 'POST' });
      if (!response.ok) {
        const body = await response.json().catch(() => ({}));
        throw new Error(body.detail || 'Could not use this workspace as your personal books.');
      }
      onAdopted();
    } catch (cause) { setError(cause instanceof Error ? cause.message : 'Could not use this workspace as your personal books.'); }
    finally { setSaving(false); }
  }
  return <ModalShell open onClose={() => { if (!saving) onClose(); }} aria-labelledby="adopt-personal-books-title">
    <div className="p-6 space-y-4">
      <h2 id="adopt-personal-books-title" className="text-lg font-bold text-[var(--text-main)]">Use {org.name} as your personal books?</h2>
      <p className="text-sm text-[var(--text-secondary)]">
        Your accounts, entries and plan stay exactly as they are. You can then link your work workspaces to it.
        Each person has one set of personal books, and personal books can't be transferred to someone else.
      </p>
      {error && <p role="alert" className="text-sm text-red-500">{error}</p>}
      <div className="flex justify-end gap-2">
        <button type="button" onClick={onClose} disabled={saving} className="px-4 py-2 rounded-lg bg-[var(--bg-subtle)]">Cancel</button>
        <button type="button" onClick={confirm} disabled={saving} className="px-4 py-2 rounded-lg bg-[var(--brand-primary)] text-white disabled:opacity-50">
          {saving ? 'Saving…' : 'Use as my personal books'}
        </button>
      </div>
    </div>
  </ModalShell>;
}
