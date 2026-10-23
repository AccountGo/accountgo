import React, { useEffect, useState } from 'react';
import { AlertCircle, CheckCircle2, FileText, RefreshCw, Save } from 'lucide-react';
import { useAuth } from '../auth/AuthContext';
import { SalesInvoiceSettings, SellerNameSource } from '../types/invoicing';

interface InvoiceSettingsTabProps {
  tradingName: string;
  canEdit: boolean;
}

const DEFAULT_SETTINGS: SalesInvoiceSettings = {
  sellerName: 'TradingName',
  showSellerAddress: true,
  showCustomerEmail: true,
  showNotes: true,
  showPaymentInstructions: true,
  defaultNotes: null,
  defaultPaymentInstructions: null,
};

const MAX_DEFAULT_TEXT = 2000;

type ToggleKey = 'showSellerAddress' | 'showCustomerEmail' | 'showNotes' | 'showPaymentInstructions';

const TOGGLES: Array<{ key: ToggleKey; label: string; hint: string }> = [
  { key: 'showSellerAddress', label: 'Show business address', hint: 'Prints your address under the name in the invoice header.' },
  { key: 'showCustomerEmail', label: "Show customer's email in Bill to", hint: "Hide it when the invoice is shared outside the customer's team." },
  { key: 'showNotes', label: 'Show notes', hint: 'The per-invoice client notes block.' },
  { key: 'showPaymentInstructions', label: 'Show payment instructions', hint: 'The per-invoice payment instructions block.' },
];

const textareaClass =
  'w-full px-3.5 py-2.5 rounded-xl bg-[var(--bg-subtle)] shadow-inner text-sm text-[var(--text-main)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-ring)] disabled:opacity-60';

/** Workspace-wide presentation of sales invoices. Business details themselves live on the General tab. */
export const InvoiceSettingsTab: React.FC<InvoiceSettingsTabProps> = ({ tradingName, canEdit }) => {
  const { authFetch } = useAuth();
  const [settings, setSettings] = useState<SalesInvoiceSettings>(DEFAULT_SETTINGS);
  const [legalName, setLegalName] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const [settingsRes, profileRes] = await Promise.all([
          authFetch('/api/organizations/current/sales-invoice-settings'),
          authFetch('/api/organizations/current/business-profile'),
        ]);
        if (cancelled) return;
        if (settingsRes.ok) setSettings(await settingsRes.json() as SalesInvoiceSettings);
        if (profileRes.ok) setLegalName(((await profileRes.json()) as { legalName: string | null }).legalName);
      } catch (err) {
        console.error('Failed to load invoice settings:', err);
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, [authFetch]);

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault();
    setSaving(true);
    setError(null);
    setSaved(false);
    try {
      const res = await authFetch('/api/organizations/current/sales-invoice-settings', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(settings),
      });
      if (!res.ok) {
        const problem = await res.json().catch(() => null) as { detail?: string; title?: string } | null;
        throw new Error(problem?.detail || problem?.title || 'Failed to save invoice settings.');
      }
      setSettings(await res.json() as SalesInvoiceSettings);
      setSaved(true);
      setTimeout(() => setSaved(false), 3000);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save invoice settings.');
    } finally {
      setSaving(false);
    }
  };

  const disabled = !canEdit || loading || saving;
  const nameOptions: Array<{ value: SellerNameSource; label: string; preview: string | null }> = [
    { value: 'TradingName', label: 'Trading name', preview: tradingName },
    { value: 'LegalName', label: 'Legal name', preview: legalName },
  ];

  return (
    <form onSubmit={handleSave} className="space-y-6">
      {saved && (
        <div role="alert" aria-live="polite" className="p-4 rounded-2xl bg-emerald-500/10 text-emerald-400 text-xs font-semibold flex items-center gap-2.5 shadow-sm">
          <CheckCircle2 className="w-4 h-4 shrink-0" />
          <span>Invoice settings saved. Show/hide options apply to every invoice; the name applies to drafts and new invoices.</span>
        </div>
      )}
      {error && (
        <div role="alert" aria-live="assertive" className="p-4 rounded-2xl bg-rose-500/10 text-rose-400 text-xs font-semibold flex items-center gap-2.5 shadow-sm">
          <AlertCircle className="w-4 h-4 shrink-0" />
          <span>{error}</span>
        </div>
      )}

      <div className="p-4 sm:p-6 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md space-y-5">
        <div className="flex items-center justify-between pb-2 border-b border-[var(--border-subtle)]">
          <div className="flex items-center gap-2.5">
            <FileText className="w-5 h-5 text-[var(--brand-primary)]" />
            <div>
              <h2 className="text-sm sm:text-base font-bold text-[var(--text-main)]">Sales Invoices</h2>
              <p className="text-xs text-[var(--text-muted)]">What your invoices show, on screen and in the downloaded PDF</p>
            </div>
          </div>
          {loading && <RefreshCw className="w-4 h-4 text-[var(--brand-primary)] animate-spin" />}
        </div>

        <fieldset className="space-y-2" disabled={disabled}>
          <legend className="text-xs font-semibold text-[var(--text-secondary)] mb-1.5">Name shown on invoices</legend>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
            {nameOptions.map((option) => {
              const selected = settings.sellerName === option.value;
              return (
                <label
                  key={option.value}
                  className={`p-3 rounded-xl cursor-pointer flex items-start gap-3 transition-all ${
                    selected ? 'bg-[var(--brand-subtle)] ring-1 ring-[var(--brand-ring)]' : 'bg-[var(--bg-subtle)] hover:bg-[var(--bg-surface-hover)]'
                  }`}
                >
                  <input
                    type="radio"
                    name="seller-name"
                    value={option.value}
                    checked={selected}
                    onChange={() => setSettings((s) => ({ ...s, sellerName: option.value }))}
                    className="mt-0.5 accent-[var(--brand-primary)]"
                  />
                  <span className="min-w-0">
                    <span className="block text-xs font-semibold text-[var(--text-main)]">{option.label}</span>
                    <span className="block text-xs text-[var(--text-muted)] truncate">
                      {option.preview || 'Not set: add it under General Profile → Business Details'}
                    </span>
                  </span>
                </label>
              );
            })}
          </div>
          <p className="text-[11px] text-[var(--text-muted)]">Issued invoices keep the name they were sent with.</p>
          {settings.sellerName === 'LegalName' && !legalName && (
            <p className="text-[11px] text-amber-400">No legal name is set yet, so invoices keep using the trading name.</p>
          )}
        </fieldset>

        <fieldset className="space-y-2" disabled={disabled}>
          <legend className="text-xs font-semibold text-[var(--text-secondary)] mb-1.5">Show on invoices</legend>
          {TOGGLES.map((toggle) => (
            <label key={toggle.key} className="p-3 rounded-xl bg-[var(--bg-subtle)] flex items-start gap-3 cursor-pointer">
              <input
                type="checkbox"
                checked={settings[toggle.key]}
                onChange={(e) => setSettings((s) => ({ ...s, [toggle.key]: e.target.checked }))}
                className="mt-0.5 accent-[var(--brand-primary)]"
              />
              <span>
                <span className="block text-xs font-semibold text-[var(--text-main)]">{toggle.label}</span>
                <span className="block text-[11px] text-[var(--text-muted)]">{toggle.hint}</span>
              </span>
            </label>
          ))}
        </fieldset>

        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
          <div>
            <label htmlFor="default-notes-input" className="block text-xs font-semibold text-[var(--text-secondary)] mb-1.5">
              Default notes
            </label>
            <textarea
              id="default-notes-input"
              rows={4}
              maxLength={MAX_DEFAULT_TEXT}
              value={settings.defaultNotes ?? ''}
              onChange={(e) => setSettings((s) => ({ ...s, defaultNotes: e.target.value }))}
              disabled={disabled}
              placeholder="Thank you for your business!"
              className={textareaClass}
            />
          </div>
          <div>
            <label htmlFor="default-payment-input" className="block text-xs font-semibold text-[var(--text-secondary)] mb-1.5">
              Default payment instructions
            </label>
            <textarea
              id="default-payment-input"
              rows={4}
              maxLength={MAX_DEFAULT_TEXT}
              value={settings.defaultPaymentInstructions ?? ''}
              onChange={(e) => setSettings((s) => ({ ...s, defaultPaymentInstructions: e.target.value }))}
              disabled={disabled}
              placeholder="Bank, account name and number"
              className={textareaClass}
            />
          </div>
          <p className="sm:col-span-2 text-[11px] text-[var(--text-muted)] -mt-2">
            Prefilled on new invoices; each invoice can still be edited.
          </p>
        </div>

        {canEdit ? (
          <div className="pt-1 flex items-center justify-end">
            <button type="submit" disabled={disabled} className="btn-primary !h-11 !px-6 text-sm flex items-center gap-2">
              {saving ? <RefreshCw className="w-4 h-4 animate-spin" /> : <Save className="w-4 h-4" />}
              <span>{saving ? 'Saving…' : 'Save Invoice Settings'}</span>
            </button>
          </div>
        ) : (
          <p className="text-[11px] text-[var(--text-muted)]">Only workspace owners and admins can change invoice settings.</p>
        )}
      </div>
    </form>
  );
};
