import React, { useState } from 'react';
import { KeyRound, Loader2 } from 'lucide-react';
import { useAuth } from '../auth/AuthContext';
import { getPasskeyAssertion, isWebAuthnSupported } from '../lib/webauthn';

/**
 * Inline "confirm it's you" prompt for actions the server protects with a recent second-factor check
 * (403 `mfa_step_up_required`). Verifies an authenticator code or passkey, swaps in the step-up session,
 * then calls `onVerified` so the caller can retry the action. Not a <form>, so it can sit inside one.
 */
export const StepUpPrompt: React.FC<{ onVerified: () => void; actionLabel?: string }> = ({ onVerified, actionLabel = 'this action' }) => {
  const { authFetch, completeStepUp } = useAuth();
  const [code, setCode] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const verify = async (passkey: boolean) => {
    if (busy) return;
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
      const res = await authFetch(endpoint, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
      const data = await res.json().catch(() => null);
      if (!res.ok || !data?.accessToken) throw new Error(data?.detail || 'Verification failed. Check the code and try again.');
      completeStepUp(data.accessToken);
      setCode('');
      onVerified();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Verification failed.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div role="group" aria-labelledby="step-up-title" className="rounded-xl bg-[var(--bg-subtle)] p-4 space-y-3 text-left">
      <p id="step-up-title" className="flex items-center gap-2 text-sm font-semibold text-[var(--text-main)]">
        <KeyRound className="w-4 h-4" aria-hidden="true" /> Confirm it’s you
      </p>
      <p className="text-xs text-[var(--text-secondary)]">
        For your security, enter a code from your authenticator app{isWebAuthnSupported() ? ' or use your passkey' : ''} to continue with {actionLabel}.
      </p>
      <label htmlFor="step-up-code" className="block text-xs font-semibold text-[var(--text-secondary)]">Authenticator code</label>
      <input
        id="step-up-code"
        value={code}
        onChange={(e) => setCode(e.target.value.replace(/\D/g, '').slice(0, 6))}
        onKeyDown={(e) => {
          if (e.key === 'Enter') {
            e.preventDefault();
            if (code.length === 6) void verify(false);
          }
        }}
        inputMode="numeric"
        autoComplete="one-time-code"
        autoFocus
        className="w-full h-11 px-3 rounded-xl bg-[var(--bg-surface)] text-sm font-mono tracking-widest text-[var(--text-main)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-primary)]"
      />
      {error && <p role="alert" className="text-xs text-rose-400">{error}</p>}
      <div className="flex flex-wrap gap-2">
        <button
          type="button"
          disabled={busy || code.length !== 6}
          onClick={() => void verify(false)}
          className="h-10 px-4 rounded-xl bg-[var(--brand-primary)] text-white text-xs font-semibold disabled:opacity-50 inline-flex items-center gap-2"
        >
          {busy && <Loader2 className="w-3.5 h-3.5 animate-spin" aria-hidden="true" />} Verify and continue
        </button>
        {isWebAuthnSupported() && (
          <button
            type="button"
            disabled={busy}
            onClick={() => void verify(true)}
            className="h-10 px-4 rounded-xl text-xs font-semibold text-[var(--text-secondary)] hover:bg-[var(--bg-surface-hover)] disabled:opacity-50"
          >
            Use passkey
          </button>
        )}
      </div>
    </div>
  );
};
