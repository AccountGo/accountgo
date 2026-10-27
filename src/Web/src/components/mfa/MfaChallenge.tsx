import React, { useState, useRef, useEffect } from 'react';
import { AuthResponse } from '../../types/auth';
import { ShieldCheck, Key, ArrowRight, ArrowLeft, Loader2, AlertCircle, HelpCircle, Fingerprint } from 'lucide-react';
import { getPasskeyAssertion, isWebAuthnSupported } from '../../lib/webauthn';

interface MfaChallengeProps {
  mfaToken: string;
  methods: string[];
  onSuccess: (authResponse: AuthResponse) => void;
  onCancel: () => void;
}

export const MfaChallenge: React.FC<MfaChallengeProps> = ({
  mfaToken,
  methods,
  onSuccess,
  onCancel,
}) => {
  const hasWebAuthn = methods.includes('webauthn') && isWebAuthnSupported();
  const [mode, setMode] = useState<'webauthn' | 'totp' | 'recovery'>(
    hasWebAuthn ? 'webauthn' : 'totp'
  );
  const [totpDigits, setTotpDigits] = useState<string[]>(['', '', '', '', '', '']);
  const [recoveryCode, setRecoveryCode] = useState<string>('');
  const [rememberDevice, setRememberDevice] = useState<boolean>(false);
  const [isSubmitting, setIsSubmitting] = useState<boolean>(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const inputRefs = useRef<(HTMLInputElement | null)[]>([]);

  useEffect(() => {
    if (mode === 'totp') {
      setTimeout(() => {
        inputRefs.current[0]?.focus();
      }, 100);
    }
  }, [mode]);

  const submitPasskey = async () => {
    setIsSubmitting(true);
    setErrorMessage(null);

    try {
      const beginRes = await fetch('/api/auth/mfa/webauthn/assertion/options', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${mfaToken}`,
        },
      });

      const options = await beginRes.json().catch(() => null);
      if (!beginRes.ok) {
        throw new Error(options?.detail || options?.title || 'Failed to initialize passkey challenge.');
      }

      const assertion = await getPasskeyAssertion(options);

      const completeRes = await fetch('/api/auth/mfa/webauthn/assertion/complete', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${mfaToken}`,
        },
        body: JSON.stringify({
          assertion,
          rememberDevice,
        }),
        credentials: 'include',
      });

      const data = await completeRes.json().catch(() => null);
      if (!completeRes.ok) {
        throw new Error(data?.detail || data?.title || 'Passkey verification failed.');
      }

      onSuccess(data as AuthResponse);
    } catch (err: any) {
      setErrorMessage(err.message || 'Passkey verification failed.');
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleTotpChange = (index: number, val: string) => {
    const numericVal = val.replace(/\D/g, '');
    if (!numericVal && val !== '') return;

    const next = [...totpDigits];
    next[index] = numericVal.slice(-1);
    setTotpDigits(next);

    if (numericVal && index < 5) {
      inputRefs.current[index + 1]?.focus();
    }

    if (numericVal && index === 5) {
      const fullCode = next.join('');
      if (fullCode.length === 6) {
        submitTotp(fullCode);
      }
    }
  };

  const handleKeyDown = (index: number, e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Backspace' && !totpDigits[index] && index > 0) {
      inputRefs.current[index - 1]?.focus();
    }
  };

  const handlePaste = (e: React.ClipboardEvent) => {
    e.preventDefault();
    const pasteData = e.clipboardData.getData('text').trim();

    if (mode === 'totp') {
      const numeric = pasteData.replace(/\D/g, '').slice(0, 6);
      if (!numeric) return;
      const chars = numeric.split('');
      const next = ['', '', '', '', '', ''];
      chars.forEach((c, i) => {
        if (i < 6) next[i] = c;
      });
      setTotpDigits(next);
      if (numeric.length === 6) {
        submitTotp(numeric);
      } else {
        inputRefs.current[Math.min(numeric.length, 5)]?.focus();
      }
    } else {
      setRecoveryCode(pasteData);
    }
  };

  const submitTotp = async (codeToVerify?: string) => {
    const code = codeToVerify || totpDigits.join('');
    if (code.length !== 6 || isSubmitting) return;

    setIsSubmitting(true);
    setErrorMessage(null);

    try {
      const res = await fetch('/api/auth/mfa/totp/verify', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${mfaToken}`,
        },
        body: JSON.stringify({
          code,
          rememberDevice,
        }),
        credentials: 'include',
      });

      const data = await res.json().catch(() => null);

      if (!res.ok) {
        throw new Error(data?.detail || data?.title || 'Invalid verification code.');
      }

      onSuccess(data as AuthResponse);
    } catch (err: any) {
      setErrorMessage(err.message || 'Verification failed. Please try again.');
    } finally {
      setIsSubmitting(false);
    }
  };

  const submitRecovery = async (e?: React.FormEvent) => {
    if (e) e.preventDefault();
    if (!recoveryCode.trim() || isSubmitting) return;

    setIsSubmitting(true);
    setErrorMessage(null);

    try {
      const res = await fetch('/api/auth/mfa/recovery/verify', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${mfaToken}`,
        },
        body: JSON.stringify({
          code: recoveryCode.trim(),
        }),
        credentials: 'include',
      });

      const data = await res.json().catch(() => null);

      if (!res.ok) {
        throw new Error(data?.detail || data?.title || 'Invalid or already used recovery code.');
      }

      onSuccess(data as AuthResponse);
    } catch (err: any) {
      setErrorMessage(err.message || 'Recovery failed. Please check the code.');
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="space-y-6 animate-fade-in" onPaste={handlePaste}>
      {/* Header */}
      <div className="flex items-center justify-between">
        <div className="w-14 h-14 rounded-2xl bg-[var(--brand-primary)]/15 ring-1 ring-[var(--brand-ring)]/40 flex items-center justify-center shadow-lg shadow-[var(--brand-primary)]/20">
          <ShieldCheck className="w-8 h-8 text-[var(--brand-primary)]" />
        </div>

        <button
          type="button"
          onClick={onCancel}
          className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-full text-xs font-semibold bg-[var(--brand-subtle)] text-[var(--brand-primary)] ring-1 ring-[var(--brand-ring)]/30 hover:bg-[var(--brand-subtle)]/80 transition-all cursor-pointer"
        >
          <ArrowLeft className="w-3.5 h-3.5" />
          <span>Back</span>
        </button>
      </div>

      <div className="space-y-1.5">
        <h1 className="text-2xl sm:text-3xl font-bold tracking-tight text-[var(--text-main)]">
          {mode === 'webauthn'
            ? 'Sign in with Passkey'
            : mode === 'totp'
            ? 'Two-Factor Authentication'
            : 'Enter Recovery Code'}
        </h1>
        <p className="text-sm text-[var(--text-secondary)] leading-relaxed">
          {mode === 'webauthn'
            ? 'Use your device biometric sensor, Touch ID, Windows Hello, or hardware security key to sign in.'
            : mode === 'totp'
            ? 'Open your authenticator app (Google Authenticator, 1Password, etc.) and enter the 6-digit code.'
            : 'Enter one of your 10-character emergency recovery codes (e.g. ABCD-EFGH).'}
        </p>
      </div>

      {/* Error Banner */}
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

      {/* WebAuthn Passkey Mode */}
      {mode === 'webauthn' && (
        <div className="space-y-4">
          <div className="p-6 rounded-2xl bg-[var(--bg-canvas)] border border-white/10 flex flex-col items-center justify-center text-center space-y-4 shadow-inner">
            <div className="w-16 h-16 rounded-full bg-[var(--brand-primary)]/10 flex items-center justify-center text-[var(--brand-primary)] ring-1 ring-[var(--brand-ring)]/30">
              <Fingerprint className="w-9 h-9" />
            </div>
            <div className="space-y-1 max-w-xs">
              <p className="text-sm font-semibold text-[var(--text-main)]">
                Ready for Passkey Verification
              </p>
              <p className="text-xs text-[var(--text-muted)]">
                Touch your security key or activate biometric authentication when prompted by your browser.
              </p>
            </div>
          </div>

          <label className="flex items-center gap-2.5 text-xs text-[var(--text-secondary)] pt-1 cursor-pointer select-none">
            <input
              type="checkbox"
              checked={rememberDevice}
              onChange={(e) => setRememberDevice(e.target.checked)}
              className="rounded bg-[var(--bg-canvas)] border-white/20 text-[var(--brand-primary)] focus:ring-[var(--brand-primary)] focus:ring-offset-0"
            />
            <span>Remember this device for 30 days</span>
          </label>

          <button
            type="button"
            onClick={submitPasskey}
            disabled={isSubmitting}
            className="w-full py-3.5 px-4 rounded-xl bg-[var(--brand-primary)] text-[var(--brand-text)] font-semibold text-sm flex items-center justify-center gap-2 shadow-lg shadow-[var(--brand-primary)]/25 hover:brightness-110 active:scale-[0.99] disabled:opacity-50 disabled:pointer-events-none transition-all cursor-pointer"
          >
            {isSubmitting ? (
              <>
                <Loader2 className="w-4 h-4 animate-spin" />
                <span>Waiting for Passkey...</span>
              </>
            ) : (
              <>
                <Fingerprint className="w-4 h-4" />
                <span>Verify with Passkey</span>
              </>
            )}
          </button>

          <div className="flex flex-col gap-2 pt-2 text-center text-xs">
            {methods.includes('totp') && (
              <button
                type="button"
                onClick={() => {
                  setMode('totp');
                  setErrorMessage(null);
                }}
                className="text-[var(--text-secondary)] hover:text-[var(--brand-primary)] transition-colors cursor-pointer"
              >
                Use an authenticator code instead
              </button>
            )}
            {methods.includes('recovery') && (
              <button
                type="button"
                onClick={() => {
                  setMode('recovery');
                  setErrorMessage(null);
                }}
                className="text-[var(--text-secondary)] hover:text-[var(--brand-primary)] transition-colors cursor-pointer"
              >
                Use an emergency recovery code
              </button>
            )}
          </div>
        </div>
      )}

      {/* TOTP Mode */}
      {mode === 'totp' && (
        <div className="space-y-4">
          <div className="grid grid-cols-6 gap-2 sm:gap-2.5">
            {totpDigits.map((digit, idx) => (
              <input
                key={idx}
                ref={(el) => {
                  inputRefs.current[idx] = el;
                }}
                type="text"
                inputMode="numeric"
                autoComplete="one-time-code"
                pattern="[0-9]*"
                maxLength={1}
                value={digit}
                onChange={(e) => handleTotpChange(idx, e.target.value)}
                onKeyDown={(e) => handleKeyDown(idx, e)}
                disabled={isSubmitting}
                aria-label={`Authenticator code digit ${idx + 1} of 6`}
                className="w-full aspect-[4/5] sm:h-16 text-center text-xl sm:text-2xl font-bold font-mono rounded-xl sm:rounded-2xl bg-[var(--bg-canvas)] text-[var(--text-main)] ring-1 ring-white/10 focus:ring-2 focus:ring-[var(--brand-primary)] focus:outline-none transition-all shadow-inner caret-[var(--brand-primary)] disabled:opacity-50"
              />
            ))}
          </div>

          <label className="flex items-center gap-2.5 text-xs text-[var(--text-secondary)] pt-1 cursor-pointer select-none">
            <input
              type="checkbox"
              checked={rememberDevice}
              onChange={(e) => setRememberDevice(e.target.checked)}
              className="rounded bg-[var(--bg-canvas)] border-white/20 text-[var(--brand-primary)] focus:ring-[var(--brand-primary)] focus:ring-offset-0"
            />
            <span>Remember this device for 30 days</span>
          </label>

          <button
            type="button"
            onClick={() => submitTotp()}
            disabled={totpDigits.join('').length !== 6 || isSubmitting}
            className="w-full py-3.5 px-4 rounded-xl bg-[var(--brand-primary)] text-[var(--brand-text)] font-semibold text-sm flex items-center justify-center gap-2 shadow-lg shadow-[var(--brand-primary)]/25 hover:brightness-110 active:scale-[0.99] disabled:opacity-50 disabled:pointer-events-none transition-all cursor-pointer"
          >
            {isSubmitting ? (
              <>
                <Loader2 className="w-4 h-4 animate-spin" />
                <span>Verifying...</span>
              </>
            ) : (
              <>
                <span>Verify & Continue</span>
                <ArrowRight className="w-4 h-4" />
              </>
            )}
          </button>

          <div className="flex flex-col gap-2 pt-2 text-center text-xs">
            {hasWebAuthn && (
              <button
                type="button"
                onClick={() => {
                  setMode('webauthn');
                  setErrorMessage(null);
                }}
                className="text-[var(--text-secondary)] hover:text-[var(--brand-primary)] transition-colors cursor-pointer"
              >
                Have a passkey or security key? <span className="underline">Sign in with Passkey</span>
              </button>
            )}
            {methods.includes('recovery') && (
              <button
                type="button"
                onClick={() => {
                  setMode('recovery');
                  setErrorMessage(null);
                }}
                className="text-[var(--text-secondary)] hover:text-[var(--brand-primary)] transition-colors cursor-pointer"
              >
                Lost your authenticator device? <span className="underline">Use a recovery code</span>
              </button>
            )}
          </div>
        </div>
      )}

      {/* Recovery Code Mode */}
      {mode === 'recovery' && (
        <form onSubmit={submitRecovery} className="space-y-4">
          <div className="space-y-1.5">
            <label
              htmlFor="recovery-input"
              className="text-xs font-semibold text-[var(--text-secondary)] uppercase tracking-wider"
            >
              Emergency Recovery Code
            </label>
            <div className="relative">
              <Key className="w-4 h-4 text-[var(--text-muted)] absolute left-3.5 top-1/2 -translate-y-1/2 pointer-events-none" />
              <input
                id="recovery-input"
                type="text"
                required
                autoFocus
                value={recoveryCode}
                onChange={(e) => setRecoveryCode(e.target.value.toUpperCase())}
                placeholder="XXXX-XXXX"
                className="w-full pl-10 pr-4 py-3 rounded-xl bg-[var(--bg-canvas)] text-sm font-mono tracking-wider font-semibold text-[var(--text-main)] placeholder-[var(--text-muted)] ring-1 ring-white/10 focus:ring-2 focus:ring-[var(--brand-primary)] focus:outline-none transition-all shadow-inner"
              />
            </div>
          </div>

          <div className="p-3 rounded-xl bg-amber-500/10 border border-amber-500/30 text-amber-300 text-xs flex items-start gap-2.5">
            <HelpCircle className="w-4 h-4 flex-shrink-0 mt-0.5 text-amber-400" />
            <span>
              Using a recovery code signs in immediately and revokes all other active sessions and trusted devices for security.
            </span>
          </div>

          <button
            type="submit"
            disabled={!recoveryCode.trim() || isSubmitting}
            className="w-full py-3.5 px-4 rounded-xl bg-[var(--brand-primary)] text-[var(--brand-text)] font-semibold text-sm flex items-center justify-center gap-2 shadow-lg shadow-[var(--brand-primary)]/25 hover:brightness-110 active:scale-[0.99] disabled:opacity-50 disabled:pointer-events-none transition-all cursor-pointer"
          >
            {isSubmitting ? (
              <>
                <Loader2 className="w-4 h-4 animate-spin" />
                <span>Verifying Recovery Code...</span>
              </>
            ) : (
              <>
                <span>Sign In with Recovery Code</span>
                <ArrowRight className="w-4 h-4" />
              </>
            )}
          </button>

          <div className="flex flex-col gap-2 pt-2 text-center text-xs">
            {hasWebAuthn && (
              <button
                type="button"
                onClick={() => {
                  setMode('webauthn');
                  setErrorMessage(null);
                }}
                className="text-[var(--text-secondary)] hover:text-[var(--brand-primary)] transition-colors cursor-pointer"
              >
                Use your <span className="underline">Passkey or Security Key</span>
              </button>
            )}
            <button
              type="button"
              onClick={() => {
                setMode('totp');
                setErrorMessage(null);
              }}
              className="text-[var(--text-secondary)] hover:text-[var(--brand-primary)] transition-colors cursor-pointer"
            >
              Have your authenticator app? <span className="underline">Enter 6-digit code</span>
            </button>
          </div>
        </form>
      )}
    </div>
  );
};
