import { useEffect, useMemo, useState } from 'react';
import { AlertCircle, AlertTriangle, Check, Hash, RefreshCw, Save } from 'lucide-react';
import { useAuth } from '../auth/AuthContext';

export interface InvoiceNumberingSettings {
  format: string | null;
  nextNumber: number;
  minNextNumber: number;
  currentFiscalYearCode: string;
  nextFiscalYearCode: string;
  sampleCurrentYear: string;
  sampleNextYear: string;
  hasIssuedInCurrentFiscalYear: boolean;
}

const MAX_INVOICE_NO_LENGTH = 30;
const ALLOWED_INVOICE_NO_REGEX = /^[A-Za-z0-9][A-Za-z0-9\-_/.]*$/;
const TOKEN_REGEX = /\{([^}]*)\}/g;

export function formatInvoiceNumberPreview(
  format: string | null | undefined,
  fiscalYearCode: string,
  sequence: number,
): { valid: boolean; preview: string; error: string | null } {
  const trimmed = (format ?? '').trim();
  if (!trimmed) {
    return { valid: true, preview: String(sequence), error: null };
  }

  let hasSequenceToken = false;
  let seqWidth = 0;
  let tokenError: string | null = null;

  // Check for unmatched braces outside valid {...} tokens
  const stripped = trimmed.replace(TOKEN_REGEX, (_, inner: string) => {
    if (inner === 'FY') {
      return fiscalYearCode || '2026';
    }
    if (/^#{1,10}$/.test(inner)) {
      if (hasSequenceToken) {
        tokenError = 'Use only one sequence token (such as {####}).';
        return '';
      }
      hasSequenceToken = true;
      seqWidth = inner.length;
      return String(sequence).padStart(seqWidth, '0');
    }
    tokenError = `Unknown token {${inner}}. Only {FY} and {####} (1–10 #s) are supported.`;
    return '';
  });

  if (tokenError) {
    return { valid: false, preview: '', error: tokenError };
  }
  if (stripped.includes('{') || stripped.includes('}')) {
    return { valid: false, preview: '', error: 'Unmatched brace in format.' };
  }
  if (!hasSequenceToken) {
    return { valid: false, preview: '', error: 'Format must include a sequence token such as {####}.' };
  }
  if (stripped.length > MAX_INVOICE_NO_LENGTH) {
    return {
      valid: false,
      preview: stripped,
      error: `Formatted number "${stripped}" exceeds ${MAX_INVOICE_NO_LENGTH} characters.`,
    };
  }
  if (!ALLOWED_INVOICE_NO_REGEX.test(stripped)) {
    return {
      valid: false,
      preview: stripped,
      error: 'Numbers must start with a letter or digit and use only letters, digits, -, _, /, or .',
    };
  }

  return { valid: true, preview: stripped, error: null };
}

interface InvoiceNumberingSectionProps {
  canEdit: boolean;
}

export const InvoiceNumberingSection: React.FC<InvoiceNumberingSectionProps> = ({ canEdit }) => {
  const { authFetch } = useAuth();
  const [settings, setSettings] = useState<InvoiceNumberingSettings | null>(null);
  const [initialFormat, setInitialFormat] = useState<string>('');
  const [formatInput, setFormatInput] = useState<string>('');
  const [nextNumberInput, setNextNumberInput] = useState<string>('1');
  const [loading, setLoading] = useState<boolean>(true);
  const [saving, setSaving] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState<boolean>(false);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      setLoading(true);
      setError(null);
      try {
        const res = await authFetch('/api/organizations/current/sales-invoice-numbering');
        if (!res.ok) {
          throw new Error('Could not load invoice numbering settings.');
        }
        const data: InvoiceNumberingSettings = await res.json();
        if (cancelled) return;
        setSettings(data);
        const fmt = data.format ?? '';
        setInitialFormat(fmt);
        setFormatInput(fmt);
        setNextNumberInput(String(data.nextNumber ?? 1));
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : 'Could not load invoice numbering settings.');
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [authFetch]);

  const parsedNextNumber = useMemo(() => {
    const trimmed = nextNumberInput.trim();
    if (!/^\d+$/.test(trimmed)) return NaN;
    return Number.parseInt(trimmed, 10);
  }, [nextNumberInput]);

  const minNextNumber = settings?.minNextNumber ?? 1;

  const nextNumberError = useMemo(() => {
    if (!Number.isInteger(parsedNextNumber) || parsedNextNumber < 1) {
      return 'Next number must be a whole number of at least 1.';
    }
    if (parsedNextNumber < minNextNumber) {
      return `Next number cannot be lower than ${minNextNumber} (forward-only).`;
    }
    return null;
  }, [parsedNextNumber, minNextNumber]);

  const currentYearCode = settings?.currentFiscalYearCode || String(new Date().getFullYear());
  const nextYearCode = settings?.nextFiscalYearCode || String(new Date().getFullYear() + 1);
  const seqForSample = Number.isInteger(parsedNextNumber) && parsedNextNumber >= 1 ? parsedNextNumber : 1;

  const currentSample = useMemo(
    () => formatInvoiceNumberPreview(formatInput, currentYearCode, seqForSample),
    [formatInput, currentYearCode, seqForSample],
  );
  const nextYearSample = useMemo(
    () => formatInvoiceNumberPreview(formatInput, nextYearCode, 1),
    [formatInput, nextYearCode],
  );

  const normalizedFormat = formatInput.trim();
  const formatChanged = normalizedFormat !== initialFormat.trim();
  const showIssuedWarning = Boolean(settings?.hasIssuedInCurrentFiscalYear && formatChanged);
  const validationError = currentSample.error ?? nextNumberError;
  const disabled = !canEdit || loading || saving;

  const handleSave = async () => {
    if (!canEdit || validationError) return;
    setSaving(true);
    setError(null);
    setSaved(false);
    try {
      const res = await authFetch('/api/organizations/current/sales-invoice-numbering', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          format: normalizedFormat || null,
          nextNumber: parsedNextNumber,
        }),
      });
      const data = await res.json().catch(() => null);
      if (!res.ok) {
        if (typeof data?.minNextNumber === 'number') {
          setSettings((prev) => (prev ? { ...prev, minNextNumber: data.minNextNumber } : prev));
        }
        throw new Error(data?.detail || data?.message || 'Could not save invoice numbering settings.');
      }
      const updated = data as InvoiceNumberingSettings;
      setSettings(updated);
      const fmt = updated.format ?? '';
      setInitialFormat(fmt);
      setFormatInput(fmt);
      setNextNumberInput(String(updated.nextNumber));
      setSaved(true);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not save invoice numbering settings.');
    } finally {
      setSaving(false);
    }
  };

  return (
    <section aria-labelledby="invoice-numbering-heading" className="pt-5 border-t border-[var(--border-subtle)] space-y-4">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2.5">
          <Hash className="w-4 h-4 text-[var(--brand-primary)]" />
          <div>
            <h3 id="invoice-numbering-heading" className="text-sm font-bold text-[var(--text-main)]">
              Numbering
            </h3>
            <p className="text-xs text-[var(--text-muted)]">
              Leave format blank for continuous numbering (1, 2, 3…), or use a fiscal-year pattern.
            </p>
          </div>
        </div>
        {loading && <RefreshCw className="w-4 h-4 text-[var(--brand-primary)] animate-spin" />}
      </div>

      {error && (
        <div role="alert" className="p-3 rounded-xl bg-rose-500/10 border border-rose-500/20 flex items-start gap-2.5 text-rose-400 text-xs">
          <AlertCircle className="w-4 h-4 shrink-0 mt-0.5" />
          <span>{error}</span>
        </div>
      )}

      {saved && (
        <div role="status" className="p-3 rounded-xl bg-emerald-500/10 border border-emerald-500/20 flex items-center gap-2.5 text-emerald-400 text-xs">
          <Check className="w-4 h-4 shrink-0" />
          <span>Invoice numbering settings saved.</span>
        </div>
      )}

      <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
        <div className="space-y-1.5">
          <label htmlFor="invoice-number-format" className="block text-xs font-semibold text-[var(--text-secondary)]">
            Format pattern
          </label>
          <input
            id="invoice-number-format"
            type="text"
            value={formatInput}
            onChange={(e) => {
              setFormatInput(e.target.value);
              setSaved(false);
            }}
            disabled={disabled}
            maxLength={40}
            placeholder="e.g. INV-{FY}-{####}"
            aria-describedby="invoice-number-format-help"
            className="w-full h-11 px-3 rounded-xl bg-[var(--bg-subtle)] text-sm text-[var(--text-main)] disabled:opacity-60 font-mono"
          />
          <p id="invoice-number-format-help" className="text-[11px] text-[var(--text-muted)]">
            Tokens: <code className="font-mono text-[var(--text-secondary)]">{'{FY}'}</code> (fiscal year code, e.g.{' '}
            {currentYearCode}) and <code className="font-mono text-[var(--text-secondary)]">{'{####}'}</code> (zero-padded sequence).
          </p>
        </div>

        <div className="space-y-1.5">
          <label htmlFor="invoice-next-number" className="block text-xs font-semibold text-[var(--text-secondary)]">
            Next number
          </label>
          <input
            id="invoice-next-number"
            type="number"
            min={minNextNumber}
            step={1}
            value={nextNumberInput}
            onChange={(e) => {
              setNextNumberInput(e.target.value);
              setSaved(false);
            }}
            disabled={disabled}
            aria-describedby="invoice-next-number-help"
            className="w-full h-11 px-3 rounded-xl bg-[var(--bg-subtle)] text-sm text-[var(--text-main)] disabled:opacity-60 font-mono"
          />
          <p id="invoice-next-number-help" className="text-[11px] text-[var(--text-muted)]">
            Forward-only (minimum {minNextNumber}). Sequence resets to 1 each new fiscal year when a format is set.
          </p>
        </div>
      </div>

      {validationError && (
        <p role="alert" className="text-xs text-rose-400 flex items-center gap-1.5">
          <AlertCircle className="w-3.5 h-3.5 shrink-0" />
          <span>{validationError}</span>
        </p>
      )}

      {showIssuedWarning && (
        <div role="status" className="p-3 rounded-xl bg-amber-500/10 border border-amber-500/25 flex items-start gap-2.5 text-amber-300 text-xs">
          <AlertTriangle className="w-4 h-4 shrink-0 mt-0.5" />
          <span>
            This fiscal year already has issued invoices. Changing the format applies only to newly issued invoices; existing invoices keep their numbers.
          </span>
        </div>
      )}

      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
        <div className="p-3 rounded-xl bg-[var(--bg-subtle)]">
          <span className="block text-[11px] text-[var(--text-muted)]">Current fiscal year ({currentYearCode})</span>
          <span data-testid="numbering-preview-current" className="block mt-0.5 text-sm font-mono font-semibold text-[var(--text-main)]">
            {currentSample.valid ? currentSample.preview : '—'}
          </span>
        </div>
        <div className="p-3 rounded-xl bg-[var(--bg-subtle)]">
          <span className="block text-[11px] text-[var(--text-muted)]">Next fiscal year ({nextYearCode})</span>
          <span data-testid="numbering-preview-next" className="block mt-0.5 text-sm font-mono font-semibold text-[var(--text-main)]">
            {nextYearSample.valid ? nextYearSample.preview : '—'}
          </span>
        </div>
      </div>

      {canEdit && (
        <div className="flex items-center justify-end">
          <button
            type="button"
            onClick={handleSave}
            disabled={disabled || Boolean(validationError)}
            className="btn-secondary !h-10 !px-4 text-xs flex items-center gap-2 disabled:opacity-50"
          >
            {saving ? <RefreshCw className="w-3.5 h-3.5 animate-spin" /> : <Save className="w-3.5 h-3.5" />}
            <span>{saving ? 'Saving…' : 'Save Numbering'}</span>
          </button>
        </div>
      )}
    </section>
  );
};
