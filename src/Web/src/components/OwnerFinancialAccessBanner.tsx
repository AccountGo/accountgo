import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { ConfirmDialog } from './ConfirmDialog';

const currentWorkspacePath = '/api/organizations/current';
const setupPath = currentWorkspacePath + '/owner-access-setup/full-access';
const grantLabel = 'Give me full workspace access';
const actionStyle = 'inline-flex min-h-11 shrink-0 items-center justify-center self-start rounded-lg bg-[var(--brand-primary)] px-4 text-sm font-semibold text-white sm:self-center';

type AccessStatus = 'unavailable' | 'finish' | 'eligible' | 'setup' | 'granted' | 'complete';

// Dismissal is remembered per user and workspace, best effort: if storage is blocked or full the prompt simply shows again.
const dismissKey = (userId: string, orgId: string) => `accountgo.setupAccessPrompt.v1:${userId}:${orgId}`;
function readDismissed(userId: string | undefined, orgId: string): boolean {
  if (!userId) return false;
  try { return window.localStorage.getItem(dismissKey(userId, orgId)) === '1'; } catch { return false; }
}
function writeDismissed(userId: string | undefined, orgId: string): void {
  if (!userId) return;
  try { window.localStorage.setItem(dismissKey(userId, orgId), '1'); } catch { /* shows again next time */ }
}

interface Props {
  slug: string;
  orgId: string;
  /** Personal books are set up on their own path; the access-setup prompt does not apply there. */
  isPersonalBooks?: boolean;
  onAccessGranted: (permissions: string[]) => void;
}

export function OwnerFinancialAccessBanner({ slug, orgId, isPersonalBooks, onAccessGranted }: Props) {
  const { authFetch, user } = useAuth();
  const [dismissed, setDismissed] = useState(() => readDismissed(user?.id, orgId));
  const [status, setStatus] = useState<AccessStatus>('unavailable');
  const [confirming, setConfirming] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const mutation = useRef<AbortController | null>(null);
  const latestAccessGranted = useRef(onAccessGranted);

  useLayoutEffect(() => {
    latestAccessGranted.current = onAccessGranted;
  }, [onAccessGranted]);

  useEffect(() => {
    const request = new AbortController();
    void authFetch(setupPath, {
      headers: { 'X-AccountGo-Org': slug },
      signal: request.signal,
    }).then(async (response) => {
      if (!response.ok) return;
      const data = await response.json();
      if (request.signal.aborted) return;
      if (data.finishSetup === true) { setStatus('finish'); return; }
      if (data.eligible === true) { setStatus('eligible'); return; }
      // The one-click grant is not offered. A sole owner whose own access is still unset gets the same prompt with the
      // Team & Members setup as its action (the same signal and eligibility Members itself uses).
      if (isPersonalBooks) return;
      const base = currentWorkspacePath;
      const [setupRes, soloRes] = await Promise.all([
        authFetch(base + '/access-setup/status', { headers: { 'X-AccountGo-Org': slug }, signal: request.signal }),
        authFetch(base + '/solo-setup/eligibility', { headers: { 'X-AccountGo-Org': slug }, signal: request.signal }),
      ]);
      if (!setupRes.ok || request.signal.aborted) return;
      const setup = await setupRes.json();
      const solo = soloRes.ok ? await soloRes.json().catch(() => null) : null;
      const soloEligible = solo?.status === 'Eligible' || solo?.status === 0;
      if (!request.signal.aborted && setup?.ownerCanManageOwnAccess === true && setup?.totalFunctionalRoles === 0 && !soloEligible) {
        setStatus('setup');
      }
    }).catch(() => undefined);

    return () => request.abort();
  }, [authFetch, slug, isPersonalBooks]);

  useEffect(() => () => mutation.current?.abort(), []);

  const activate = async (grant: boolean) => {
    if (mutation.current) return;
    const request = new AbortController();
    mutation.current = request;
    setBusy(true);
    setError(null);
    const options = { headers: { 'X-AccountGo-Org': slug }, signal: request.signal };
    try {
      if (grant) {
        const response = await authFetch(setupPath, { ...options, method: 'POST' }, false);
        if (request.signal.aborted) return;
        if (response.status === 403 || response.status === 409) {
          setConfirming(false);
          setStatus('unavailable');
          setError('Self-service access setup is no longer available.');
          return;
        }
        if (!response.ok) throw new Error('Grant failed');
        setConfirming(false);
        setStatus('granted');
      }

      const current = await authFetch(currentWorkspacePath, options);
      if (!current.ok) throw new Error('Refresh failed');
      const { permissions } = await current.json();
      if (!Array.isArray(permissions)) throw new Error('Permissions unavailable');
      if (request.signal.aborted) return;
      latestAccessGranted.current(permissions);
      setStatus('complete');
    } catch {
      if (!request.signal.aborted) setError('Could not update your access on this screen. Try again.');
    } finally {
      if (!request.signal.aborted) setBusy(false);
      mutation.current = null;
    }
  };

  if (status === 'unavailable' && !error) return null;
  if (dismissed && (status === 'eligible' || status === 'finish' || status === 'setup')) return null;
  if (status === 'complete') return <p role="status" className="text-sm">Full workspace access granted.</p>;
  if (status === 'unavailable') {
    return (
      <p role="alert" className="text-sm">
        {error}{' '}
        <Link to={`/org/${slug}/members`} className="underline">Review your access in Team &amp; Members</Link>
      </p>
    );
  }

  return (
    <section aria-labelledby="owner-financial-access-title" className="flex flex-col gap-3 rounded-2xl border border-[var(--brand-primary)]/30 bg-[var(--brand-subtle)] p-4 sm:flex-row sm:justify-between sm:p-5">
      <div>
        <h2 id="owner-financial-access-title" className="font-semibold">{status === 'setup' ? 'Set up your access' : 'Set up your financial access'}</h2>
        <p className="mt-1 text-sm text-[var(--text-secondary)]">
          {status === 'setup'
            ? 'You own this workspace, but your own access to its tools is not set up yet. Choose what you can work with in Team & Members.'
            : status === 'granted'
            ? 'Full workspace access was granted. Refresh your permissions to see the available tools.'
            : 'Your Owner role lets you manage this workspace. Financial permissions are assigned separately so teams can control who works with the books. Approval rules still apply.'}
        </p>
        {error && !confirming && <p role="alert" className="mt-2 text-sm">{error}</p>}
      </div>
      {status === 'finish' ? (
        <Link className={actionStyle} to={'/create-org?' + new URLSearchParams({ continue: slug })}>Finish workspace setup</Link>
      ) : status === 'setup' ? (
        <Link className={actionStyle} to={`/org/${encodeURIComponent(slug)}/members?access=setup`}>Set up your access</Link>
      ) : (
        <button
          type="button"
          className={actionStyle}
          disabled={busy}
          onClick={() => status === 'granted' ? void activate(false) : setConfirming(true)}
        >
          {status === 'granted' ? 'Refresh permissions' : grantLabel}
        </button>
      )}
      {(status === 'eligible' || status === 'finish' || status === 'setup') && (
        <button
          type="button"
          className="inline-flex min-h-11 shrink-0 items-center justify-center self-start rounded-lg px-3 text-sm text-[var(--text-secondary)] underline sm:self-center"
          onClick={() => { writeDismissed(user?.id, orgId); setDismissed(true); }}
        >
          Not now
        </button>
      )}
      {confirming && (
        <ConfirmDialog
          title={grantLabel}
          message="This gives you the permissions included in Full workspace access for this workspace. Your plan, enabled tools, and approval rules still apply."
          confirmLabel={grantLabel}
          busy={busy}
          error={error}
          onConfirm={() => void activate(true)}
          onCancel={() => { setConfirming(false); setError(null); }}
        />
      )}
    </section>
  );
}
