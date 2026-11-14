import React, { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { CreditCard, Loader2, Trash2 } from 'lucide-react';
import { useAuth } from '../auth/AuthContext';
import { ModalShell } from './ModalShell';
import { StepUpPrompt } from './StepUpPrompt';
import { useDeleteCurrentWorkspace } from '../lib/useDeleteCurrentWorkspace';

export const PendingCheckoutScreen: React.FC<{
  name: string;
  slug: string;
  pendingPlan?: string | null;
  onActivated: () => void;
}> = ({ name, slug, pendingPlan, onActivated }) => {
  const { authFetch } = useAuth();
  const [searchParams] = useSearchParams();
  const checkoutSuccess = searchParams.get('checkout') === 'success';
  const checkoutError = searchParams.get('checkoutError');
  const resumeRequested = searchParams.get('resume') === '1';
  const billingInterval: 'month' | 'year' = searchParams.get('interval') === 'year' ? 'year' : 'month';
  const resumable = Boolean(pendingPlan);
  const [confirming, setConfirming] = useState(checkoutSuccess);
  const [timedOut, setTimedOut] = useState(false);
  const [resuming, setResuming] = useState(false);
  const [resumeError, setResumeError] = useState<string | null>(null);
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [deleteSlug, setDeleteSlug] = useState('');
  const workspaceDelete = useDeleteCurrentWorkspace();
  const { deleting, error: deleteError, needsStepUp } = workspaceDelete;
  const ticks = useRef(0);
  const autoResumed = useRef(false);

  const handleResumePayment = useCallback(async () => {
    if (!pendingPlan || resuming) return;
    setResuming(true);
    setResumeError(null);
    try {
      const returnUrl = `${window.location.origin}/org/${encodeURIComponent(slug)}`;
      const res = await authFetch('/api/Subscription/update', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          plan: pendingPlan,
          billingInterval,
          returnUrl,
          confirmed: true,
        }),
      });
      const body = await res.json().catch(() => ({})) as { redirectUrl?: string; message?: string; error?: string };
      if (!res.ok) {
        setResumeError(body.message || body.error || 'Could not start checkout. Please try again.');
        setResuming(false);
        return;
      }
      if (body.redirectUrl) {
        window.location.assign(body.redirectUrl);
        return;
      }
      setResumeError('Checkout link was not returned. Please choose a plan below.');
      setResuming(false);
    } catch {
      setResumeError('Could not reach the billing service. Please try again.');
      setResuming(false);
    }
  }, [authFetch, billingInterval, pendingPlan, resuming, slug]);

  useEffect(() => {
    if (!resumable || !resumeRequested || checkoutSuccess || autoResumed.current) return;
    autoResumed.current = true;
    const url = new URL(window.location.href);
    url.searchParams.delete('resume');
    window.history.replaceState({}, '', `${url.pathname}${url.search}${url.hash}`);
    void handleResumePayment();
  }, [checkoutSuccess, handleResumePayment, resumable, resumeRequested]);

  useEffect(() => {
    if (!checkoutSuccess) return;
    let cancelled = false;
    setConfirming(true);
    setTimedOut(false);
    ticks.current = 0;
    const timer = window.setInterval(() => {
      void (async () => {
        ticks.current += 1;
        try {
          const res = await authFetch('/api/organizations/current');
          if (!res.ok) return;
          const body = await res.json() as { activationState?: string };
          if (cancelled) return;
          if ((body.activationState || 'active') === 'active') {
            window.clearInterval(timer);
            onActivated();
            return;
          }
        } catch { /* keep polling */ }
        if (ticks.current >= 20) {
          window.clearInterval(timer);
          if (!cancelled) {
            setConfirming(false);
            setTimedOut(true);
          }
        }
      })();
    }, 3000);
    return () => {
      cancelled = true;
      window.clearInterval(timer);
    };
  }, [authFetch, checkoutSuccess, onActivated]);

  const handleDelete = (event: React.FormEvent) => {
    event.preventDefault();
    if (deleteSlug !== slug || deleting || needsStepUp) return;
    void workspaceDelete.remove(deleteSlug);
  };

  return (
    <div className="min-h-screen w-full flex items-center justify-center bg-[var(--bg-canvas)] px-4">
      <div className="w-full max-w-md rounded-2xl bg-[var(--bg-surface)] shadow-2xl shadow-black/30 p-6 sm:p-8 space-y-4 text-center">
        <div className="mx-auto w-12 h-12 rounded-2xl bg-amber-500/15 text-amber-600 dark:text-amber-300 flex items-center justify-center">
          {confirming ? <Loader2 className="w-6 h-6 animate-spin" aria-hidden="true" /> : <CreditCard className="w-6 h-6" aria-hidden="true" />}
        </div>
        <h1 className="text-lg font-bold text-[var(--text-main)]">
          {confirming ? 'Confirming payment…' : 'Waiting for payment'}
        </h1>
        <p className="text-sm text-[var(--text-secondary)]">
          <strong className="text-[var(--text-main)]">{name}</strong> is ready once payment is confirmed.
          Unpaid workspaces are removed 7 days after they&apos;re created.
        </p>
        {(resumeError || checkoutError) && (
          <p role="alert" className="text-xs text-rose-400 font-medium">{resumeError || checkoutError}</p>
        )}
        {timedOut && (
          <p role="status" className="text-xs text-amber-700 dark:text-amber-300 font-medium">
            Payment is still being confirmed. This can take a minute.
          </p>
        )}
        <div className="flex flex-col sm:flex-row gap-2.5 pt-2">
          {resumable ? (
            <button
              type="button"
              disabled={resuming || confirming}
              onClick={() => { void handleResumePayment(); }}
              className="btn-primary !h-11 flex-1 inline-flex items-center justify-center gap-2 disabled:opacity-60"
            >
              {resuming && <Loader2 className="w-4 h-4 animate-spin" aria-hidden="true" />}
              {resuming ? 'Starting checkout…' : 'Resume payment'}
            </button>
          ) : (
            <Link
              to={`/org/${encodeURIComponent(slug)}/settings?tab=subscription`}
              className="btn-primary !h-11 flex-1 inline-flex items-center justify-center"
            >
              Choose a plan
            </Link>
          )}
          <button
            type="button"
            onClick={() => { setDeleteSlug(''); workspaceDelete.reset(); setDeleteOpen(true); }}
            className="h-11 px-4 rounded-xl text-sm font-semibold text-rose-400 border border-rose-500/30 hover:bg-rose-500/10 flex-1 inline-flex items-center justify-center gap-2"
          >
            <Trash2 className="w-4 h-4" aria-hidden="true" /> Discard workspace
          </button>
        </div>
        {resumable && pendingPlan && (
          <div className="pt-1">
            <Link
              to={`/org/${encodeURIComponent(slug)}/settings?tab=subscription&upgrade=${encodeURIComponent(pendingPlan)}`}
              className="text-xs font-semibold text-[var(--brand-primary)] hover:underline"
            >
              Choose a different plan
            </Link>
          </div>
        )}
        {timedOut && (
          <button
            type="button"
            className="text-xs font-semibold text-[var(--brand-primary)] hover:underline"
            onClick={() => window.location.reload()}
          >
            Refresh
          </button>
        )}
      </div>

      <ModalShell
        open={deleteOpen}
        onClose={() => { if (!deleting) setDeleteOpen(false); }}
        closeOnEscape={!deleting}
        closeOnBackdrop={!deleting}
        bare
        role="alertdialog"
        aria-labelledby="discard-workspace-title"
        panelClassName="w-full max-w-md rounded-2xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] p-6 shadow-2xl space-y-4"
      >
        <form onSubmit={handleDelete} className="space-y-4">
          <div>
            <h2 id="discard-workspace-title" className="text-base font-bold text-[var(--text-main)]">
              Discard {name}?
            </h2>
            <p className="mt-2 text-sm text-[var(--text-secondary)]">
              This action cannot be undone. Type <strong>{slug}</strong> to confirm.
            </p>
          </div>
          <input
            value={deleteSlug}
            onChange={(e) => setDeleteSlug(e.target.value)}
            className="w-full h-11 px-3 rounded-xl bg-[var(--bg-subtle)] text-sm font-mono text-[var(--text-main)] focus:outline-none focus:ring-2 focus:ring-rose-500"
            aria-label="Confirm workspace slug"
          />
          {deleteError && <p role="alert" className="text-sm text-rose-400">{deleteError}</p>}
          {needsStepUp && <StepUpPrompt actionLabel="discarding this workspace" onVerified={workspaceDelete.retry} />}
          <div className="flex justify-end gap-2">
            <button type="button" disabled={deleting} onClick={() => setDeleteOpen(false)} className="h-10 px-4 rounded-xl text-xs font-semibold text-[var(--text-secondary)]">
              Cancel
            </button>
            <button
              type="submit"
              disabled={deleteSlug !== slug || deleting || needsStepUp}
              className="h-10 px-4 rounded-xl bg-rose-600 text-white text-xs font-semibold disabled:opacity-50"
            >
              {deleting ? 'Deleting…' : 'Discard workspace'}
            </button>
          </div>
        </form>
      </ModalShell>
    </div>
  );
};

export default PendingCheckoutScreen;
