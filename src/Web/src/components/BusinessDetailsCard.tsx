import React, { useEffect, useState } from 'react';
import { AlertCircle, CheckCircle2, MapPin, RefreshCw, Save, UserRound } from 'lucide-react';
import { useAuth } from '../auth/AuthContext';
import { BusinessProfile } from '../types/invoicing';

interface BusinessDetailsCardProps {
  canEdit: boolean;
}

const EMPTY_PROFILE: BusinessProfile = {
  legalName: null,
  businessEmail: null,
  businessPhone: null,
  address: { line1: null, line2: null, city: null, region: null, postalCode: null, countryCode: null },
};

const inputClass =
  'w-full h-11 px-3.5 rounded-xl bg-[var(--bg-subtle)] shadow-inner text-sm text-[var(--text-main)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-ring)] disabled:opacity-60';
const labelClass = 'block text-xs font-semibold text-[var(--text-secondary)] mb-1.5';

/** The seller's legal name, contact details and address, printed in the "From" block of invoices. */
export const BusinessDetailsCard: React.FC<BusinessDetailsCardProps> = ({ canEdit }) => {
  const { authFetch, user } = useAuth();
  const [profile, setProfile] = useState<BusinessProfile>(EMPTY_PROFILE);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const res = await authFetch('/api/organizations/current/business-profile');
        if (res.ok && !cancelled) setProfile(await res.json() as BusinessProfile);
      } catch (err) {
        console.error('Failed to load business details:', err);
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, [authFetch]);

  const setField = (field: 'legalName' | 'businessEmail' | 'businessPhone', value: string) =>
    setProfile((p) => ({ ...p, [field]: value }));
  const setAddress = (field: keyof BusinessProfile['address'], value: string) =>
    setProfile((p) => ({ ...p, address: { ...p.address, [field]: value } }));

  const ownName = `${user?.firstName ?? ''} ${user?.lastName ?? ''}`.trim();

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault();
    setSaving(true);
    setError(null);
    setSaved(false);
    try {
      const res = await authFetch('/api/organizations/current/business-profile', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(profile),
      });
      if (!res.ok) {
        const problem = await res.json().catch(() => null) as { detail?: string; title?: string } | null;
        throw new Error(problem?.detail || problem?.title || 'Failed to save business details.');
      }
      setProfile(await res.json() as BusinessProfile);
      setSaved(true);
      setTimeout(() => setSaved(false), 3000);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save business details.');
    } finally {
      setSaving(false);
    }
  };

  const disabled = !canEdit || loading || saving;

  return (
    <form onSubmit={handleSave} className="p-4 sm:p-6 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md space-y-4">
      <div className="flex items-center justify-between pb-2 border-b border-[var(--border-subtle)]">
        <div className="flex items-center gap-2.5">
          <MapPin className="w-5 h-5 text-[var(--brand-primary)]" />
          <div>
            <h2 className="text-sm sm:text-base font-bold text-[var(--text-main)]">Business Details</h2>
            <p className="text-xs text-[var(--text-muted)]">
              Who your invoices come from. The address and contact details print in the invoice header.
            </p>
          </div>
        </div>
        {loading && <RefreshCw className="w-4 h-4 text-[var(--brand-primary)] animate-spin" />}
      </div>

      {saved && (
        <div role="alert" aria-live="polite" className="p-3 rounded-xl bg-emerald-500/10 text-emerald-400 text-xs font-semibold flex items-center gap-2">
          <CheckCircle2 className="w-4 h-4 shrink-0" />
          <span>Business details saved.</span>
        </div>
      )}
      {error && (
        <div role="alert" aria-live="assertive" className="p-3 rounded-xl bg-rose-500/10 text-rose-400 text-xs font-semibold flex items-center gap-2">
          <AlertCircle className="w-4 h-4 shrink-0" />
          <span>{error}</span>
        </div>
      )}

      <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
        <div className="sm:col-span-2">
          <label htmlFor="legal-name-input" className={labelClass}>Legal Name</label>
          <div className="flex flex-col sm:flex-row gap-2">
            <input
              id="legal-name-input"
              type="text"
              value={profile.legalName ?? ''}
              onChange={(e) => setField('legalName', e.target.value)}
              disabled={disabled}
              placeholder="e.g. Juan Dela Cruz"
              className={inputClass}
            />
            {ownName && canEdit && (
              <button
                type="button"
                disabled={disabled}
                onClick={() => setField('legalName', ownName)}
                className="h-11 px-4 shrink-0 rounded-xl bg-[var(--bg-subtle)] hover:bg-[var(--bg-surface-hover)] text-xs font-semibold text-[var(--text-secondary)] flex items-center justify-center gap-1.5 disabled:opacity-60"
              >
                <UserRound className="w-3.5 h-3.5" />
                <span>Use my name</span>
              </button>
            )}
          </div>
          <p className="text-[11px] text-[var(--text-muted)] mt-1">
            Registered or personal name. Independent contractors usually invoice under their own name; choose it under Settings → Invoices.
          </p>
        </div>

        <div>
          <label htmlFor="business-email-input" className={labelClass}>Business Email</label>
          <input
            id="business-email-input"
            type="email"
            value={profile.businessEmail ?? ''}
            onChange={(e) => setField('businessEmail', e.target.value)}
            disabled={disabled}
            placeholder="billing@example.com"
            className={inputClass}
          />
        </div>

        <div>
          <label htmlFor="business-phone-input" className={labelClass}>Business Phone</label>
          <input
            id="business-phone-input"
            type="tel"
            value={profile.businessPhone ?? ''}
            onChange={(e) => setField('businessPhone', e.target.value)}
            disabled={disabled}
            placeholder="+63 917 000 0000"
            className={inputClass}
          />
        </div>

        <div className="sm:col-span-2">
          <label htmlFor="address-line1-input" className={labelClass}>Address Line 1</label>
          <input
            id="address-line1-input"
            type="text"
            autoComplete="address-line1"
            value={profile.address.line1 ?? ''}
            onChange={(e) => setAddress('line1', e.target.value)}
            disabled={disabled}
            placeholder="Street address"
            className={inputClass}
          />
        </div>

        <div className="sm:col-span-2">
          <label htmlFor="address-line2-input" className={labelClass}>Address Line 2</label>
          <input
            id="address-line2-input"
            type="text"
            autoComplete="address-line2"
            value={profile.address.line2 ?? ''}
            onChange={(e) => setAddress('line2', e.target.value)}
            disabled={disabled}
            placeholder="Unit, building, floor (optional)"
            className={inputClass}
          />
        </div>

        <div>
          <label htmlFor="address-city-input" className={labelClass}>City</label>
          <input
            id="address-city-input"
            type="text"
            autoComplete="address-level2"
            value={profile.address.city ?? ''}
            onChange={(e) => setAddress('city', e.target.value)}
            disabled={disabled}
            className={inputClass}
          />
        </div>

        <div>
          <label htmlFor="address-region-input" className={labelClass}>State / Province / Region</label>
          <input
            id="address-region-input"
            type="text"
            autoComplete="address-level1"
            value={profile.address.region ?? ''}
            onChange={(e) => setAddress('region', e.target.value)}
            disabled={disabled}
            className={inputClass}
          />
        </div>

        <div>
          <label htmlFor="address-postal-input" className={labelClass}>Postal Code</label>
          <input
            id="address-postal-input"
            type="text"
            autoComplete="postal-code"
            value={profile.address.postalCode ?? ''}
            onChange={(e) => setAddress('postalCode', e.target.value)}
            disabled={disabled}
            className={inputClass}
          />
        </div>

        <div>
          <label htmlFor="address-country-input" className={labelClass}>Country</label>
          <input
            id="address-country-input"
            type="text"
            autoComplete="country"
            value={profile.address.countryCode ?? ''}
            onChange={(e) => setAddress('countryCode', e.target.value)}
            disabled={disabled}
            placeholder="e.g. Philippines"
            className={inputClass}
          />
        </div>
      </div>

      {canEdit ? (
        <div className="pt-3 flex items-center justify-end">
          <button type="submit" disabled={disabled} className="btn-primary !h-11 !px-6 text-sm flex items-center gap-2">
            {saving ? <RefreshCw className="w-4 h-4 animate-spin" /> : <Save className="w-4 h-4" />}
            <span>{saving ? 'Saving…' : 'Save Business Details'}</span>
          </button>
        </div>
      ) : (
        <p className="text-[11px] text-[var(--text-muted)]">Only workspace owners and admins can change business details.</p>
      )}
    </form>
  );
};
