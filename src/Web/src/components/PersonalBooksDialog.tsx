import React, { useState } from 'react';
import { useAuth } from '../auth/AuthContext';
import type { OrgDetails } from '../types';
import { ModalShell } from './ModalShell';

export function PersonalBooksDialog({ currentOrg, onClose, onCreated }: {
  currentOrg?: OrgDetails; onClose: () => void; onCreated: (slug: string) => void;
}) {
  const { user, authFetch } = useAuth();
  const [name, setName] = useState(`${user?.firstName?.trim() || user?.email?.split('@')[0] || 'My'} — Personal`);
  const [currency, setCurrency] = useState(currentOrg?.currency || 'USD');
  const canLink = currentOrg?.role === 'Owner' && !currentOrg.isPersonalBooks && !currentOrg.linkedPersonalCompanyId;
  const [link, setLink] = useState(canLink);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaving(true); setError('');
    try {
      const response = await authFetch('/api/organizations/personal', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name: name.trim(), currency: currency.trim().toUpperCase(),
          ...(canLink && link ? { linkWorkCompanyId: Number(currentOrg!.id) } : {}) }),
      });
      const body = await response.json();
      if (!response.ok) throw new Error(body.detail || 'Could not create personal books.');
      onCreated(body.slug);
    } catch (cause) { setError(cause instanceof Error ? cause.message : 'Could not create personal books.'); }
    finally { setSaving(false); }
  }
  return <ModalShell open onClose={() => { if (!saving) onClose(); }} aria-labelledby="personal-books-title">
    <form onSubmit={submit} className="p-6 space-y-4">
      <h2 id="personal-books-title" className="text-lg font-bold text-[var(--text-main)]">Add personal books</h2>
      <label className="block text-sm text-[var(--text-secondary)]">Name
        <input required maxLength={200} value={name} onChange={e => setName(e.target.value)}
          className="mt-1 w-full rounded-lg p-2 bg-[var(--bg-subtle)] text-[var(--text-main)]" />
      </label>
      <label className="block text-sm text-[var(--text-secondary)]">Currency
        <input required pattern="[A-Za-z]{3}" maxLength={3} value={currency} onChange={e => setCurrency(e.target.value)}
          className="mt-1 w-full rounded-lg p-2 bg-[var(--bg-subtle)] text-[var(--text-main)]" />
      </label>
      {canLink && <label className="flex gap-2 items-center text-sm text-[var(--text-secondary)]">
        <input type="checkbox" checked={link} onChange={e => setLink(e.target.checked)} />Link to {currentOrg?.name}
      </label>}
      {error && <p role="alert" className="text-sm text-red-500">{error}</p>}
      <div className="flex justify-end gap-2">
        <button type="button" onClick={onClose} disabled={saving} className="px-4 py-2 rounded-lg bg-[var(--bg-subtle)]">Cancel</button>
        <button type="submit" disabled={saving} className="px-4 py-2 rounded-lg bg-[var(--brand-primary)] text-white disabled:opacity-50">
          {saving ? 'Creating…' : 'Create personal books'}
        </button>
      </div>
    </form>
  </ModalShell>;
}
