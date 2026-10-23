import React, { useEffect, useId, useRef, useState } from 'react';
import { Building2, Loader2, X } from 'lucide-react';
import { PartyBase, PaymentTermOption } from '../types';
import { useAuth } from '../auth/AuthContext';
import { PartyRole, partyRoles } from '../lib/partyRoles';

const inputClass =
  'w-full px-3.5 py-2.5 text-sm bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-xl focus:ring-2 focus:ring-[var(--brand-ring)] focus:outline-none text-[var(--text-main)] placeholder-[var(--text-muted)] transition';
const labelClass = 'block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1';

/**
 * Adds a client or vendor, or edits one. Used from the Clients and Vendors pages and from the invoice editor. Saving
 * changes the party for new documents and drafts; invoices already issued and bills already approved keep the
 * details they were issued or approved with.
 */
export function PartyFormDialog<T extends PartyBase>({
  role,
  party,
  paymentTerms,
  onSaved,
  onClose,
}: {
  role: PartyRole;
  /** The client or vendor to edit; omitted to add one. */
  party?: T;
  paymentTerms: PaymentTermOption[];
  onSaved: (party: T) => void;
  onClose: () => void;
}) {
  const { authFetch } = useAuth();
  const config = partyRoles[role];
  const [name, setName] = useState(party?.name ?? '');
  const [email, setEmail] = useState(party?.email ?? '');
  const [phone, setPhone] = useState(party?.phone ?? '');
  const [street, setStreet] = useState(party?.street ?? '');
  const [city, setCity] = useState(party?.city ?? '');
  const [postalCode, setPostalCode] = useState(party?.postalCode ?? '');
  const [country, setCountry] = useState(party?.country ?? '');
  const [paymentTermId, setPaymentTermId] = useState<number | ''>(party?.paymentTermId ?? '');
  const [isSaving, setIsSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const nameRef = useRef<HTMLInputElement>(null);
  const titleId = useId();
  const isEdit = !!party;

  useEffect(() => {
    const trigger = document.activeElement as HTMLElement | null;
    nameRef.current?.focus();
    return () => trigger?.focus();
  }, []);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    if (!name.trim()) {
      setError(`Enter the ${config.noun}'s name.`);
      return;
    }

    setIsSaving(true);
    try {
      const res = await authFetch(config.api.save, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          id: party?.id ?? 0,
          name: name.trim(),
          email: email.trim() || null,
          phone: phone.trim() || null,
          street: street.trim() || null,
          city: city.trim() || null,
          postalCode: postalCode.trim() || null,
          country: country.trim() || null,
          paymentTermId: paymentTermId || null,
        }),
      });
      const data = await res.json().catch(() => null);
      if (!res.ok) {
        throw new Error(
          res.status === 403
            ? `You don't have permission to manage ${config.plural}.`
            : data?.message || (isEdit ? `The ${config.noun} could not be saved.` : `The ${config.noun} could not be added.`)
        );
      }
      onSaved(data as T);
    } catch (err: any) {
      setError(err.message);
      setIsSaving(false);
    }
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/60 backdrop-blur-sm animate-fade-in"
      onKeyDown={(e) => {
        if (e.key === 'Escape' && !isSaving) {
          e.preventDefault();
          onClose();
        }
      }}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        className="bg-[var(--bg-surface)] rounded-2xl max-w-lg w-full max-h-[calc(100dvh-2rem)] overflow-y-auto border border-[var(--border-subtle)] shadow-2xl"
      >
        <div className="px-6 py-4 border-b border-[var(--border-subtle)] flex items-center justify-between">
          <div className="flex items-center gap-2">
            <Building2 className="w-5 h-5 text-[var(--brand-primary)]" />
            <h3 id={titleId} className="font-bold text-[var(--text-main)] text-base">
              {isEdit ? `Edit ${config.noun}` : `New ${config.noun}`}
            </h3>
          </div>
          <button
            type="button"
            onClick={onClose}
            disabled={isSaving}
            aria-label="Close"
            className="text-[var(--text-muted)] hover:text-[var(--text-main)] p-1 rounded-lg transition"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        <form onSubmit={handleSubmit} className="p-6 space-y-4">
          {error && (
            <div role="alert" className="p-3 bg-rose-500/10 border border-rose-500/20 rounded-xl text-xs text-rose-500">
              {error}
            </div>
          )}

          <div>
            <label htmlFor={`${titleId}-name`} className={labelClass}>
              {config.Noun} / company name <span className="text-rose-500">*</span>
            </label>
            <input
              id={`${titleId}-name`}
              ref={nameRef}
              type="text"
              required
              maxLength={200}
              placeholder={config.copy.namePlaceholder}
              value={name}
              onChange={(e) => setName(e.target.value)}
              className={inputClass}
            />
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <div>
              <label htmlFor={`${titleId}-email`} className={labelClass}>
                Email
              </label>
              <input
                id={`${titleId}-email`}
                type="email"
                placeholder={config.copy.emailPlaceholder}
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                className={inputClass}
              />
            </div>
            <div>
              <label htmlFor={`${titleId}-phone`} className={labelClass}>
                Phone
              </label>
              <input
                id={`${titleId}-phone`}
                type="tel"
                placeholder="+1 (555) 000-0000"
                value={phone}
                onChange={(e) => setPhone(e.target.value)}
                className={inputClass}
              />
            </div>
          </div>

          <div>
            <label htmlFor={`${titleId}-street`} className={labelClass}>
              Street address
            </label>
            <input
              id={`${titleId}-street`}
              type="text"
              placeholder="123 Market St, Suite 400"
              value={street}
              onChange={(e) => setStreet(e.target.value)}
              className={inputClass}
            />
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
            <div>
              <label htmlFor={`${titleId}-city`} className={labelClass}>
                City / state
              </label>
              <input
                id={`${titleId}-city`}
                type="text"
                placeholder="New York, NY"
                value={city}
                onChange={(e) => setCity(e.target.value)}
                className={inputClass}
              />
            </div>
            <div>
              <label htmlFor={`${titleId}-postal`} className={labelClass}>
                Postal code
              </label>
              <input
                id={`${titleId}-postal`}
                type="text"
                placeholder="10001"
                value={postalCode}
                onChange={(e) => setPostalCode(e.target.value)}
                className={inputClass}
              />
            </div>
            <div>
              <label htmlFor={`${titleId}-country`} className={labelClass}>
                Country
              </label>
              <input
                id={`${titleId}-country`}
                type="text"
                placeholder="USA"
                value={country}
                onChange={(e) => setCountry(e.target.value)}
                className={inputClass}
              />
            </div>
          </div>

          <div>
            <label htmlFor={`${titleId}-terms`} className={labelClass}>
              Default payment terms
            </label>
            <select
              id={`${titleId}-terms`}
              value={paymentTermId}
              onChange={(e) => setPaymentTermId(e.target.value ? Number(e.target.value) : '')}
              className={inputClass}
            >
              <option value="">Workspace default</option>
              {paymentTerms.map((t) => (
                <option key={t.id} value={t.id}>
                  {t.description}
                </option>
              ))}
            </select>
          </div>

          {isEdit && (
            <p className="text-xs text-[var(--text-secondary)]">
              {config.copy.editNotice}
            </p>
          )}

          <div className="pt-2 flex items-center justify-end gap-3">
            <button
              type="button"
              onClick={onClose}
              disabled={isSaving}
              className="px-4 py-2.5 text-sm font-medium text-[var(--text-secondary)] bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-xl hover:bg-[var(--bg-surface-hover)] hover:text-[var(--text-main)] transition"
            >
              Cancel
            </button>
            <button
              type="submit"
              disabled={isSaving}
              className="inline-flex items-center gap-2 px-5 py-2.5 text-sm font-semibold text-[var(--brand-text)] bg-[var(--brand-primary)] rounded-xl hover:bg-[var(--brand-hover)] transition disabled:opacity-50 shadow-sm"
            >
              {isSaving && <Loader2 className="w-4 h-4 animate-spin" />}
              {isEdit ? `Save ${config.noun}` : `Add ${config.noun}`}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
