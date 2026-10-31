import { useEffect, useState } from 'react';
import { AlertCircle, RotateCcw, Save } from 'lucide-react';
import { useAuth } from '../auth/AuthContext';
import { getPasskeyAssertion, isWebAuthnSupported } from '../lib/webauthn';

export interface InvoicePdfPalette {
  text: string;
  secondaryText: string;
  mutedText: string;
  border: string;
  surface: string;
  accent: string;
  success: string;
  danger: string;
  neutral: string;
}

export interface InvoiceBrandingCapabilities {
  companyId: number;
  canCustomizeInvoiceAppearance: boolean;
  canHideAccountGoBranding: boolean;
  canManageSettings: boolean;
  upgradeAvailable: boolean;
  unavailableReason: string | null;
  hasRetainedSettings: boolean;
  effectiveShowsAccountGoBranding: boolean;
  inactiveReason: string | null;
}

export interface InvoiceBrandingSettings {
  schemaVersion: number;
  requestedPalette: InvoicePdfPalette | null;
  hideAccountGoBranding: boolean;
  revision: number;
  applied: boolean;
  inactiveReason: string | null;
  effectivePalette: InvoicePdfPalette;
  effectiveShowsAccountGoBranding: boolean;
}

export const DEFAULT_INVOICE_PALETTE: InvoicePdfPalette = {
  text: '#0F172A',
  secondaryText: '#475569',
  mutedText: '#64748B',
  border: '#E2E8F0',
  surface: '#F1F5F9',
  accent: '#059669',
  success: '#059669',
  danger: '#E11D48',
  neutral: '#64748B',
};

const PRESETS: Array<{ name: string; palette: InvoicePdfPalette }> = [
  { name: 'AccountGo', palette: DEFAULT_INVOICE_PALETTE },
  {
    name: 'Harbor',
    palette: { ...DEFAULT_INVOICE_PALETTE, accent: '#1D4ED8', success: '#047857' },
  },
  {
    name: 'Forest',
    palette: { ...DEFAULT_INVOICE_PALETTE, accent: '#047857', success: '#047857' },
  },
];

const FIELDS: Array<{ key: keyof InvoicePdfPalette; label: string }> = [
  { key: 'text', label: 'Text' },
  { key: 'secondaryText', label: 'Secondary text' },
  { key: 'mutedText', label: 'Muted text' },
  { key: 'border', label: 'Border' },
  { key: 'surface', label: 'Surface' },
  { key: 'accent', label: 'Accent' },
  { key: 'success', label: 'Success' },
  { key: 'danger', label: 'Danger' },
  { key: 'neutral', label: 'Neutral' },
];

const inputClass = 'w-full rounded-xl bg-[var(--bg-subtle)] px-3 py-2 text-sm text-[var(--text-main)]';

export function InvoicePdfAppearanceEditor({
  capabilities,
  settings,
  disabled,
  onChange,
  onSave,
  onRestore,
  onPreview,
}: {
  capabilities: InvoiceBrandingCapabilities;
  settings: InvoiceBrandingSettings;
  disabled: boolean;
  onChange: (next: InvoiceBrandingSettings) => void;
  onSave: () => void;
  onRestore: () => void;
  onPreview: () => void;
}) {
  const palette = settings.requestedPalette ?? settings.effectivePalette ?? DEFAULT_INVOICE_PALETTE;
  const inactive = capabilities.inactiveReason || settings.inactiveReason;
  return (
    <div className="space-y-4 rounded-2xl border border-[var(--border-subtle)] p-4">
      <div>
        <h3 className="text-sm font-semibold text-[var(--text-main)]">PDF appearance</h3>
        <p className="mt-1 text-[11px] text-[var(--text-muted)]">
          Colors apply to invoice PDFs only. They do not change the app theme or the email sender name.
        </p>
      </div>
      {inactive && (
        <p role="status" className="rounded-xl bg-amber-500/10 p-3 text-xs text-amber-700">
          {inactive}
          {capabilities.upgradeAvailable ? ' An upgrade can turn customization back on.' : ''}
        </p>
      )}
      {!capabilities.canCustomizeInvoiceAppearance && !capabilities.hasRetainedSettings && (
        <p className="text-xs text-[var(--text-muted)]">Invoice PDFs include “Powered by AccountGo.”</p>
      )}
      {(capabilities.canCustomizeInvoiceAppearance || capabilities.hasRetainedSettings) && (
        <>
          <div className="flex flex-wrap gap-2">
            {PRESETS.map((preset) => (
              <button
                key={preset.name}
                type="button"
                className="rounded-xl border border-[var(--border-subtle)] px-3 py-1.5 text-xs"
                disabled={disabled || !capabilities.canManageSettings}
                onClick={() => onChange({ ...settings, requestedPalette: preset.palette })}
              >
                {preset.name}
              </button>
            ))}
          </div>
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
            {FIELDS.map((field) => (
              <label key={field.key} className="text-xs text-[var(--text-secondary)]">
                {field.label}
                <input
                  aria-label={field.label}
                  className={inputClass + ' mt-1 font-mono'}
                  value={palette[field.key]}
                  disabled={disabled || !capabilities.canManageSettings}
                  onChange={(event) => onChange({
                    ...settings,
                    requestedPalette: { ...palette, [field.key]: event.target.value },
                  })}
                />
              </label>
            ))}
          </div>
          <label className="flex items-center gap-2 text-sm text-[var(--text-main)]">
            <input
              type="checkbox"
              checked={settings.hideAccountGoBranding}
              disabled={disabled || !capabilities.canManageSettings || !capabilities.canHideAccountGoBranding}
              onChange={(event) => onChange({ ...settings, hideAccountGoBranding: event.target.checked })}
            />
            Hide AccountGo branding
          </label>
          <p className="text-[11px] text-[var(--text-muted)]">
            Hiding attribution removes only the “Powered by AccountGo” line. Seller details, totals, and page numbers stay.
            {capabilities.effectiveShowsAccountGoBranding ? ' Current PDFs show AccountGo attribution.' : ' Current PDFs hide AccountGo attribution.'}
          </p>
          <div className="flex flex-wrap gap-2">
            <button type="button" className="btn-primary !h-10 !px-4 text-sm" disabled={disabled || !capabilities.canManageSettings} onClick={onSave}>
              <Save className="mr-1 inline h-4 w-4" /> Save PDF appearance
            </button>
            <button type="button" className="rounded-xl border border-[var(--border-subtle)] px-3 py-2 text-xs" disabled={disabled || !capabilities.canManageSettings} onClick={onRestore}>
              <RotateCcw className="mr-1 inline h-3 w-3" /> Restore defaults
            </button>
            <button type="button" className="rounded-xl border border-[var(--border-subtle)] px-3 py-2 text-xs" disabled={disabled} onClick={onPreview}>
              Preview PDF
            </button>
          </div>
        </>
      )}
    </div>
  );
}

/** Loads appearance capabilities and settings for the invoice settings screen. */
export function InvoicePdfAppearanceSection({ canEdit }: { canEdit: boolean }) {
  const { authFetch, completeStepUp } = useAuth();
  const [capabilities, setCapabilities] = useState<InvoiceBrandingCapabilities | null>(null);
  const [settings, setSettings] = useState<InvoiceBrandingSettings | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [needsStepUp, setNeedsStepUp] = useState(false);
  const [code, setCode] = useState('');

  const load = async () => {
    const response = await authFetch('/api/invoice-branding/capabilities');
    if (!response.ok) throw new Error('Invoice appearance settings could not be loaded.');
    const next = await response.json() as InvoiceBrandingCapabilities;
    setCapabilities(next);
    if (next.canCustomizeInvoiceAppearance || next.hasRetainedSettings) {
      const saved = await authFetch('/api/invoice-branding/settings');
      if (saved.ok) setSettings(await saved.json() as InvoiceBrandingSettings);
    } else {
      setSettings(null);
    }
  };

  useEffect(() => {
    let cancelled = false;
    load().catch((err: unknown) => { if (!cancelled) setError(err instanceof Error ? err.message : 'Invoice appearance settings could not be loaded.'); });
    return () => { cancelled = true; };
    // authFetch identity is stable for the signed-in workspace.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [authFetch]);

  const write = async (path: string, body: unknown) => {
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      const response = await authFetch(path, {
        method: path.endsWith('/restore') ? 'POST' : 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });
      const data = await response.json().catch(() => null);
      if (!response.ok) {
        if (response.status === 403 && (!data || data.code === 'mfa_step_up_required')) {
          setNeedsStepUp(true);
          throw new Error('Verify your identity below, then retry this action.');
        }
        throw new Error(data?.message || data?.detail || 'Invoice appearance could not be saved.');
      }
      setSettings(data as InvoiceBrandingSettings);
      await load();
      setNotice('PDF appearance saved. New downloads and signed links use it. Emails already accepted keep their original file.');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Invoice appearance could not be saved.');
    } finally {
      setBusy(false);
    }
  };

  const preview = async () => {
    if (!settings) return;
    setBusy(true);
    setError(null);
    try {
      const response = await authFetch('/api/invoice-branding/preview', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          palette: settings.requestedPalette ?? settings.effectivePalette,
          hideAccountGoBranding: settings.hideAccountGoBranding,
          revision: settings.revision,
        }),
      });
      if (!response.ok) {
        const data = await response.json().catch(() => null);
        throw new Error(data?.message || 'The PDF preview could not be created.');
      }
      const url = URL.createObjectURL(await response.blob());
      window.open(url, '_blank', 'noopener');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'The PDF preview could not be created.');
    } finally {
      setBusy(false);
    }
  };

  const verify = async (passkey: boolean) => {
    setBusy(true);
    setError(null);
    try {
      let endpoint = '/api/auth/mfa/step-up/totp';
      let body: unknown = { code };
      if (passkey) {
        const begin = await authFetch('/api/auth/mfa/step-up/webauthn/options', { method: 'POST' });
        if (!begin.ok) throw new Error('Passkey verification could not be started.');
        body = { assertion: await getPasskeyAssertion(await begin.json()) };
        endpoint = '/api/auth/mfa/step-up/webauthn/complete';
      }
      const response = await authFetch(endpoint, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
      const data = await response.json().catch(() => null);
      if (!response.ok || !data?.accessToken) throw new Error(data?.detail || 'Verification failed.');
      completeStepUp(data.accessToken);
      setNeedsStepUp(false);
      setCode('');
      setNotice('Identity verified. Retry the appearance action.');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Verification failed.');
    } finally {
      setBusy(false);
    }
  };

  if (!capabilities) {
    return error ? <p role="alert" className="text-xs text-rose-500">{error}</p> : null;
  }

  const shown = settings ?? {
    schemaVersion: 1,
    requestedPalette: DEFAULT_INVOICE_PALETTE,
    hideAccountGoBranding: false,
    revision: 0,
    applied: false,
    inactiveReason: null,
    effectivePalette: DEFAULT_INVOICE_PALETTE,
    effectiveShowsAccountGoBranding: true,
  };

  return (
    <div className="space-y-3">
      {error && (
        <p role="alert" className="flex items-center gap-2 text-xs text-rose-500">
          <AlertCircle className="h-4 w-4" /> {error}
        </p>
      )}
      {notice && <p role="status" className="text-xs text-emerald-600">{notice}</p>}
      {needsStepUp && (
        <div className="space-y-2">
          <label className="block text-xs">Authenticator code
            <input
              className={inputClass + ' mt-1'}
              value={code}
              inputMode="numeric"
              autoComplete="one-time-code"
              onChange={(event) => setCode(event.target.value.replace(/\D/g, '').slice(0, 6))}
              onKeyDown={(event) => {
                if (event.key === 'Enter') {
                  event.preventDefault();
                  void verify(false);
                }
              }}
            />
          </label>
          <button type="button" className="rounded-xl border px-3 py-2 text-xs" disabled={busy || code.length !== 6} onClick={() => void verify(false)}>Verify code</button>
          {isWebAuthnSupported() && <button type="button" className="ml-2 rounded-xl border px-3 py-2 text-xs" disabled={busy} onClick={() => void verify(true)}>Use passkey</button>}
        </div>
      )}
      {!capabilities.canCustomizeInvoiceAppearance && capabilities.unavailableReason && !capabilities.hasRetainedSettings && (
        <p className="text-[11px] text-[var(--text-muted)]">{capabilities.unavailableReason}</p>
      )}
      <InvoicePdfAppearanceEditor
        capabilities={capabilities}
        settings={shown}
        disabled={!canEdit || busy}
        onChange={setSettings}
        onSave={() => void write('/api/invoice-branding/settings', {
          palette: (settings ?? shown).requestedPalette ?? DEFAULT_INVOICE_PALETTE,
          hideAccountGoBranding: (settings ?? shown).hideAccountGoBranding,
          revision: (settings ?? shown).revision,
        })}
        onRestore={() => void write('/api/invoice-branding/settings/restore', { revision: (settings ?? shown).revision })}
        onPreview={() => void preview()}
      />
    </div>
  );
}
