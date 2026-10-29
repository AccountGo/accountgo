import React, { useEffect, useId, useRef, useState } from 'react';
import { Loader2, User, X } from 'lucide-react';
import { ContactPerson, PartyBase } from '../types';
import { useAuth } from '../auth/AuthContext';
import { PartyRole, partyRoles } from '../lib/partyRoles';
import { ModalShell } from './ModalShell';

const inputClass =
  'w-full px-3.5 py-2.5 text-sm bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-xl focus:ring-2 focus:ring-[var(--brand-ring)] focus:outline-none text-[var(--text-main)] placeholder-[var(--text-muted)] transition';
const labelClass = 'block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1';

/** Adds or edits a contact person on a client or vendor. */
export function ContactFormDialog({
  role,
  partyId,
  contact,
  onSaved,
  onClose,
}: {
  role: PartyRole;
  partyId: number;
  /** Omit to add a contact. */
  contact?: ContactPerson;
  onSaved: (party: PartyBase) => void;
  onClose: () => void;
}) {
  const { authFetch } = useAuth();
  const config = partyRoles[role];
  const [firstName, setFirstName] = useState(contact?.firstName ?? '');
  const [lastName, setLastName] = useState(contact?.lastName ?? '');
  const [email, setEmail] = useState(contact?.email ?? '');
  const [phone, setPhone] = useState(contact?.phone ?? '');
  const [street, setStreet] = useState(contact?.street ?? '');
  const [city, setCity] = useState(contact?.city ?? '');
  const [postalCode, setPostalCode] = useState(contact?.postalCode ?? '');
  const [country, setCountry] = useState(contact?.country ?? '');
  const [isBillTo, setIsBillTo] = useState(contact?.isBillTo ?? false);
  const [isPrimary, setIsPrimary] = useState(contact?.isPrimary ?? false);
  const [isSaving, setIsSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const firstNameRef = useRef<HTMLInputElement>(null);
  const titleId = useId();
  const isEdit = !!contact;

  useEffect(() => {
    const trigger = document.activeElement as HTMLElement | null;
    firstNameRef.current?.focus();
    return () => trigger?.focus();
  }, []);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    if (!firstName.trim() || !lastName.trim()) {
      setError("Enter the contact's first and last name.");
      return;
    }

    setIsSaving(true);
    try {
      const res = await authFetch(config.api.saveContact(partyId), {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          id: contact?.id ?? 0,
          firstName: firstName.trim(),
          lastName: lastName.trim(),
          email: email.trim() || null,
          phone: phone.trim() || null,
          street: street.trim() || null,
          city: city.trim() || null,
          postalCode: postalCode.trim() || null,
          country: country.trim() || null,
          isBillTo,
          isPrimary,
        }),
      });
      const data = await res.json().catch(() => null);
      if (!res.ok) {
        throw new Error(data?.message || 'The contact could not be saved.');
      }
      onSaved(data as PartyBase);
    } catch (err: any) {
      setError(err.message || 'The contact could not be saved.');
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <ModalShell
      open
      onClose={() => {
        if (!isSaving) onClose();
      }}
      closeOnEscape={!isSaving}
      closeOnBackdrop={!isSaving}
      bare
      aria-labelledby={titleId}
      panelClassName="bg-[var(--bg-surface)] rounded-2xl max-w-lg w-full max-h-[min(90vh,calc(100dvh-2rem))] overflow-y-auto border border-[var(--border-subtle)] shadow-2xl"
    >
      <form onSubmit={handleSubmit} className="flex flex-col">
        <div className="flex items-start gap-3 px-5 pt-5 pb-4 border-b border-[var(--border-subtle)]">
          <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-[var(--brand-subtle)] text-[var(--brand-primary)]">
            <User className="w-5 h-5" />
          </span>
          <div className="min-w-0 flex-1">
            <h2 id={titleId} className="text-base font-semibold text-[var(--text-main)]">
              {isEdit ? 'Edit contact' : 'Add contact'}
            </h2>
            <p className="text-xs text-[var(--text-secondary)] mt-0.5">
              Contact persons can be marked as Bill To for documents.
            </p>
          </div>
          <button
            type="button"
            onClick={onClose}
            disabled={isSaving}
            className="text-[var(--text-muted)] hover:text-[var(--text-main)] p-1"
            aria-label="Close"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        <div className="px-5 py-4 space-y-4 overflow-y-auto">
          {error && (
            <p role="alert" className="text-sm text-rose-600 dark:text-rose-400">
              {error}
            </p>
          )}
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <div>
              <label className={labelClass} htmlFor="contact-first">
                First name
              </label>
              <input
                id="contact-first"
                ref={firstNameRef}
                className={inputClass}
                value={firstName}
                onChange={(e) => setFirstName(e.target.value)}
                required
              />
            </div>
            <div>
              <label className={labelClass} htmlFor="contact-last">
                Last name
              </label>
              <input id="contact-last" className={inputClass} value={lastName} onChange={(e) => setLastName(e.target.value)} required />
            </div>
          </div>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <div>
              <label className={labelClass} htmlFor="contact-email">
                Email
              </label>
              <input
                id="contact-email"
                type="email"
                className={inputClass}
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                placeholder="name@example.com"
              />
            </div>
            <div>
              <label className={labelClass} htmlFor="contact-phone">
                Phone
              </label>
              <input id="contact-phone" className={inputClass} value={phone} onChange={(e) => setPhone(e.target.value)} />
            </div>
          </div>
          <div>
            <label className={labelClass} htmlFor="contact-street">
              Street
            </label>
            <input id="contact-street" className={inputClass} value={street} onChange={(e) => setStreet(e.target.value)} />
          </div>
          <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
            <div className="sm:col-span-1">
              <label className={labelClass} htmlFor="contact-city">
                City / Province
              </label>
              <input id="contact-city" className={inputClass} value={city} onChange={(e) => setCity(e.target.value)} />
            </div>
            <div>
              <label className={labelClass} htmlFor="contact-postal">
                Postal code
              </label>
              <input id="contact-postal" className={inputClass} value={postalCode} onChange={(e) => setPostalCode(e.target.value)} />
            </div>
            <div>
              <label className={labelClass} htmlFor="contact-country">
                Country
              </label>
              <input id="contact-country" className={inputClass} value={country} onChange={(e) => setCountry(e.target.value)} />
            </div>
          </div>
          <div className="space-y-2 pt-1">
            <label className="flex items-center gap-2 text-sm text-[var(--text-main)]">
              <input type="checkbox" checked={isBillTo} onChange={(e) => setIsBillTo(e.target.checked)} className="rounded border-[var(--border-subtle)]" />
              Set as default Bill To contact
            </label>
            <label className="flex items-center gap-2 text-sm text-[var(--text-main)]">
              <input type="checkbox" checked={isPrimary} onChange={(e) => setIsPrimary(e.target.checked)} className="rounded border-[var(--border-subtle)]" />
              Set as primary contact
            </label>
          </div>
        </div>

        <div className="px-5 py-4 border-t border-[var(--border-subtle)] flex justify-end gap-2">
          <button
            type="button"
            onClick={onClose}
            className="px-4 py-2 text-sm font-medium rounded-xl text-[var(--text-secondary)] hover:bg-[var(--bg-muted)]"
          >
            Cancel
          </button>
          <button
            type="submit"
            disabled={isSaving}
            className="inline-flex items-center gap-2 px-4 py-2 text-sm font-semibold rounded-xl bg-[var(--brand-primary)] text-white hover:opacity-90 disabled:opacity-50"
          >
            {isSaving && <Loader2 className="w-4 h-4 animate-spin" />}
            {isEdit ? 'Save contact' : 'Add contact'}
          </button>
        </div>
      </form>
    </ModalShell>
  );
}
