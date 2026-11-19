import React, { useCallback, useEffect, useState } from 'react';
import { AlertCircle, CalendarCheck, CheckCircle2, Loader2, RotateCcw } from 'lucide-react';
import { Link } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import {
  canEnableCloseButton,
  closeResolutionUrl,
  fiscalYearStatusLabel,
  normalizeFiscalYearCloseStatus,
  reviewAndCloseLabel,
  warningCloseReady,
  type FiscalYearCloseStatusValue,
} from '../lib/yearEndClose';
import { PermissionDeniedCard, usePermissions, useTenantContext } from './Gating';
import { ModalShell } from './ModalShell';
import { PageHeader } from './PageHeader';

interface YearEndYearRow {
  fiscalYearCode: string;
  startDate: string;
  endDate: string;
  status: FiscalYearCloseStatusValue;
  closedAt?: string | null;
  closedBy?: string | null;
}

interface YearEndCheckItem {
  code: string;
  message: string;
  count: number;
  linkPath?: string | null;
}

interface YearEndClosingLine {
  accountId: number;
  accountCode: string;
  accountName: string;
  drCr: string;
  amount: number;
}

interface YearEndPreview {
  fiscalYearCode: string;
  startDate: string;
  endDate: string;
  status: FiscalYearCloseStatusValue;
  closedAt?: string | null;
  closedBy?: string | null;
  blocking: YearEndCheckItem[];
  warnings: YearEndCheckItem[];
  closingLines: YearEndClosingLine[];
  partnerAllocations?: Array<{ partnerId: number; name: string; percent: number; profitShare: number; drawingsClosed: number; capitalAfterClose: number }>;
  partnerLossNotice?: string | null;
  canClose: boolean;
  previewHash?: string;
  packageNote?: string | null;
  issuanceStatus?: string;
  canReopen?: boolean;
  reopenBlockedReason?: string | null;
  latestCloseRunId?: number | null;
  currentBooksLockedThrough?: string | null;
  proposedBooksLockedThrough?: string | null;
  requiresLockAcknowledgement?: boolean;
  newlyUnlockedFrom?: string | null;
  newlyUnlockedThrough?: string | null;
}

const labelClass = 'block text-xs font-semibold text-[var(--text-secondary)] mb-1.5';
const inputClass =
  'w-full h-11 px-3.5 rounded-xl bg-[var(--bg-subtle)] shadow-inner text-sm text-[var(--text-main)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-ring)] disabled:opacity-60';

function formatAmount(value: number): string {
  return Number(value).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

function CheckList({
  items,
  tone,
}: {
  items: YearEndCheckItem[];
  tone: 'blocking' | 'warning';
}) {
  if (items.length === 0) return null;
  const boxClass =
    tone === 'blocking'
      ? 'bg-rose-500/10 text-rose-400 border-rose-500/20'
      : 'bg-amber-500/10 text-amber-700 border-amber-500/20';
  return (
    <ul className={`space-y-2 p-3 rounded-xl border text-xs ${boxClass}`}>
      {items.map((item) => (
        <li key={item.code} className="flex flex-wrap items-start gap-2">
          <AlertCircle className="w-4 h-4 shrink-0 mt-0.5" aria-hidden="true" />
          <span>
            {item.message}
            {item.count > 1 ? ` (${item.count})` : ''}
          </span>
          {item.linkPath && (
            <ReviewLink path={item.linkPath} />
          )}
        </li>
      ))}
    </ul>
  );
}

/** Fiscal year-end close / reopen on Financial Rules (#455 Phase 2b-1). */
function ReviewLink({ path }: { path: string }) {
  const { currentOrg } = useTenantContext();
  return (
    <Link to={closeResolutionUrl(currentOrg.slug, path)} className="font-semibold underline">
      Review
    </Link>
  );
}

export const YearEndCloseCard: React.FC<{ canClose: boolean; personal?: boolean }> = ({ canClose, personal = false }) => {
  const { authFetch } = useAuth();
  const [years, setYears] = useState<YearEndYearRow[] | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [idempotencyKey, setIdempotencyKey] = useState<string | null>(null);
  const [busyCode, setBusyCode] = useState<string | null>(null);
  const [message, setMessage] = useState<{ kind: 'ok' | 'error'; text: string } | null>(null);

  const [closeDialog, setCloseDialog] = useState<{
    code: string;
    preview: YearEndPreview;
    acknowledgeWarnings: boolean;
    warningReason: string;
    stale: boolean;
  } | null>(null);

  const [reopenDialog, setReopenDialog] = useState<{ code: string; reason: string; preview: YearEndPreview; acknowledge: boolean } | null>(null);

  const loadYears = useCallback(async () => {
    try {
      const res = await authFetch('/api/Financials/YearEnd');
      if (res.ok) {
        const data = (await res.json()) as YearEndYearRow[];
        setYears(Array.isArray(data) ? data : []);
        setLoadError(null);
      } else {
        setYears([]);
        setLoadError('Could not load fiscal years.');
      }
    } catch (err) {
      console.error('Failed to load fiscal years for year-end close:', err);
      setYears([]);
      setLoadError(err instanceof Error ? err.message : 'Could not load fiscal years.');
    }
  }, [authFetch]);

  useEffect(() => {
    void loadYears();
  }, [loadYears]);

  const prepareYears = async () => {
    setBusyCode('prepare');
    setMessage(null);
    try {
      const res = await authFetch('/api/Financials/YearEnd/EnsureYears', { method: 'POST' });
      const body = await res.json().catch(() => null);
      if (!res.ok) throw new Error(body?.message || 'Could not prepare fiscal years.');
      await loadYears();
      setMessage({ kind: 'ok', text: 'Fiscal years are available through next year.' });
    } catch (err) {
      setMessage({ kind: 'error', text: err instanceof Error ? err.message : 'Could not prepare fiscal years.' });
    } finally {
      setBusyCode(null);
    }
  };

  const openCloseDialog = async (code: string) => {
    setBusyCode(code);
    setMessage(null);
    try {
      const res = await authFetch(`/api/Financials/YearEnd/${encodeURIComponent(code)}`);
      const body = await res.json().catch(() => null);
      if (!res.ok) {
        throw new Error(body?.message || 'Could not load year-end preview.');
      }
      if (!idempotencyKey) setIdempotencyKey(crypto.randomUUID());
      setCloseDialog({
        code,
        preview: body as YearEndPreview,
        acknowledgeWarnings: false,
        warningReason: '',
        stale: false,
      });
    } catch (err) {
      setMessage({
        kind: 'error',
        text: err instanceof Error ? err.message : 'Could not load year-end preview.',
      });
    } finally {
      setBusyCode(null);
    }
  };

  const submitClose = async () => {
    if (!closeDialog || !canClose) return;
    const { code, preview, acknowledgeWarnings, warningReason } = closeDialog;
    if (!warningCloseReady(preview.warnings.length, acknowledgeWarnings, warningReason)) {
      setMessage({ kind: 'error', text: 'Acknowledge the warnings and enter a reason before closing the year.' });
      return;
    }
    if (!preview.previewHash) {
      setMessage({ kind: 'error', text: 'Refresh the review before closing the year.' });
      return;
    }
    setBusyCode(code);
    setMessage(null);
    try {
      const res = await authFetch(`/api/Financials/YearEnd/${encodeURIComponent(code)}/Close`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Idempotency-Key': idempotencyKey || crypto.randomUUID(),
        },
        body: JSON.stringify({
          acknowledgeWarnings,
          warningExceptionReason: warningReason.trim(),
          previewHash: preview.previewHash,
        }),
      });
      const body = await res.json().catch(() => null);
      if (res.status === 409 && body?.code === 'stale_year_end_preview') {
        setCloseDialog({ ...closeDialog, acknowledgeWarnings: false, warningReason: '', stale: true });
        setMessage({ kind: 'error', text: body.message || 'The review is out of date. Refresh it before closing.' });
        return;
      }
      if (!res.ok) {
        throw new Error(body?.message || 'Could not close the fiscal year.');
      }
      setCloseDialog(null);
      setIdempotencyKey(null);
      setMessage({ kind: 'ok', text: `Fiscal year ${code} closed.` });
      await loadYears();
    } catch (err) {
      setMessage({
        kind: 'error',
        text: err instanceof Error ? err.message : 'Could not close the fiscal year.',
      });
    } finally {
      setBusyCode(null);
    }
  };

  const openReopenDialog = async (code: string) => {
    setBusyCode(code);
    setMessage(null);
    try {
      const res = await authFetch(`/api/Financials/YearEnd/${encodeURIComponent(code)}`);
      const body = await res.json().catch(() => null);
      if (!res.ok) throw new Error(body?.message || 'Could not load reopening details.');
      setReopenDialog({ code, reason: '', preview: body as YearEndPreview, acknowledge: false });
    } catch (err) {
      setMessage({ kind: 'error', text: err instanceof Error ? err.message : 'Could not load reopening details.' });
    } finally {
      setBusyCode(null);
    }
  };

  const submitReopen = async () => {
    if (!reopenDialog || !canClose) return;
    if (!reopenDialog.reason.trim()) {
      setMessage({ kind: 'error', text: 'A reason is required to reopen a closed year.' });
      return;
    }
    const { code, reason, preview, acknowledge } = reopenDialog;
    if (preview.requiresLockAcknowledgement && !acknowledge) return;
    setBusyCode(code);
    setMessage(null);
    try {
      const res = await authFetch(`/api/Financials/YearEnd/${encodeURIComponent(code)}/Reopen`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ reason: reason.trim(), acknowledgeIndependentLock: acknowledge, expectedLockThrough: preview.currentBooksLockedThrough ?? null }),
      });
      const body = await res.json().catch(() => null);
      if (!res.ok) {
        throw new Error(body?.message || 'Could not reopen the fiscal year.');
      }
      setReopenDialog(null);
      setMessage({ kind: 'ok', text: `Fiscal year ${code} reopened.` });
      await loadYears();
    } catch (err) {
      setMessage({
        kind: 'error',
        text: err instanceof Error ? err.message : 'Could not reopen the fiscal year.',
      });
    } finally {
      setBusyCode(null);
    }
  };

  const downloadPackage = async (code: string) => {
    setMessage(null);
    try {
      const res = await authFetch(`/api/Financials/YearEnd/${encodeURIComponent(code)}/CloseRuns/latest/Package?format=json`);
      const body = await res.json().catch(() => null);
      if (!res.ok) throw new Error(body?.message || 'Could not download the close package.');
      const blob = new Blob([JSON.stringify(body, null, 2)], { type: 'application/json' });
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = `year-end-${code}.json`;
      link.click();
      URL.revokeObjectURL(url);
    } catch (err) {
      setMessage({ kind: 'error', text: err instanceof Error ? err.message : 'Could not download the close package.' });
    }
  };

  const declareUnissued = async (code: string, reason: string) => {
    if (!reason.trim()) {
      setMessage({ kind: 'error', text: 'A reason is required to declare that the statements were not issued.' });
      return;
    }
    const res = await authFetch(`/api/Financials/YearEnd/${encodeURIComponent(code)}/Issuance`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ status: 'Unissued', reason: reason.trim() }),
    });
    const body = await res.json().catch(() => null);
    if (!res.ok) throw new Error(body?.message || 'Could not record the declaration.');
    setReopenDialog((current) => current ? { ...current, preview: body as YearEndPreview } : current);
  };

  if (!years) {
    return (
      <div className="p-4 sm:p-6 rounded-2xl bg-[var(--bg-surface)] shadow-md">
        <p className="text-sm text-[var(--text-secondary)]">Loading year-end close…</p>
      </div>
    );
  }

  return (
    <>
      <div className="p-4 sm:p-6 rounded-2xl bg-[var(--bg-surface)] shadow-md space-y-4">
        <div className="pb-2 border-b border-[var(--border-subtle)]">
          <div className="flex items-center gap-2.5">
            <CalendarCheck className="w-5 h-5 text-indigo-400" aria-hidden="true" />
            <div>
              <h3 className="text-sm font-bold text-[var(--text-main)]">Year-end close</h3>
              <p className="text-xs text-[var(--text-muted)]">
                {personal ? 'Close completed fiscal years.' : 'Close completed fiscal years to zero revenue and expense into equity.'}
              </p>
            </div>
          </div>
        </div>

        {loadError && (
          <div role="alert" className="flex items-center justify-between gap-2 p-3 rounded-xl text-xs bg-rose-500/10 text-rose-400">
            <span>{loadError}</span>
            <button type="button" className="btn-secondary !h-8 !text-xs" onClick={() => void loadYears()}>Retry</button>
          </div>
        )}

        {message && (
          <div
            role={message.kind === 'error' ? 'alert' : 'status'}
            className={`flex items-start gap-2 p-3 rounded-xl text-xs font-medium ${
              message.kind === 'error' ? 'bg-rose-500/10 text-rose-400' : 'bg-emerald-500/10 text-emerald-400'
            }`}
          >
            {message.kind === 'error' ? (
              <AlertCircle className="w-4 h-4 mt-0.5" aria-hidden="true" />
            ) : (
              <CheckCircle2 className="w-4 h-4 mt-0.5" aria-hidden="true" />
            )}
            <span>{message.text}</span>
          </div>
        )}

        {canClose && (
          <button type="button" className="btn-secondary !h-9 !text-xs" disabled={busyCode !== null} onClick={() => void prepareYears()}>
            {busyCode === 'prepare' ? 'Preparing…' : 'Prepare fiscal years through next year'}
          </button>
        )}

        <ul className="space-y-2">
          {years.map((year) => {
            const statusNum = normalizeFiscalYearCloseStatus(year.status);
            const isClosed = statusNum === 3;
            const showClose =
              canClose &&
              canEnableCloseButton({
                status: year.status,
                blockingCount: 0,
                canClosePermission: true,
              }) &&
              statusNum === 2;
            const showReopen = canClose && isClosed;

            return (
              <li
                key={year.fiscalYearCode}
                className="flex flex-col sm:flex-row sm:items-center justify-between gap-2 p-3 rounded-xl bg-[var(--bg-subtle)] text-sm"
              >
                <div>
                  <div className="font-semibold text-[var(--text-main)]">
                    {year.fiscalYearCode}{' '}
                    <span className="text-xs font-normal text-[var(--text-muted)]">
                      ({year.startDate} – {year.endDate})
                    </span>
                  </div>
                  <div className="text-xs text-[var(--text-secondary)]">
                    {fiscalYearStatusLabel(year.status)}
                    {year.closedBy ? ` · ${year.closedBy}` : ''}
                    {year.closedAt ? ` · ${year.closedAt}` : ''}
                  </div>
                </div>
                <div className="flex flex-wrap gap-2 shrink-0">
                  {showClose && (
                    <button
                      type="button"
                      className="btn-primary !h-9 !text-xs"
                      disabled={busyCode === year.fiscalYearCode}
                      onClick={() => void openCloseDialog(year.fiscalYearCode)}
                    >
                      {busyCode === year.fiscalYearCode ? (
                        <Loader2 className="w-4 h-4 animate-spin" aria-hidden="true" />
                      ) : null}
                      {reviewAndCloseLabel}
                    </button>
                  )}
                  {isClosed && (
                    <button type="button" className="btn-secondary !h-9 !text-xs" onClick={() => void downloadPackage(year.fiscalYearCode)}>
                      Download close package
                    </button>
                  )}
                  {showReopen && (
                    <button
                      type="button"
                      className="btn-secondary !h-9 !text-xs"
                      disabled={busyCode === year.fiscalYearCode}
                      onClick={() => void openReopenDialog(year.fiscalYearCode)}
                    >
                      <RotateCcw className="w-4 h-4" aria-hidden="true" /> Reopen
                    </button>
                  )}
                  {!canClose && statusNum === 2 && (
                    <span className="text-xs text-[var(--text-muted)] self-center">
                      View only — close permission required
                    </span>
                  )}
                </div>
              </li>
            );
          })}
        </ul>
      </div>

      {closeDialog && (
        <ModalShell
          open
          onClose={() => setCloseDialog(null)}
          bare
          aria-labelledby="year-end-close-title"
          panelClassName="w-full max-w-lg rounded-2xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] p-6 shadow-2xl space-y-4 max-h-[min(90vh,calc(100dvh-2rem))] overflow-y-auto"
        >
          <h2 id="year-end-close-title" className="text-base font-bold text-[var(--text-main)]">
            Close fiscal year {closeDialog.preview.fiscalYearCode}
          </h2>
          <div className="space-y-4 text-sm">
            <p className="text-[var(--text-secondary)]">
              {closeDialog.preview.startDate} – {closeDialog.preview.endDate}
            </p>
            <CheckList items={closeDialog.preview.blocking} tone="blocking" />
            <CheckList items={closeDialog.preview.warnings} tone="warning" />
            {!!closeDialog.preview.partnerLossNotice && <p className="rounded-lg bg-amber-500/10 p-3 text-xs text-amber-700">{closeDialog.preview.partnerLossNotice}</p>}
            {!!closeDialog.preview.partnerAllocations?.length && <div className="overflow-x-auto">
              <table className="w-full text-xs"><thead><tr className="text-left border-b border-[var(--border-subtle)]"><th className="py-2 pr-2">Partner</th><th className="py-2 pr-2">Share %</th><th className="py-2 pr-2">Profit share</th><th className="py-2 pr-2">Drawings closed</th><th className="py-2">Capital after close</th></tr></thead>
                <tbody>{closeDialog.preview.partnerAllocations.map(row => <tr key={row.partnerId} className="border-b border-[var(--border-subtle)]"><td className="py-2 pr-2">{row.name}</td><td className="py-2 pr-2">{row.percent.toFixed(4)}%</td><td className="py-2 pr-2">{formatAmount(row.profitShare)}</td><td className="py-2 pr-2">{formatAmount(row.drawingsClosed)}</td><td className="py-2">{formatAmount(row.capitalAfterClose)}</td></tr>)}</tbody></table>
            </div>}
            {closeDialog.preview.warnings.length > 0 && canClose && (
              <label className="flex items-start gap-2 text-xs text-[var(--text-main)] cursor-pointer">
                <input
                  type="checkbox"
                  className="mt-0.5"
                  checked={closeDialog.acknowledgeWarnings}
                  onChange={(e) =>
                    setCloseDialog({ ...closeDialog, acknowledgeWarnings: e.target.checked, stale: false })
                  }
                />
                <span>I acknowledge these warnings and want to proceed.</span>
              </label>
            )}
            {closeDialog.preview.warnings.length > 0 && canClose && (
              <div>
                <label htmlFor="year-end-warning-reason" className={labelClass}>Reason for closing with warnings</label>
                <textarea
                  id="year-end-warning-reason"
                  className={inputClass + ' h-24 py-2'}
                  maxLength={2000}
                  value={closeDialog.warningReason}
                  onChange={(e) => setCloseDialog({ ...closeDialog, warningReason: e.target.value })}
                />
              </div>
            )}
            {closeDialog.stale && (
              <button type="button" className="btn-secondary !h-9 !text-xs" onClick={() => void openCloseDialog(closeDialog.code)}>
                Refresh review
              </button>
            )}
            {closeDialog.preview.closingLines.length > 0 && (
              <div className="overflow-x-auto">
                <table className="w-full text-xs">
                  <thead>
                    <tr className="text-left text-[var(--text-muted)] border-b border-[var(--border-subtle)]">
                      <th className="py-2 pr-2">Account</th>
                      <th className="py-2 pr-2">Dr/Cr</th>
                      <th className="py-2 text-right">Amount</th>
                    </tr>
                  </thead>
                  <tbody>
                    {closeDialog.preview.closingLines.map((line) => (
                      <tr key={`${line.accountId}-${line.drCr}`} className="border-b border-[var(--border-subtle)]">
                        <td className="py-2 pr-2">
                          {line.accountCode} {line.accountName}
                        </td>
                        <td className="py-2 pr-2">{line.drCr}</td>
                        <td className="py-2 text-right font-mono">{formatAmount(line.amount)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
          <div className="flex justify-end gap-2 pt-2">
            <button type="button" className="btn-secondary" onClick={() => setCloseDialog(null)}>
              Cancel
            </button>
            {canClose && (
              <button
                type="button"
                className="btn-primary"
                disabled={
                  !canEnableCloseButton({
                    status: closeDialog.preview.status,
                    blockingCount: closeDialog.preview.blocking.length,
                    canClosePermission: canClose,
                  }) ||
                  !warningCloseReady(closeDialog.preview.warnings.length, closeDialog.acknowledgeWarnings, closeDialog.warningReason) ||
                  closeDialog.stale ||
                  busyCode === closeDialog.code
                }
                onClick={() => void submitClose()}
              >
                {busyCode === closeDialog.code ? 'Closing…' : reviewAndCloseLabel}
              </button>
            )}
          </div>
        </ModalShell>
      )}

      {reopenDialog && canClose && (
        <ModalShell
          open
          onClose={() => setReopenDialog(null)}
          bare
          aria-labelledby="year-end-reopen-title"
          panelClassName="w-full max-w-md rounded-2xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] p-6 shadow-2xl space-y-4"
        >
          <h2 id="year-end-reopen-title" className="text-base font-bold text-[var(--text-main)]">
            Reopen fiscal year {reopenDialog.code}
          </h2>
          {reopenDialog.preview.packageNote && (
            <p className="text-sm text-[var(--text-secondary)]">{reopenDialog.preview.packageNote}</p>
          )}
          {reopenDialog.preview.issuanceStatus !== 'Unissued' && reopenDialog.preview.issuanceStatus !== 'Issued' && (
            <div className="space-y-2">
              <p className="text-sm text-[var(--text-secondary)]">Declare whether the annual financial statements were issued before this year can be reopened.</p>
              <button type="button" className="btn-secondary !h-9 !text-xs" onClick={() => void declareUnissued(reopenDialog.code, reopenDialog.reason || 'Statements were not issued.').catch((err) => setMessage({ kind: 'error', text: err instanceof Error ? err.message : 'Could not record the declaration.' }))}>
                Declare unissued
              </button>
            </div>
          )}
          {reopenDialog.preview.reopenBlockedReason && (
            <p className="text-sm text-amber-700">{reopenDialog.preview.reopenBlockedReason}</p>
          )}
          <p className="text-sm text-[var(--text-secondary)]">
            Current lock: {reopenDialog.preview.currentBooksLockedThrough ?? 'no lock'}. After reopening: {reopenDialog.preview.proposedBooksLockedThrough ?? 'no lock'}. A later lock will not be restored automatically.
          </p>
          {reopenDialog.preview.newlyUnlockedFrom && reopenDialog.preview.newlyUnlockedThrough && (
            <p className="text-sm text-[var(--text-secondary)]">
              Newly unlocked: {reopenDialog.preview.newlyUnlockedFrom} through {reopenDialog.preview.newlyUnlockedThrough}, inclusive.
            </p>
          )}
          {reopenDialog.preview.requiresLockAcknowledgement && (
            <label className="flex items-start gap-2 text-sm text-[var(--text-secondary)]">
              <input type="checkbox" checked={reopenDialog.acknowledge} onChange={(e) => setReopenDialog({ ...reopenDialog, acknowledge: e.target.checked })} />
              I acknowledge that reopening moves an independent lock backward or clears it.
            </label>
          )}
          <div>
            <label htmlFor="year-end-reopen-reason" className={labelClass}>
              Reason (required)
            </label>
            <input
              id="year-end-reopen-reason"
              type="text"
              className={inputClass}
              value={reopenDialog.reason}
              onChange={(e) => setReopenDialog({ ...reopenDialog, reason: e.target.value })}
              placeholder="Why are you reopening this year?"
            />
          </div>
          <div className="flex justify-end gap-2">
            <button type="button" className="btn-secondary" onClick={() => setReopenDialog(null)}>
              Cancel
            </button>
            <button
              type="button"
              className="btn-primary"
              disabled={!reopenDialog.reason.trim() || (reopenDialog.preview.requiresLockAcknowledgement && !reopenDialog.acknowledge) || busyCode === reopenDialog.code}
              onClick={() => void submitReopen()}
            >
              {busyCode === reopenDialog.code ? 'Reopening…' : 'Reopen year'}
            </button>
          </div>
        </ModalShell>
      )}
    </>
  );
};

/** Close surface for a member who can review or close a year without organization administration. */
export function YearEndClosePage() {
  const { currentOrg } = useTenantContext();
  const { hasPermission } = usePermissions();
  const canInspect = hasPermission(['financials.periods.close', 'financials.accounts.view']);
  if (!canInspect) return <PermissionDeniedCard permission="financials.periods.close" />;
  return (
    <div className="space-y-4">
      <PageHeader
        title="Year-end close"
        back={{
          to: `/org/${currentOrg.slug}/financials`,
          label: currentOrg.isPersonalBooks ? 'Transactions' : 'General Ledger',
        }}
      />
      <YearEndCloseCard canClose={hasPermission('financials.periods.close')} personal={!!currentOrg.isPersonalBooks} />
    </div>
  );
}
