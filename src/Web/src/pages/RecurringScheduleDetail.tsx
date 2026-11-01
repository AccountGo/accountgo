// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

import React, { useCallback, useEffect, useState } from 'react';
import { Link, useOutletContext, useParams } from 'react-router-dom';
import {
  AlertTriangle, Ban, CheckCircle2, FileEdit, Loader2, Pause, Play, Play as Run,
  RotateCcw, SkipForward, X,
} from 'lucide-react';
import { useAuth } from '../auth/AuthContext';
import { usePermissions, ReadOnlyModuleBanner } from '../components/Gating';
import { PageHeader, HeaderButton, HeaderMenu, StatusPill } from '../components/PageHeader';
import { ModalShell } from '../components/ModalShell';
import type { TenantContextType } from '../types';
import type { RecurringOccurrenceView, RecurringScheduleDetail } from '../types/recurring';
import {
  cadenceLabel, formatRunDate, occurrenceStatusLabel, occurrenceStatusTone, scheduleStatusTone,
} from '../lib/recurring';

export const RecurringScheduleDetailPage: React.FC = () => {
  const { canWrite } = useOutletContext<TenantContextType>();
  const { hasPermission } = usePermissions();
  const { authFetch } = useAuth();
  const { orgSlug, scheduleId } = useParams<{ orgSlug: string; scheduleId: string }>();

  const salesWritable = canWrite('sales');
  const canManage = hasPermission('sales.invoices.create') && salesWritable;
  const canPost = hasPermission('sales.invoices.post') && salesWritable;

  const [schedule, setSchedule] = useState<RecurringScheduleDetail | null>(null);
  const [occurrences, setOccurrences] = useState<RecurringOccurrenceView[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  // Dialog state: which action's confirm modal is open.
  const [dialog, setDialog] = useState<null | 'skip' | 'pause' | 'end' | 'run'>(null);
  const [reason, setReason] = useState('');

  const load = useCallback(async () => {
    setIsLoading(true);
    try {
      const [schedRes, occRes] = await Promise.all([
        authFetch(`/api/RecurringSchedules/${scheduleId}`),
        authFetch(`/api/RecurringSchedules/${scheduleId}/occurrences`),
      ]);
      if (schedRes.status === 404) {
        setError('This recurring schedule was not found.');
        setSchedule(null);
        return;
      }
      if (!schedRes.ok) throw new Error(`Failed to load the schedule (${schedRes.status}).`);
      setSchedule(await schedRes.json());
      setOccurrences(occRes.ok ? await occRes.json() : []);
    } catch (err: any) {
      setError(err.message || 'The schedule could not be loaded.');
    } finally {
      setIsLoading(false);
    }
  }, [authFetch, scheduleId]);

  useEffect(() => {
    void load();
  }, [load]);

  const act = async (
    label: string,
    url: string,
    body?: unknown,
    okMessage?: string,
  ) => {
    setBusy(label);
    setError(null);
    setSuccess(null);
    try {
      const res = await authFetch(url, {
        method: 'POST',
        headers: body ? { 'Content-Type': 'application/json' } : undefined,
        body: body ? JSON.stringify(body) : undefined,
      });
      if (!res.ok) {
        const data = await res.json().catch(() => null);
        throw new Error(data?.message || `The action could not be completed (${res.status}).`);
      }
      // Run/skip return a body with an outcome; pause/resume/end return 204.
      let outcomeMsg = okMessage ?? 'Done.';
      if (res.status !== 204) {
        const data = await res.json().catch(() => null);
        if (data?.outcome) outcomeMsg = outcomeMessage(data.outcome, data.message) ?? outcomeMsg;
      }
      setSuccess(outcomeMsg);
      setDialog(null);
      setReason('');
      await load();
    } catch (err: any) {
      setError(err.message || 'The action could not be completed.');
    } finally {
      setBusy(null);
    }
  };

  const runNow = () =>
    act('run', `/api/RecurringSchedules/${scheduleId}/run`,
      schedule?.nextRunDate ? { runDate: schedule.nextRunDate } : {}, 'Run started.');
  const doSkip = () =>
    act('skip', `/api/RecurringSchedules/${scheduleId}/skip`,
      { runDate: schedule?.nextRunDate, reason: reason.trim() }, 'Run skipped.');
  const doPause = () =>
    act('pause', `/api/RecurringSchedules/${scheduleId}/pause`, { reason: reason.trim() || undefined }, 'Schedule paused.');
  const doResume = () =>
    act('resume', `/api/RecurringSchedules/${scheduleId}/resume`, undefined, 'Schedule resumed.');
  const doEnd = () =>
    act('end', `/api/RecurringSchedules/${scheduleId}/end`, { reason: reason.trim() || undefined }, 'Schedule ended.');
  const retry = (occurrenceId: number) =>
    act(`retry-${occurrenceId}`, `/api/RecurringSchedules/occurrences/${occurrenceId}/retry`, undefined, 'Retry done.');

  if (isLoading) {
    return (
      <div className="max-w-5xl mx-auto px-4 py-16 flex items-center justify-center text-[var(--text-muted)]">
        <Loader2 className="w-5 h-5 animate-spin mr-2" /> Loading…
      </div>
    );
  }

  if (!schedule) {
    return (
      <div className="max-w-5xl mx-auto px-4 py-10 space-y-4">
        <div role="alert" className="p-4 bg-rose-500/10 border border-rose-500/20 text-rose-500 rounded-xl text-sm">
          {error ?? 'This recurring schedule was not found.'}
        </div>
        <Link to={`/org/${orgSlug}/sales/recurring`} className="text-sm text-[var(--brand-primary)] underline">
          Back to recurring invoices
        </Link>
      </div>
    );
  }

  const active = schedule.status === 'Active';
  const paused = schedule.status === 'Paused';
  const ended = schedule.status === 'Ended';
  // Run-now generates the run server-side. For an auto-issue schedule the server requires the post permission and
  // denies (it does not downgrade), so only offer run-now when the user can actually complete it.
  const canRunNow = canManage && active && (schedule.action !== 'AutoIssue' || canPost);

  return (
    <div className="max-w-5xl mx-auto px-4 sm:px-6 py-6 space-y-6 text-[var(--text-main)]">
      <ReadOnlyModuleBanner module="sales" orgSlug={orgSlug} />

      <PageHeader
        back={{ to: `/org/${orgSlug}/sales/recurring`, label: 'Back to recurring invoices' }}
        title={schedule.name}
        badges={
          <>
            <StatusPill tone={scheduleStatusTone(schedule.status)}>{schedule.status}</StatusPill>
            {schedule.action === 'AutoIssue' && <StatusPill tone="blue">Auto-issue</StatusPill>}
            {schedule.needsAttention && (
              <StatusPill tone="amber" icon={AlertTriangle}>Needs attention</StatusPill>
            )}
            {schedule.customerArchived && <StatusPill tone="red">Client archived</StatusPill>}
          </>
        }
        subtitle={[schedule.customerName, cadenceLabel(schedule.cadence, schedule.interval)].filter(Boolean).join(' · ')}
        actions={
          <>
            {canRunNow && (
              <HeaderButton
                label="Run now"
                icon={Run}
                variant="primary"
                busy={busy === 'run'}
                onClick={() => setDialog('run')}
              />
            )}
            {canManage && paused && (
              <HeaderButton label="Resume" icon={Play} variant="primary" busy={busy === 'resume'} onClick={doResume} />
            )}
            {canManage && (
              <HeaderMenu
                items={[
                  !ended && { label: 'Edit schedule', icon: FileEdit, to: `/org/${orgSlug}/sales/recurring/${schedule.id}/edit` },
                  active && { label: 'Skip next run', icon: SkipForward, onSelect: () => setDialog('skip') },
                  active && { label: 'Pause schedule', icon: Pause, onSelect: () => setDialog('pause') },
                  !ended && 'separator',
                  !ended && { label: 'End schedule', icon: Ban, danger: true, onSelect: () => setDialog('end') },
                ]}
              />
            )}
          </>
        }
      />

      {error && (
        <div role="alert" className="p-4 bg-rose-500/10 border border-rose-500/20 text-rose-500 rounded-xl flex items-start gap-3">
          <AlertTriangle className="w-5 h-5 shrink-0 mt-0.5" />
          <div className="flex-1 text-sm font-medium">{error}</div>
          <button onClick={() => setError(null)} className="p-1 hover:opacity-75"><X className="w-4 h-4" /></button>
        </div>
      )}
      {success && (
        <div className="p-4 bg-emerald-500/10 border border-emerald-500/20 text-emerald-500 rounded-xl flex items-center gap-3">
          <CheckCircle2 className="w-5 h-5 shrink-0" />
          <div className="flex-1 text-sm font-medium">{success}</div>
          <button onClick={() => setSuccess(null)} className="p-1 hover:opacity-75"><X className="w-4 h-4" /></button>
        </div>
      )}

      {/* Summary + upcoming dates */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
        <SummaryCard label="Next run">
          <span className={schedule.isOverdue ? 'text-amber-600 dark:text-amber-400' : ''}>
            {formatRunDate(schedule.nextRunDate)}{schedule.isOverdue ? ' (due)' : ''}
          </span>
        </SummaryCard>
        <SummaryCard label="Runs so far">
          {schedule.occurrenceCount}{schedule.maxOccurrences ? ` of ${schedule.maxOccurrences}` : ''}
        </SummaryCard>
        <SummaryCard label="Window">
          {formatRunDate(schedule.startDate)} → {schedule.endDate ? formatRunDate(schedule.endDate) : 'no end'}
        </SummaryCard>
      </div>

      {schedule.upcomingDates.length > 0 && !ended && (
        <div className="p-4 rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)]">
          <h3 className="text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-2">Upcoming runs</h3>
          <div className="flex flex-wrap gap-2">
            {schedule.upcomingDates.map((d) => (
              <span key={d} className="px-2.5 py-1 rounded-lg bg-[var(--bg-subtle)] text-sm text-[var(--text-secondary)]">
                {formatRunDate(d)}
              </span>
            ))}
          </div>
        </div>
      )}

      {/* Occurrence history */}
      <div className="border border-[var(--border-subtle)] rounded-xl bg-[var(--bg-surface)] overflow-hidden">
        <div className="px-4 py-3 border-b border-[var(--border-subtle)]">
          <h3 className="text-sm font-semibold">History</h3>
        </div>
        {occurrences.length === 0 ? (
          <div className="p-8 text-center text-sm text-[var(--text-muted)]">No runs yet.</div>
        ) : (
          <table className="w-full text-left border-collapse">
            <thead>
              <tr className="bg-[var(--bg-subtle)] text-xs font-semibold text-[var(--text-muted)] uppercase tracking-wider border-b border-[var(--border-subtle)]">
                <th className="py-3 px-4">Run date</th>
                <th className="py-3 px-4">Status</th>
                <th className="py-3 px-4">Detail</th>
                <th className="py-3 px-4">Invoice</th>
                <th className="py-3 px-4 text-right">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-[var(--border-subtle)] text-sm">
              {occurrences.map((o) => {
                const canRetry = canManage && o.status === 'Failed' && !o.salesInvoiceId;
                const detail = o.status === 'Skipped'
                  ? o.skipReason
                  : o.status === 'Failed'
                    ? o.errorCode
                    : o.guardFailureReason;
                return (
                  <tr key={o.id} className="align-middle">
                    <td className="py-3 px-4 text-[var(--text-secondary)]">{formatRunDate(o.scheduledRunDate)}</td>
                    <td className="py-3 px-4">
                      <StatusPill tone={occurrenceStatusTone(o.status)}>{occurrenceStatusLabel(o.status)}</StatusPill>
                    </td>
                    <td className="py-3 px-4 text-[var(--text-muted)] max-w-xs truncate" title={detail ?? undefined}>
                      {detail ?? '—'}
                    </td>
                    <td className="py-3 px-4">
                      {o.salesInvoiceId ? (
                        <Link to={`/org/${orgSlug}/sales/${o.salesInvoiceId}`} className="text-[var(--brand-primary)] underline">
                          View
                        </Link>
                      ) : '—'}
                    </td>
                    <td className="py-3 px-4 text-right">
                      {canRetry ? (
                        <button
                          type="button"
                          onClick={() => retry(o.id)}
                          disabled={busy === `retry-${o.id}`}
                          className="inline-flex items-center gap-1.5 px-2.5 py-1.5 text-xs font-semibold text-[var(--brand-primary)] bg-[var(--brand-subtle)] rounded-lg hover:opacity-85 transition disabled:opacity-50"
                        >
                          {busy === `retry-${o.id}` ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <RotateCcw className="w-3.5 h-3.5" />}
                          Retry
                        </button>
                      ) : '—'}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        )}
      </div>

      {/* Confirm dialogs */}
      <ReasonDialog
        open={dialog === 'skip'}
        title="Skip the next run"
        description={`Skip ${formatRunDate(schedule.nextRunDate)}? No invoice is created and the schedule advances to the next date.`}
        reasonRequired
        reason={reason}
        setReason={setReason}
        confirmLabel="Skip run"
        busy={busy === 'skip'}
        onConfirm={doSkip}
        onClose={() => { setDialog(null); setReason(''); }}
      />
      <ReasonDialog
        open={dialog === 'pause'}
        title="Pause this schedule"
        description="It will stop generating invoices until you resume it. Existing invoices are untouched."
        reason={reason}
        setReason={setReason}
        confirmLabel="Pause schedule"
        busy={busy === 'pause'}
        onConfirm={doPause}
        onClose={() => { setDialog(null); setReason(''); }}
      />
      <ReasonDialog
        open={dialog === 'end'}
        title="End this schedule"
        description="This is permanent — it cannot be resumed. Existing invoices are untouched."
        danger
        reason={reason}
        setReason={setReason}
        confirmLabel="End schedule"
        busy={busy === 'end'}
        onConfirm={doEnd}
        onClose={() => { setDialog(null); setReason(''); }}
      />
      <ModalShell open={dialog === 'run'} onClose={() => setDialog(null)} maxWidthClassName="max-w-md">
        <div className="p-6 space-y-4">
          <h2 className="text-lg font-semibold">Run now</h2>
          <p className="text-sm text-[var(--text-secondary)]">
            Generate the invoice for {formatRunDate(schedule.nextRunDate)} now?
            {schedule.action === 'AutoIssue' && ' It will be issued if all checks pass.'}
          </p>
          <div className="flex justify-end gap-2">
            <button onClick={() => setDialog(null)} className="px-3.5 py-2 text-sm rounded-lg border border-[var(--border-subtle)] hover:bg-[var(--bg-surface-hover)]">
              Cancel
            </button>
            <button
              onClick={runNow}
              disabled={busy === 'run'}
              className="inline-flex items-center gap-1.5 px-3.5 py-2 text-sm font-semibold rounded-lg bg-[var(--brand-primary)] text-[var(--brand-text)] hover:bg-[var(--brand-hover)] disabled:opacity-50"
            >
              {busy === 'run' && <Loader2 className="w-4 h-4 animate-spin" />}
              Run now
            </button>
          </div>
        </div>
      </ModalShell>
    </div>
  );
};

const SummaryCard: React.FC<{ label: string; children: React.ReactNode }> = ({ label, children }) => (
  <div className="p-4 rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)]">
    <div className="text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1">{label}</div>
    <div className="text-sm font-medium text-[var(--text-main)]">{children}</div>
  </div>
);

const ReasonDialog: React.FC<{
  open: boolean;
  title: string;
  description: string;
  reason: string;
  setReason: (v: string) => void;
  confirmLabel: string;
  reasonRequired?: boolean;
  danger?: boolean;
  busy?: boolean;
  onConfirm: () => void;
  onClose: () => void;
}> = ({ open, title, description, reason, setReason, confirmLabel, reasonRequired, danger, busy, onConfirm, onClose }) => (
  <ModalShell open={open} onClose={onClose} maxWidthClassName="max-w-md">
    <div className="p-6 space-y-4">
      <h2 className="text-lg font-semibold">{title}</h2>
      <p className="text-sm text-[var(--text-secondary)]">{description}</p>
      <div>
        <label className="block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1.5">
          Reason{reasonRequired ? '' : ' (optional)'}
        </label>
        <textarea
          rows={2}
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          placeholder={reasonRequired ? 'Why is this run being skipped?' : 'Add a note for the audit trail…'}
          className="w-full px-3 py-2 text-sm bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-lg focus:ring-2 focus:ring-[var(--brand-ring)] focus:outline-none"
        />
      </div>
      <div className="flex justify-end gap-2">
        <button onClick={onClose} className="px-3.5 py-2 text-sm rounded-lg border border-[var(--border-subtle)] hover:bg-[var(--bg-surface-hover)]">
          Cancel
        </button>
        <button
          onClick={onConfirm}
          disabled={busy || (reasonRequired && reason.trim() === '')}
          className={`inline-flex items-center gap-1.5 px-3.5 py-2 text-sm font-semibold rounded-lg disabled:opacity-50 ${
            danger
              ? 'bg-rose-600 text-white hover:bg-rose-700'
              : 'bg-[var(--brand-primary)] text-[var(--brand-text)] hover:bg-[var(--brand-hover)]'
          }`}
        >
          {busy && <Loader2 className="w-4 h-4 animate-spin" />}
          {confirmLabel}
        </button>
      </div>
    </div>
  </ModalShell>
);

/** Turns a generation outcome into a friendly message. */
function outcomeMessage(outcome: string, message?: string | null): string | null {
  switch (outcome) {
    case 'DraftCreated':
      return 'Draft invoice created.';
    case 'Issued':
      return 'Invoice issued.';
    case 'DraftCreatedGuardFailed':
      return message ? `Draft created for review: ${message}` : 'Draft created for review.';
    case 'Skipped':
      return 'Run skipped.';
    case 'AlreadyProcessed':
      return 'That run was already generated.';
    case 'Failed':
      return message || 'The run could not be completed.';
    case 'Aborted':
      return message || 'No change was made.';
    default:
      return null;
  }
}

export default RecurringScheduleDetailPage;
