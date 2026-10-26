import React, { useState, useEffect } from 'react';
import { AuthResponse, TotpBeginResponse, TotpConfirmResponse } from '../../types/auth';
import { QrCodeSvg } from './QrCodeSvg';
import {
  ShieldCheck,
  Copy,
  Check,
  Download,
  AlertTriangle,
  ArrowRight,
  ArrowLeft,
  Loader2,
  AlertCircle,
  Fingerprint,
  Smartphone,
} from 'lucide-react';
import { createPasskeyCredential, isWebAuthnSupported } from '../../lib/webauthn';

interface MfaEnrollWizardProps {
  /** If provided, enrollment is forced via this enroll-only token */
  enrollToken?: string;
  onSuccess?: (authResponse: AuthResponse) => void;
  onComplete?: () => void;
  onCancel?: () => void;
}

export const MfaEnrollWizard: React.FC<MfaEnrollWizardProps> = ({
  enrollToken,
  onSuccess,
  onComplete,
  onCancel,
}) => {
  const [enrollMethod, setEnrollMethod] = useState<'totp' | 'passkey'>('totp');
  const [step, setStep] = useState<1 | 2 | 3>(1); // 1 = QR/Key or Passkey, 2 = Verify code, 3 = Recovery codes
  const [sharedKey, setSharedKey] = useState<string>('');
  const [otpauthUrl, setOtpauthUrl] = useState<string>('');
  const [confirmCode, setConfirmCode] = useState<string>('');
  const [recoveryCodes, setRecoveryCodes] = useState<string[]>([]);
  const [fullAuthResponse, setFullAuthResponse] = useState<AuthResponse | null>(null);

  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [isSubmitting, setIsSubmitting] = useState<boolean>(false);
  const [copiedKey, setCopiedKey] = useState<boolean>(false);
  const [copiedCodes, setCopiedCodes] = useState<boolean>(false);
  const [acknowledgedCodes, setAcknowledgedCodes] = useState<boolean>(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  // Load TOTP key and URI on mount
  useEffect(() => {
    let mounted = true;
    (async () => {
      try {
        const headers: Record<string, string> = { 'Content-Type': 'application/json' };
        if (enrollToken) {
          headers['Authorization'] = `Bearer ${enrollToken}`;
        }
        const res = await fetch('/api/auth/mfa/totp/begin', {
          method: 'POST',
          headers,
          credentials: 'include',
        });

        if (!res.ok) {
          throw new Error('Failed to initiate authenticator enrollment.');
        }

        const data: TotpBeginResponse = await res.json();
        if (mounted) {
          setSharedKey(data.sharedKey);
          setOtpauthUrl(data.otpauthUrl);
          setIsLoading(false);
        }
      } catch (err: any) {
        if (mounted) {
          setErrorMessage(err.message || 'Unable to setup authenticator.');
          setIsLoading(false);
        }
      }
    })();

    return () => {
      mounted = false;
    };
  }, [enrollToken]);

  const copyKey = async () => {
    try {
      await navigator.clipboard.writeText(sharedKey.replace(/\s+/g, ''));
      setCopiedKey(true);
      setTimeout(() => setCopiedKey(false), 2000);
    } catch {}
  };

  const copyAllCodes = async () => {
    try {
      await navigator.clipboard.writeText(recoveryCodes.join('\n'));
      setCopiedCodes(true);
      setTimeout(() => setCopiedCodes(false), 2000);
    } catch {}
  };

  const downloadCodes = () => {
    const content = `ACCOUNTGO EMERGENCY RECOVERY CODES\nGenerated: ${new Date().toISOString()}\n\nKeep these codes in a safe place. Each code can be used once to access your account if you lose your authenticator device.\n\n${recoveryCodes.join('\n')}\n`;
    const blob = new Blob([content], { type: 'text/plain;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `accountgo-recovery-codes-${new Date().toISOString().slice(0, 10)}.txt`;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
  };

  const handleConfirm = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!confirmCode.trim() || confirmCode.trim().length !== 6 || isSubmitting) return;

    setIsSubmitting(true);
    setErrorMessage(null);

    try {
      const headers: Record<string, string> = { 'Content-Type': 'application/json' };
      if (enrollToken) {
        headers['Authorization'] = `Bearer ${enrollToken}`;
      }

      const res = await fetch('/api/auth/mfa/totp/confirm', {
        method: 'POST',
        headers,
        body: JSON.stringify({ code: confirmCode.trim() }),
        credentials: 'include',
      });

      const data: TotpConfirmResponse = await res.json().catch(() => null);

      if (!res.ok) {
        throw new Error((data as any)?.detail || (data as any)?.title || 'Invalid verification code.');
      }

      setRecoveryCodes(data.recoveryCodes);
      if (data.authResponse) {
        setFullAuthResponse(data.authResponse);
      }
      setStep(3);
    } catch (err: any) {
      setErrorMessage(err.message || 'Confirmation failed. Check the code and try again.');
    } finally {
      setIsSubmitting(false);
    }
  };

  const handlePasskeyEnroll = async () => {
    setIsSubmitting(true);
    setErrorMessage(null);

    try {
      const headers: Record<string, string> = { 'Content-Type': 'application/json' };
      if (enrollToken) {
        headers['Authorization'] = `Bearer ${enrollToken}`;
      }

      const beginRes = await fetch('/api/auth/mfa/webauthn/register/begin', {
        method: 'POST',
        headers,
        credentials: 'include',
      });

      const options = await beginRes.json().catch(() => null);
      if (!beginRes.ok) {
        throw new Error(options?.detail || options?.title || 'Failed to initialize passkey registration.');
      }

      const attestation = await createPasskeyCredential(options);

      const completeRes = await fetch('/api/auth/mfa/webauthn/register/complete', {
        method: 'POST',
        headers,
        body: JSON.stringify({
          attestation,
          nickname: 'Passkey',
        }),
        credentials: 'include',
      });

      const data = await completeRes.json().catch(() => null);
      if (!completeRes.ok) {
        throw new Error(data?.detail || data?.title || 'Passkey registration failed.');
      }

      if (data.recoveryCodes && data.recoveryCodes.length > 0) {
        setRecoveryCodes(data.recoveryCodes);
        if (data.authResponse) {
          setFullAuthResponse(data.authResponse);
        }
        setStep(3);
      } else if (data.authResponse && onSuccess) {
        onSuccess(data.authResponse);
      } else if (onComplete) {
        onComplete();
      }
    } catch (err: any) {
      setErrorMessage(err.message || 'Passkey registration failed.');
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleFinish = () => {
    if (fullAuthResponse && onSuccess) {
      onSuccess(fullAuthResponse);
    } else if (onComplete) {
      onComplete();
    }
  };

  if (isLoading) {
    return (
      <div className="flex flex-col items-center justify-center p-8 space-y-3">
        <Loader2 className="w-8 h-8 text-[var(--brand-primary)] animate-spin" />
        <span className="text-sm text-[var(--text-muted)]">Setting up secure authenticator...</span>
      </div>
    );
  }

  return (
    <div className="space-y-6 animate-fade-in max-w-md mx-auto">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div className="w-12 h-12 rounded-2xl bg-[var(--brand-primary)]/15 ring-1 ring-[var(--brand-ring)]/40 flex items-center justify-center shadow-lg shadow-[var(--brand-primary)]/20">
          <ShieldCheck className="w-6 h-6 text-[var(--brand-primary)]" />
        </div>

        {onCancel && step !== 3 && (
          <button
            type="button"
            onClick={onCancel}
            className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-full text-xs font-semibold bg-[var(--brand-subtle)] text-[var(--brand-primary)] ring-1 ring-[var(--brand-ring)]/30 hover:bg-[var(--brand-subtle)]/80 transition-all cursor-pointer"
          >
            <ArrowLeft className="w-3.5 h-3.5" />
            <span>Cancel</span>
          </button>
        )}
      </div>

      {/* Progress Indicators */}
      <div className="flex items-center gap-2">
        <div className={`h-1.5 flex-1 rounded-full ${step >= 1 ? 'bg-[var(--brand-primary)]' : 'bg-white/10'}`} />
        <div className={`h-1.5 flex-1 rounded-full ${step >= 2 ? 'bg-[var(--brand-primary)]' : 'bg-white/10'}`} />
        <div className={`h-1.5 flex-1 rounded-full ${step >= 3 ? 'bg-[var(--brand-primary)]' : 'bg-white/10'}`} />
      </div>

      {/* Error Alert */}
      {errorMessage && (
        <div
          role="alert"
          aria-live="assertive"
          className="flex items-start gap-2.5 p-3 rounded-xl bg-rose-500/10 text-rose-400 text-xs font-medium ring-1 ring-rose-500/20 animate-fade-in"
        >
          <AlertCircle className="w-4 h-4 flex-shrink-0 mt-0.5" />
          <span className="flex-1">{errorMessage}</span>
        </div>
      )}

      {/* STEP 1: Method selection & Setup */}
      {step === 1 && (
        <div className="space-y-5">
          {/* Method selector tabs */}
          {isWebAuthnSupported() && (
            <div className="grid grid-cols-2 p-1 rounded-xl bg-[var(--bg-canvas)] border border-white/10 text-xs font-semibold">
              <button
                type="button"
                onClick={() => setEnrollMethod('totp')}
                className={`py-2 px-3 rounded-lg flex items-center justify-center gap-1.5 transition-all cursor-pointer ${
                  enrollMethod === 'totp'
                    ? 'bg-[var(--brand-primary)] text-[var(--brand-text)] shadow'
                    : 'text-[var(--text-secondary)] hover:text-[var(--text-main)]'
                }`}
              >
                <Smartphone className="w-3.5 h-3.5" />
                <span>Authenticator App</span>
              </button>
              <button
                type="button"
                onClick={() => setEnrollMethod('passkey')}
                className={`py-2 px-3 rounded-lg flex items-center justify-center gap-1.5 transition-all cursor-pointer ${
                  enrollMethod === 'passkey'
                    ? 'bg-[var(--brand-primary)] text-[var(--brand-text)] shadow'
                    : 'text-[var(--text-secondary)] hover:text-[var(--text-main)]'
                }`}
              >
                <Fingerprint className="w-3.5 h-3.5" />
                <span>Passkey / Key</span>
              </button>
            </div>
          )}

          {enrollMethod === 'passkey' ? (
            <div className="space-y-4">
              <div className="space-y-1.5">
                <h2 className="text-xl sm:text-2xl font-bold tracking-tight text-[var(--text-main)]">
                  Register a Passkey
                </h2>
                <p className="text-xs sm:text-sm text-[var(--text-secondary)] leading-relaxed">
                  Use Touch ID, Face ID, Windows Hello, or a hardware security key (e.g. YubiKey) for fast, phishing-resistant logins.
                </p>
              </div>

              <div className="p-6 rounded-2xl bg-[var(--bg-canvas)] border border-white/10 flex flex-col items-center justify-center text-center space-y-4 shadow-inner">
                <div className="w-16 h-16 rounded-full bg-[var(--brand-primary)]/10 flex items-center justify-center text-[var(--brand-primary)] ring-1 ring-[var(--brand-ring)]/30">
                  <Fingerprint className="w-9 h-9" />
                </div>
                <div className="space-y-1 max-w-xs">
                  <p className="text-sm font-semibold text-[var(--text-main)]">
                    Fast & Phishing-Resistant
                  </p>
                  <p className="text-xs text-[var(--text-muted)]">
                    You won't have to type 6-digit codes. Touch your fingerprint sensor or insert your key when signing in.
                  </p>
                </div>
              </div>

              <button
                type="button"
                onClick={handlePasskeyEnroll}
                disabled={isSubmitting}
                className="w-full py-3.5 px-4 rounded-xl bg-[var(--brand-primary)] text-[var(--brand-text)] font-semibold text-sm flex items-center justify-center gap-2 shadow-lg shadow-[var(--brand-primary)]/25 hover:brightness-110 active:scale-[0.99] disabled:opacity-50 disabled:pointer-events-none transition-all cursor-pointer"
              >
                {isSubmitting ? (
                  <>
                    <Loader2 className="w-4 h-4 animate-spin" />
                    <span>Registering Passkey...</span>
                  </>
                ) : (
                  <>
                    <Fingerprint className="w-4 h-4" />
                    <span>Register Passkey Now</span>
                  </>
                )}
              </button>
            </div>
          ) : (
            <div className="space-y-5">
              <div className="space-y-1.5">
                <h2 className="text-xl sm:text-2xl font-bold tracking-tight text-[var(--text-main)]">
                  Scan QR Code
                </h2>
                <p className="text-xs sm:text-sm text-[var(--text-secondary)] leading-relaxed">
                  Scan this QR code with your authenticator app (such as Google Authenticator, Microsoft Authenticator, or 1Password).
                </p>
              </div>

              <div className="flex flex-col items-center justify-center p-4 rounded-2xl bg-white shadow-lg">
                <QrCodeSvg value={otpauthUrl} size={192} />
              </div>

              <div className="space-y-1.5">
                <span className="text-xs font-semibold text-[var(--text-secondary)] uppercase tracking-wider">
                  Or enter key manually
                </span>
                <div className="flex items-center gap-2">
                  <input
                    type="text"
                    readOnly
                    value={sharedKey}
                    className="w-full px-3.5 py-2.5 rounded-xl bg-[var(--bg-canvas)] text-xs font-mono font-medium text-[var(--text-main)] ring-1 ring-white/10 focus:outline-none select-all"
                  />
                  <button
                    type="button"
                    onClick={copyKey}
                    className="px-3.5 py-2.5 rounded-xl bg-[var(--brand-subtle)] text-[var(--brand-primary)] hover:bg-[var(--brand-subtle)]/80 text-xs font-semibold flex items-center gap-1.5 ring-1 ring-[var(--brand-ring)]/30 transition-all cursor-pointer"
                  >
                    {copiedKey ? <Check className="w-3.5 h-3.5" /> : <Copy className="w-3.5 h-3.5" />}
                    <span>{copiedKey ? 'Copied' : 'Copy'}</span>
                  </button>
                </div>
              </div>

              <button
                type="button"
                onClick={() => setStep(2)}
                className="w-full py-3 px-4 rounded-xl bg-[var(--brand-primary)] text-[var(--brand-text)] font-semibold text-sm flex items-center justify-center gap-2 shadow-lg shadow-[var(--brand-primary)]/25 hover:brightness-110 active:scale-[0.99] transition-all cursor-pointer"
              >
                <span>Next: Verify Code</span>
                <ArrowRight className="w-4 h-4" />
              </button>
            </div>
          )}
        </div>
      )}

      {/* STEP 2: Enter code to confirm */}
      {step === 2 && (
        <form onSubmit={handleConfirm} className="space-y-5">
          <div className="space-y-1.5">
            <h2 className="text-xl sm:text-2xl font-bold tracking-tight text-[var(--text-main)]">
              Verify Authenticator Code
            </h2>
            <p className="text-xs sm:text-sm text-[var(--text-secondary)] leading-relaxed">
              Enter the 6-digit code currently shown in your authenticator app to confirm setup.
            </p>
          </div>

          <div className="space-y-1.5">
            <label
              htmlFor="confirm-code"
              className="text-xs font-semibold text-[var(--text-secondary)] uppercase tracking-wider"
            >
              6-Digit Code
            </label>
            <input
              id="confirm-code"
              type="text"
              autoFocus
              inputMode="numeric"
              pattern="[0-9]*"
              maxLength={6}
              required
              value={confirmCode}
              onChange={(e) => setConfirmCode(e.target.value.replace(/\D/g, '').slice(0, 6))}
              placeholder="123456"
              className="w-full py-3 text-center text-2xl font-bold font-mono tracking-widest rounded-xl bg-[var(--bg-canvas)] text-[var(--text-main)] ring-1 ring-white/10 focus:ring-2 focus:ring-[var(--brand-primary)] focus:outline-none transition-all shadow-inner"
            />
          </div>

          <div className="flex items-center gap-3">
            <button
              type="button"
              onClick={() => setStep(1)}
              className="py-3 px-4 rounded-xl bg-white/5 hover:bg-white/10 text-xs font-semibold text-[var(--text-secondary)] transition-colors cursor-pointer"
            >
              Back
            </button>
            <button
              type="submit"
              disabled={confirmCode.length !== 6 || isSubmitting}
              className="flex-1 py-3 px-4 rounded-xl bg-[var(--brand-primary)] text-[var(--brand-text)] font-semibold text-sm flex items-center justify-center gap-2 shadow-lg shadow-[var(--brand-primary)]/25 hover:brightness-110 active:scale-[0.99] disabled:opacity-50 disabled:pointer-events-none transition-all cursor-pointer"
            >
              {isSubmitting ? (
                <>
                  <Loader2 className="w-4 h-4 animate-spin" />
                  <span>Verifying...</span>
                </>
              ) : (
                <>
                  <span>Confirm & Enable</span>
                  <ArrowRight className="w-4 h-4" />
                </>
              )}
            </button>
          </div>
        </form>
      )}

      {/* STEP 3: Show recovery codes */}
      {step === 3 && (
        <div className="space-y-5">
          <div className="space-y-1.5">
            <h2 className="text-xl sm:text-2xl font-bold tracking-tight text-[var(--text-main)]">
              Save Your Recovery Codes
            </h2>
            <p className="text-xs sm:text-sm text-[var(--text-secondary)] leading-relaxed">
              If you lose access to your phone or authenticator app, these emergency codes are the <strong className="text-[var(--text-main)]">only way</strong> to access your workspace.
            </p>
          </div>

          <div className="p-3 rounded-xl bg-amber-500/10 border border-amber-500/30 text-amber-300 text-xs flex items-start gap-2.5">
            <AlertTriangle className="w-4 h-4 flex-shrink-0 mt-0.5 text-amber-400" />
            <span>
              Each recovery code can only be used once. They will <strong className="text-amber-200">not</strong> be shown again after this step.
            </span>
          </div>

          {/* 10 codes in 2 columns */}
          <div className="p-4 rounded-2xl bg-[var(--bg-canvas)] border border-white/10 grid grid-cols-2 gap-2 text-center font-mono text-sm font-semibold tracking-wider text-[var(--brand-primary)]">
            {recoveryCodes.map((code, idx) => (
              <div key={idx} className="p-1.5 rounded-lg bg-white/5 select-all">
                {code}
              </div>
            ))}
          </div>

          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={copyAllCodes}
              className="flex-1 py-2.5 px-3 rounded-xl bg-white/5 hover:bg-white/10 text-xs font-semibold text-[var(--text-main)] flex items-center justify-center gap-1.5 transition-colors cursor-pointer"
            >
              {copiedCodes ? <Check className="w-3.5 h-3.5 text-emerald-400" /> : <Copy className="w-3.5 h-3.5" />}
              <span>{copiedCodes ? 'Copied All' : 'Copy All Codes'}</span>
            </button>
            <button
              type="button"
              onClick={downloadCodes}
              className="flex-1 py-2.5 px-3 rounded-xl bg-white/5 hover:bg-white/10 text-xs font-semibold text-[var(--text-main)] flex items-center justify-center gap-1.5 transition-colors cursor-pointer"
            >
              <Download className="w-3.5 h-3.5" />
              <span>Download .txt</span>
            </button>
          </div>

          <label className="flex items-start gap-2.5 text-xs text-[var(--text-secondary)] pt-2 cursor-pointer select-none">
            <input
              type="checkbox"
              checked={acknowledgedCodes}
              onChange={(e) => setAcknowledgedCodes(e.target.checked)}
              className="mt-0.5 rounded bg-[var(--bg-canvas)] border-white/20 text-[var(--brand-primary)] focus:ring-[var(--brand-primary)] focus:ring-offset-0"
            />
            <span>I have downloaded or stored these 10 recovery codes in a secure location.</span>
          </label>

          <button
            type="button"
            disabled={!acknowledgedCodes}
            onClick={handleFinish}
            className="w-full py-3 px-4 rounded-xl bg-[var(--brand-primary)] text-[var(--brand-text)] font-semibold text-sm flex items-center justify-center gap-2 shadow-lg shadow-[var(--brand-primary)]/25 hover:brightness-110 active:scale-[0.99] disabled:opacity-50 disabled:pointer-events-none transition-all cursor-pointer"
          >
            <span>Done & Continue</span>
            <ArrowRight className="w-4 h-4" />
          </button>
        </div>
      )}
    </div>
  );
};
