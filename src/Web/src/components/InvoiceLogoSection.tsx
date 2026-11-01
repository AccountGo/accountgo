import { useEffect, useRef, useState } from 'react';
import { AlertCircle, Image as ImageIcon, Trash2, Upload } from 'lucide-react';
import { useAuth } from '../auth/AuthContext';
import { getPasskeyAssertion, isWebAuthnSupported } from '../lib/webauthn';

export interface InvoiceLogoLimits {
  maxUploadBytes: number;
  maxStoredBytes: number;
  maxDimensionPixels: number;
  maxTotalPixels: number;
  maxNormalizedDimension: number;
}

export interface InvoiceLogoMetadata {
  hasLogo: boolean;
  revision: number;
  hash: string | null;
  width: number | null;
  height: number | null;
  byteCount: number | null;
  limits: InvoiceLogoLimits;
}

const inputClass = 'w-full rounded-xl bg-[var(--bg-subtle)] px-3 py-2 text-sm text-[var(--text-main)]';

export function InvoiceLogoSection({
  canManage,
  disabled = false,
}: {
  canManage: boolean;
  disabled?: boolean;
}) {
  const { authFetch, completeStepUp } = useAuth();
  const [logo, setLogo] = useState<InvoiceLogoMetadata | null>(null);
  const [imageUrl, setImageUrl] = useState<string | null>(null);
  const [localFile, setLocalFile] = useState<File | null>(null);
  const [localPreviewUrl, setLocalPreviewUrl] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [needsStepUp, setNeedsStepUp] = useState(false);
  const [code, setCode] = useState('');
  const fileInputRef = useRef<HTMLInputElement>(null);

  // Manage object URLs cleanly
  const activeImageUrlRef = useRef<string | null>(null);
  const activeLocalPreviewRef = useRef<string | null>(null);

  const setSavedImageUrl = (url: string | null) => {
    if (activeImageUrlRef.current && activeImageUrlRef.current !== url) {
      URL.revokeObjectURL(activeImageUrlRef.current);
    }
    activeImageUrlRef.current = url;
    setImageUrl(url);
  };

  const setLocalPreview = (url: string | null) => {
    if (activeLocalPreviewRef.current && activeLocalPreviewRef.current !== url) {
      URL.revokeObjectURL(activeLocalPreviewRef.current);
    }
    activeLocalPreviewRef.current = url;
    setLocalPreviewUrl(url);
  };

  const loadLogo = async (cancelledRef: { current: boolean }) => {
    try {
      const res = await authFetch('/api/invoice-branding/logo');
      if (!res.ok) {
        if (!cancelledRef.current) setLogo(null);
        return;
      }
      const data = (await res.json()) as InvoiceLogoMetadata;
      if (cancelledRef.current) return;
      setLogo(data);

      if (data.hasLogo) {
        const imgRes = await authFetch('/api/invoice-branding/logo/image');
        if (cancelledRef.current) return;
        if (imgRes.ok) {
          const blob = await imgRes.blob();
          if (cancelledRef.current) return;
          const objectUrl = URL.createObjectURL(blob);
          setSavedImageUrl(objectUrl);
        } else {
          setSavedImageUrl(null);
        }
      } else {
        setSavedImageUrl(null);
      }
    } catch (err) {
      if (!cancelledRef.current) {
        setError(err instanceof Error ? err.message : 'Failed to load logo.');
      }
    }
  };

  useEffect(() => {
    const cancelledRef = { current: false };
    // Clear state when workspace/authFetch changes
    setSavedImageUrl(null);
    setLocalPreview(null);
    setLocalFile(null);
    if (fileInputRef.current) fileInputRef.current.value = '';
    setError(null);
    setNotice(null);

    void loadLogo(cancelledRef);

    return () => {
      cancelledRef.current = true;
      if (activeImageUrlRef.current) {
        URL.revokeObjectURL(activeImageUrlRef.current);
        activeImageUrlRef.current = null;
      }
      if (activeLocalPreviewRef.current) {
        URL.revokeObjectURL(activeLocalPreviewRef.current);
        activeLocalPreviewRef.current = null;
      }
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [authFetch]);

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    setError(null);
    setNotice(null);
    const files = e.target.files;
    if (!files || files.length === 0) {
      setLocalFile(null);
      setLocalPreview(null);
      return;
    }

    const file = files[0];
    if (file.type !== 'image/png' && file.type !== 'image/jpeg') {
      setError('Only PNG and JPEG images are allowed.');
      e.target.value = '';
      setLocalFile(null);
      setLocalPreview(null);
      return;
    }

    if (file.size > 2 * 1024 * 1024) {
      setError('The selected file exceeds the 2 MB limit.');
      e.target.value = '';
      setLocalFile(null);
      setLocalPreview(null);
      return;
    }

    setLocalFile(file);
    const previewUrl = URL.createObjectURL(file);
    setLocalPreview(previewUrl);
  };

  const handleUpload = async () => {
    if (!localFile) return;
    setBusy(true);
    setError(null);
    setNotice(null);

    try {
      const formData = new FormData();
      formData.append('file', localFile);
      formData.append('expectedRevision', String(logo?.revision ?? 0));

      const res = await authFetch('/api/invoice-branding/logo', {
        method: 'PUT',
        body: formData,
      });

      const data = await res.json().catch(() => null);

      if (!res.ok) {
        if (res.status === 403 && (!data || data.code === 'mfa_step_up_required')) {
          setNeedsStepUp(true);
          throw new Error('Verify your identity below, then retry saving your logo.');
        }
        if (res.status === 409) {
          await loadLogo({ current: false });
          throw new Error(data?.message || 'Invoice logo was updated by someone else. Reloaded latest state.');
        }
        throw new Error(data?.message || 'Failed to upload logo.');
      }

      setLocalPreview(null);
      setLocalFile(null);
      if (fileInputRef.current) fileInputRef.current.value = '';
      await loadLogo({ current: false });
      setNotice('Invoice logo saved. Newly generated invoice PDFs will display it.');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to upload logo.');
    } finally {
      setBusy(false);
    }
  };

  const handleRemove = async () => {
    setBusy(true);
    setError(null);
    setNotice(null);

    try {
      const revision = logo?.revision ?? 0;
      const res = await authFetch(`/api/invoice-branding/logo?revision=${revision}`, {
        method: 'DELETE',
      });

      const data = await res.json().catch(() => null);

      if (!res.ok) {
        if (res.status === 403 && (!data || data.code === 'mfa_step_up_required')) {
          setNeedsStepUp(true);
          throw new Error('Verify your identity below, then retry removing your logo.');
        }
        if (res.status === 409) {
          await loadLogo({ current: false });
          throw new Error(data?.message || 'Invoice logo was updated by someone else. Reloaded latest state.');
        }
        throw new Error(data?.message || 'Failed to remove logo.');
      }

      setSavedImageUrl(null);
      setLocalPreview(null);
      setLocalFile(null);
      if (fileInputRef.current) fileInputRef.current.value = '';
      await loadLogo({ current: false });
      setNotice('Invoice logo removed. Newly generated invoice PDFs will not include a logo.');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to remove logo.');
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
      const res = await authFetch(endpoint, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });
      const data = await res.json().catch(() => null);
      if (!res.ok || !data?.accessToken) throw new Error(data?.detail || 'Verification failed.');
      completeStepUp(data.accessToken);
      setNeedsStepUp(false);
      setCode('');
      setNotice('Identity verified. Please retry your logo action now.');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Step-up verification failed.');
    } finally {
      setBusy(false);
    }
  };

  const displayedImage = localPreviewUrl || imageUrl;

  return (
    <div className="space-y-4 rounded-2xl border border-[var(--border-subtle)] p-4">
      <div>
        <h4 className="text-sm font-semibold text-[var(--text-main)] flex items-center gap-2">
          <ImageIcon className="h-4 w-4" /> Company logo
        </h4>
        <p className="text-xs text-[var(--text-muted)] mt-1">
          Appears above your seller information in the header of invoice PDFs. Changes apply to future invoice PDF generation.
        </p>
      </div>

      {error && (
        <p role="alert" className="flex items-center gap-2 text-xs text-rose-500">
          <AlertCircle className="h-4 w-4 shrink-0" /> {error}
        </p>
      )}
      {notice && <p role="status" className="text-xs text-emerald-600">{notice}</p>}

      {needsStepUp && (
        <div className="space-y-2 rounded-xl bg-[var(--bg-subtle)] p-3">
          <label className="block text-xs font-medium">Authenticator code
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
          <div className="flex gap-2">
            <button
              type="button"
              className="rounded-xl border border-[var(--border-subtle)] px-3 py-1.5 text-xs"
              disabled={busy || code.length !== 6}
              onClick={() => void verify(false)}
            >
              Verify code
            </button>
            {isWebAuthnSupported() && (
              <button
                type="button"
                className="rounded-xl border border-[var(--border-subtle)] px-3 py-1.5 text-xs"
                disabled={busy}
                onClick={() => void verify(true)}
              >
                Use passkey
              </button>
            )}
          </div>
        </div>
      )}

      <div className="flex flex-col sm:flex-row items-start sm:items-center gap-4">
        <div className="flex items-center justify-center w-36 h-20 rounded-xl border border-dashed border-[var(--border-subtle)] bg-[var(--bg-subtle)] overflow-hidden p-2">
          {displayedImage ? (
            <img
              src={displayedImage}
              alt="Invoice logo preview"
              className="max-h-full max-w-full object-contain"
            />
          ) : (
            <span className="text-[11px] text-[var(--text-muted)] text-center">No logo</span>
          )}
        </div>

        <div className="flex-1 space-y-2">
          {localPreviewUrl && (
            <p className="text-xs text-amber-600 font-medium">
              Local preview (not yet saved). Click &quot;{logo?.hasLogo ? 'Replace logo' : 'Upload logo'}&quot; to save.
            </p>
          )}

          {canManage ? (
            <div className="space-y-2">
              <input
                ref={fileInputRef}
                type="file"
                accept="image/png,image/jpeg"
                disabled={disabled || busy}
                onChange={handleFileChange}
                className="block w-full text-xs text-[var(--text-muted)] file:mr-3 file:py-1.5 file:px-3 file:rounded-xl file:border-0 file:text-xs file:font-semibold file:bg-[var(--bg-main)] file:text-[var(--text-main)] hover:file:bg-[var(--bg-subtle)]"
              />
              <p className="text-[11px] text-[var(--text-muted)]">
                PNG or JPEG, up to 2 MB. Scaled to fit within 140 &times; 56 pt on invoices.
              </p>

              <div className="flex flex-wrap gap-2 pt-1">
                {localFile && (
                  <button
                    type="button"
                    className="btn-primary !h-8 !px-3 text-xs"
                    disabled={disabled || busy}
                    onClick={() => void handleUpload()}
                  >
                    <Upload className="mr-1 inline h-3.5 w-3.5" />
                    {logo?.hasLogo ? 'Replace logo' : 'Upload logo'}
                  </button>
                )}

                {logo?.hasLogo && !localFile && (
                  <button
                    type="button"
                    className="rounded-xl border border-rose-300 px-3 py-1.5 text-xs text-rose-600 hover:bg-rose-50 dark:border-rose-800 dark:hover:bg-rose-950"
                    disabled={disabled || busy}
                    onClick={() => void handleRemove()}
                  >
                    <Trash2 className="mr-1 inline h-3.5 w-3.5" />
                    Remove logo
                  </button>
                )}
              </div>
            </div>
          ) : (
            <p className="text-xs text-[var(--text-muted)]">
              {logo?.hasLogo ? 'An invoice logo is active.' : 'No invoice logo is configured.'} Only organization owners and admins can upload or remove the logo.
            </p>
          )}
        </div>
      </div>
    </div>
  );
}
