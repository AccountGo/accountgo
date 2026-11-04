// SPDX-License-Identifier: AGPL-3.0-only
import React, { useCallback, useEffect, useState } from 'react';
import { Link, Navigate, useNavigate, useSearchParams } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { Loader2, ShieldAlert, Terminal } from 'lucide-react';

interface PendingRequest {
  requestId: string;
  clientId: string;
  clientDisplayName: string;
  redirectUri: string;
  loopbackPort: number;
  scope: string;
  signedInEmail: string;
  requiresStepUp: boolean;
}

export const CliAuthorize: React.FC = () => {
  const { isAuthenticated, isLoading, authFetch, user } = useAuth();
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const requestId = params.get('request_id')?.trim() ?? '';

  const [pending, setPending] = useState<PendingRequest | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    if (!requestId) {
      setError('Missing authorization request.');
      return;
    }
    setError(null);
    const res = await authFetch(`/api/auth/oauth/requests/${encodeURIComponent(requestId)}`);
    if (res.status === 401) {
      navigate('/login', { replace: true, state: { from: { pathname: '/cli/authorize', search: `?request_id=${encodeURIComponent(requestId)}` } } });
      return;
    }
    if (!res.ok) {
      const body = await res.json().catch(() => null);
      setError(body?.error_description || body?.detail || 'This authorization request is invalid or expired.');
      setPending(null);
      return;
    }
    const data = (await res.json()) as PendingRequest;
    setPending(data);
  }, [authFetch, navigate, requestId]);

  useEffect(() => {
    if (!isLoading && isAuthenticated && requestId) {
      void load();
    }
  }, [isAuthenticated, isLoading, load, requestId]);

  const decide = async (decision: 'approve' | 'deny') => {
    if (!requestId || busy) return;
    setBusy(true);
    setError(null);
    try {
      const res = await authFetch('/api/auth/oauth/code', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          requestId,
          decision,
          deviceLabel: `CLI on ${typeof navigator !== 'undefined' ? navigator.platform || 'device' : 'device'}`,
        }),
      });
      const body = await res.json().catch(() => null);
      if (res.status === 403 && body?.code === 'mfa_step_up_required') {
        setError('Confirm your identity again, then return here to approve CLI access.');
        setBusy(false);
        return;
      }
      if (!res.ok || !body?.redirectUrl) {
        setError(body?.error_description || body?.detail || 'Could not complete authorization.');
        setBusy(false);
        return;
      }
      window.location.assign(body.redirectUrl as string);
    } catch {
      setError('Could not complete authorization.');
      setBusy(false);
    }
  };

  if (isLoading) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-[var(--bg-canvas)]">
        <Loader2 className="h-6 w-6 animate-spin text-[var(--brand-primary)]" />
      </div>
    );
  }

  if (!isAuthenticated) {
    return (
      <Navigate
        to="/login"
        replace
        state={{ from: { pathname: '/cli/authorize', search: requestId ? `?request_id=${encodeURIComponent(requestId)}` : '' } }}
      />
    );
  }

  return (
    <div className="min-h-screen bg-[var(--bg-canvas)] text-[var(--text-main)] flex items-center justify-center p-4">
      <div className="w-full max-w-md rounded-2xl border border-[var(--border-subtle)] bg-[var(--bg-elevated)] p-6 shadow-sm">
        <div className="flex items-center gap-3 mb-4">
          <div className="h-10 w-10 rounded-xl bg-[var(--brand-primary)]/10 flex items-center justify-center">
            <Terminal className="h-5 w-5 text-[var(--brand-primary)]" />
          </div>
          <div>
            <h1 className="text-lg font-semibold">Authorize CLI access</h1>
            <p className="text-sm text-[var(--text-muted)]">Signed in as {pending?.signedInEmail || user?.email}</p>
          </div>
        </div>

        {error && (
          <div className="mb-4 flex gap-2 rounded-lg border border-amber-500/30 bg-amber-500/10 p-3 text-sm">
            <ShieldAlert className="h-4 w-4 shrink-0 mt-0.5" />
            <span>{error}</span>
          </div>
        )}

        {!pending && !error && (
          <div className="flex justify-center py-8">
            <Loader2 className="h-5 w-5 animate-spin text-[var(--brand-primary)]" />
          </div>
        )}

        {pending && (
          <>
            <dl className="space-y-3 text-sm mb-6">
              <div>
                <dt className="text-[var(--text-muted)]">Application</dt>
                <dd className="font-medium">{pending.clientDisplayName}</dd>
              </div>
              <div>
                <dt className="text-[var(--text-muted)]">Loopback callback</dt>
                <dd className="font-mono text-xs break-all">127.0.0.1:{pending.loopbackPort}</dd>
              </div>
              <div>
                <dt className="text-[var(--text-muted)]">Scopes</dt>
                <dd className="font-mono text-xs">{pending.scope}</dd>
              </div>
            </dl>

            {pending.requiresStepUp && (
              <p className="mb-4 text-sm text-amber-700 dark:text-amber-300">
                Your session needs a recent confirmation before approving. Complete step-up from Profile, then reload this page.
              </p>
            )}

            <div className="flex gap-3">
              <button
                type="button"
                disabled={busy || pending.requiresStepUp}
                onClick={() => void decide('approve')}
                className="flex-1 rounded-lg bg-[var(--brand-primary)] px-4 py-2.5 text-sm font-semibold text-white disabled:opacity-50"
              >
                {busy ? 'Working…' : 'Approve'}
              </button>
              <button
                type="button"
                disabled={busy}
                onClick={() => void decide('deny')}
                className="flex-1 rounded-lg border border-[var(--border-subtle)] px-4 py-2.5 text-sm font-semibold disabled:opacity-50"
              >
                Deny
              </button>
            </div>
          </>
        )}

        <p className="mt-6 text-center text-xs text-[var(--text-muted)]">
          <Link to="/select-org" className="text-[var(--brand-primary)]">Back to AccountGo</Link>
        </p>
      </div>
    </div>
  );
};

export default CliAuthorize;
