import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { ConfirmDialog } from './ConfirmDialog';

const currentWorkspacePath = '/api/organizations/current';
const setupPath = currentWorkspacePath + '/owner-access-setup/full-access';
const grantLabel = 'Give me full workspace access';
const actionStyle = 'inline-flex min-h-11 shrink-0 items-center justify-center self-start rounded-lg bg-[var(--brand-primary)] px-4 text-sm font-semibold text-white sm:self-center';

type AccessStatus = 'unavailable' | 'finish' | 'eligible' | 'granted' | 'complete';

interface Props {
  slug: string;
  onAccessGranted: (permissions: string[]) => void;
}

export function OwnerFinancialAccessBanner({ slug, onAccessGranted }: Props) {
  const { authFetch } = useAuth();
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
      if (!request.signal.aborted) {
        setStatus(data.finishSetup === true ? 'finish' : data.eligible === true ? 'eligible' : 'unavailable');
      }
    }).catch(() => undefined);

    return () => request.abort();
  }, [authFetch, slug]);

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
        <h2 id="owner-financial-access-title" className="font-semibold">Set up your financial access</h2>
        <p className="mt-1 text-sm text-[var(--text-secondary)]">
          {status === 'granted'
            ? 'Full workspace access was granted. Refresh your permissions to see the available tools.'
            : 'Your Owner role lets you manage this workspace. Financial permissions are assigned separately so teams can control who works with the books. Approval rules still apply.'}
        </p>
        {error && !confirming && <p role="alert" className="mt-2 text-sm">{error}</p>}
      </div>
      {status === 'finish' ? (
        <Link className={actionStyle} to={'/create-org?' + new URLSearchParams({ continue: slug })}>Finish workspace setup</Link>
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
