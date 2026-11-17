import { useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { Check, Copy, Plus, RefreshCw, Trash2 } from 'lucide-react';
import { useAuth } from '../auth/AuthContext';
import ConfirmDialog from '../components/ConfirmDialog';
import { PermissionDeniedCard, ReadOnlyModuleBanner, useTenantContext } from '../components/Gating';
import { HeaderButton, PageHeader } from '../components/PageHeader';
import { RecordHistoryAction } from '../components/RecordHistoryPanel';
import { currencySymbol } from '../lib/currency';
import {
  journalVoucherTypeLabel,
  PRIOR_PERIOD_ADJUSTMENT,
  SELECTABLE_JOURNAL_VOUCHER_TYPES,
} from '../lib/journalVoucherTypes';
import { withConfirmReconciledPeriod } from '../lib/reconciledPeriod';
import { useReconciledPeriodConfirm } from '../lib/useReconciledPeriodConfirm';
import { getDateRange } from './Reports';

interface JournalLine { id: number; accountId: number; drCr: number; amount: number; memo: string | null }
export interface JournalEntry {
  id: number;
  journalDate: string;
  voucherType: number;
  adjustsFiscalYearId?: number | null;
  adjustsFiscalYearCode?: string | null;
  referenceNo: string | null;
  memo: string | null;
  posted: boolean | null;
  sourceSystem?: string | null;
  sourceKey?: string | null;
  journalEntryLines: JournalLine[];
}

export type JournalSourceFilter = 'all' | 'imported' | 'manual';

export interface PostBatchItemResult {
  id: number;
  posted: boolean;
  code?: string | null;
  message?: string | null;
}

export function filterJournalEntries(
  entries: JournalEntry[],
  status: 'draft' | 'posted',
  sourceFilter: JournalSourceFilter,
  range?: { from?: string; to?: string },
): JournalEntry[] {
  return entries.filter((entry) => {
    const day = (entry.journalDate || '').slice(0, 10);
    if (range?.from && day < range.from) return false;
    if (range?.to && day > range.to) return false;
    if (!!entry.posted !== (status === 'posted')) return false;
    const isImported = Boolean(entry.sourceSystem);
    if (sourceFilter === 'imported') return isImported;
    if (sourceFilter === 'manual') return !isImported;
    return true;
  });
}
interface JournalAccount { id: number; accountCode: string; accountName: string; canPost: boolean; archived?: boolean; childAccounts: JournalAccount[] }
interface ClosedFiscalYear { id: number; fiscalYearCode: string; status: number | string }
type Fetcher = (url: string, init?: RequestInit) => Promise<Response>;

export function journalTotals(entry: JournalEntry) {
  const sum = (side: number) => entry.journalEntryLines.filter(line => line.drCr === side)
    .reduce((total, line) => total + Math.round((line.amount + Number.EPSILON) * 100), 0);
  return { debit: sum(1), credit: sum(2) };
}
export function canPostJournal(entry: JournalEntry) {
  const totals = journalTotals(entry);
  return !entry.posted && entry.journalEntryLines.length >= 2 && totals.debit > 0 && totals.debit === totals.credit
    && entry.journalEntryLines.every(line => line.accountId > 0 && Number.isFinite(line.amount) && line.amount > 0 && [1, 2].includes(line.drCr));
}

export function emptyJournalMessage(status: 'draft' | 'posted', sourceFilter: 'all' | 'imported' | 'manual'): string {
  if (status === 'posted') {
    if (sourceFilter === 'imported') return 'No imported posted journals.';
    if (sourceFilter === 'manual') return 'No manual posted journals.';
    return 'No posted journals.';
  }
  if (sourceFilter === 'imported') return 'No imported drafts.';
  if (sourceFilter === 'manual') return 'No draft manual journals.';
  return 'No draft journals.';
}
const JOURNAL_POST_OPERATION_KEY = 'financials.journal.post';
const APPROVAL_REQUEST_HEADER = 'X-Approval-Request-Id';
const APPROVER_USER_HEADER = 'X-Approved-By-User-Id';
const ACCESS_APPROVALS_PATH = '/api/organizations/current/members/access-approvals';

/** Headers that carry an independent reviewer's recorded approval when posting a journal. */
export function journalApprovalHeaders(approvalRequestId: string, approverUserId: string): Record<string, string> {
  const requestId = approvalRequestId.trim();
  const approverId = approverUserId.trim();
  return requestId && approverId ? { [APPROVAL_REQUEST_HEADER]: requestId, [APPROVER_USER_HEADER]: approverId } : {};
}

async function problemText(response: Response): Promise<string> {
  const text = await response.text();
  try {
    const problem: { detail?: string; title?: string } = JSON.parse(text);
    return problem.detail || problem.title || text;
  } catch {
    return text;
  }
}

async function read<T>(fetcher: Fetcher, path: string, init?: RequestInit): Promise<T> {
  const response = await fetcher(path, init);
  if (!response.ok) throw new Error(`Journal request failed (HTTP ${response.status}): ${await response.text()}`);
  return response.json();
}
const inputClass = 'w-full rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-surface)] px-3 py-2 text-sm text-[var(--text-main)] focus:ring-2 focus:ring-[var(--brand-ring)]';

const PPA_HELP =
  'For a material error in a closed year whose statements were already filed. Post the correction against account 30820 and the balance-sheet accounts. Immaterial corrections go through this year\'s accounts as a normal entry. Unfiled closed years can be reopened under Year-end instead.';

export function JournalEditor({ initial, accounts, closedYears, canCreate, canPost, fetcher, onSaved, onDiscarded, currency, independentReview = false, canReview = false }: {
  initial: JournalEntry; accounts: JournalAccount[]; closedYears: ClosedFiscalYear[];
  canCreate: boolean; canPost: boolean;
  fetcher: Fetcher; onSaved: (id: number) => void; onDiscarded?: () => void; currency: string | undefined;
  independentReview?: boolean; canReview?: boolean;
}) {
  const [entry, setEntry] = useState(initial);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [dirty, setDirty] = useState(false);
  const [approvalRequestId, setApprovalRequestId] = useState('');
  const [approverUserId, setApproverUserId] = useState('');
  const [reviewNotice, setReviewNotice] = useState('');
  const [copiedKey, setCopiedKey] = useState(false);
  const [confirmDiscard, setConfirmDiscard] = useState(false);
  const [discardError, setDiscardError] = useState<string | null>(null);
  const isImported = Boolean(entry.sourceSystem);
  const editable = canCreate && !entry.posted && !isImported && !busy;
  const totals = journalTotals(entry);
  const isPpa = entry.voucherType === PRIOR_PERIOD_ADJUSTMENT;
  const update = (next: JournalEntry) => { setEntry(next); setDirty(true); setError(''); };
  const updateLine = (index: number, values: Partial<JournalLine>) =>
    update({ ...entry, journalEntryLines: entry.journalEntryLines.map((line, i) => i === index ? { ...line, ...values } : line) });
  const periodConfirm = useReconciledPeriodConfirm();
  const voucherOptions = SELECTABLE_JOURNAL_VOUCHER_TYPES.slice();
  // Keep displaying Closing entries (2) for existing system journals even though it is not selectable.
  if (entry.voucherType === 2 && !voucherOptions.some(o => o.value === 2)) {
    voucherOptions.unshift({ value: 2, label: journalVoucherTypeLabel(2) });
  }
  const submit = async (post: boolean, confirmReconciledPeriod = false) => {
    setBusy(true); setError('');
    try {
      const path = post
        ? withConfirmReconciledPeriod('/api/Financials/PostJournalEntry', confirmReconciledPeriod)
        : '/api/Financials/SaveJournalEntry';
      const payload = {
        ...entry,
        adjustsFiscalYearId: isPpa ? entry.adjustsFiscalYearId : null,
      };
      const approvalHeaders = post && independentReview ? journalApprovalHeaders(approvalRequestId, approverUserId) : {};
      const response = await fetcher(path, {
        method: 'POST', headers: { 'Content-Type': 'application/json', ...approvalHeaders }, body: JSON.stringify(payload),
      });
      if (post && await periodConfirm.handleConflict(response, () => void submit(true, true))) {
        setBusy(false);
        return;
      }
      if (!response.ok) throw new Error(`Journal request failed (HTTP ${response.status}): ${await problemText(response)}`);
      const result: { id: number } = await response.json();
      onSaved(post ? entry.id : result.id);
    } catch (error) {
      setError(error instanceof Error ? error.message : 'Unable to save journal.');
      setBusy(false);
    }
  };
  const recordReview = async () => {
    setBusy(true); setError(''); setReviewNotice('');
    try {
      const response = await fetcher(ACCESS_APPROVALS_PATH, {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ operationKey: JOURNAL_POST_OPERATION_KEY, resourceId: String(entry.id) }),
      });
      if (!response.ok) throw new Error(await problemText(response));
      const approval: { approvalRequestId: string; approverUserId: string } = await response.json();
      setReviewNotice(`Approval recorded. Give the preparer approval request ID ${approval.approvalRequestId} and approver user ID ${approval.approverUserId}. It is valid for a limited time.`);
    } catch (reviewError) {
      setError(reviewError instanceof Error ? reviewError.message : 'Unable to record the review.');
    } finally {
      setBusy(false);
    }
  };
  const copySourceKey = async () => {
    if (!entry.sourceKey) return;
    try {
      await navigator.clipboard?.writeText(entry.sourceKey);
      setCopiedKey(true);
      setTimeout(() => setCopiedKey(false), 2000);
    } catch {
      /* clipboard may be unavailable in non-secure contexts */
    }
  };
  const discardImported = async () => {
    if (!entry.id) return;
    setBusy(true); setDiscardError(null);
    try {
      const response = await fetcher(`/api/Financials/JournalEntries/${entry.id}/DiscardImported`, { method: 'POST' });
      if (!response.ok) throw new Error(await problemText(response));
      setConfirmDiscard(false);
      onDiscarded?.();
    } catch (err) {
      setDiscardError(err instanceof Error ? err.message : 'Unable to discard imported journal.');
    } finally {
      setBusy(false);
    }
  };
  const showReviewPanel = independentReview && !entry.posted && !!entry.id && !dirty && (canPost || canReview);
  const money = (cents: number) => `${currencySymbol(currency)}${(cents / 100).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
  return <>
  <section aria-label="Journal entry" className="space-y-4 rounded-2xl bg-[var(--bg-surface)] p-5">
    <div className="flex flex-wrap items-center justify-between gap-2">
      <h2 className="text-lg font-semibold">{entry.id ? `Journal #${entry.id}` : 'New Journal Entry'} - {entry.posted ? 'Posted (read-only)' : isImported ? 'Imported draft (read-only)' : 'Draft'}</h2>
      {isImported && <span className="inline-flex items-center rounded-full border border-[var(--border-subtle)] px-2.5 py-0.5 text-xs font-medium">Imported · {entry.sourceSystem}</span>}
    </div>
    {isImported && (
      <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-base)] p-3 text-sm">
        <div className="min-w-0 space-y-0.5">
          <p className="text-xs text-[var(--text-secondary)]">Source: <span className="font-medium text-[var(--text-main)]">{entry.sourceSystem}</span></p>
          {entry.sourceKey && <p className="truncate font-mono text-xs text-[var(--text-main)]" title={entry.sourceKey}>{entry.sourceKey}</p>}
        </div>
        {entry.sourceKey && (
          <button type="button" onClick={() => void copySourceKey()}
            className="inline-flex items-center gap-1.5 rounded-lg border border-[var(--border-subtle)] px-3 py-1.5 text-xs font-medium hover:bg-[var(--bg-surface-hover)]">
            {copiedKey ? <Check className="h-3.5 w-3.5" /> : <Copy className="h-3.5 w-3.5" />}
            {copiedKey ? 'Copied' : 'Copy key'}
          </button>
        )}
      </div>
    )}
    <form className="space-y-4" onSubmit={event => { event.preventDefault(); void submit(false); }}>
      <fieldset disabled={!editable} className="grid gap-4 sm:grid-cols-2">
        <label className="grid gap-1 text-sm">Journal date<input type="date" required className={inputClass} value={entry.journalDate.slice(0, 10)}
          onChange={event => update({ ...entry, journalDate: event.target.value })} /></label>
        <label className="grid gap-1 text-sm">Voucher type<select className={inputClass} value={entry.voucherType}
          onChange={event => {
            const voucherType = Number(event.target.value);
            update({
              ...entry,
              voucherType,
              adjustsFiscalYearId: voucherType === PRIOR_PERIOD_ADJUSTMENT ? entry.adjustsFiscalYearId : null,
            });
          }}>
          {voucherOptions.map(option => <option key={option.value} value={option.value}>{option.label}</option>)}
        </select></label>
        <label className="grid gap-1 text-sm">Reference<input className={inputClass} value={entry.referenceNo || ''} onChange={event => update({ ...entry, referenceNo: event.target.value })} /></label>
        <label className="grid gap-1 text-sm">Memo<input className={inputClass} value={entry.memo || ''} onChange={event => update({ ...entry, memo: event.target.value })} required={isPpa} /></label>
        {isPpa && <>
          <label className="grid gap-1 text-sm sm:col-span-2">Corrects fiscal year
            <select
              className={inputClass}
              required
              disabled={!editable || closedYears.length === 0}
              value={entry.adjustsFiscalYearId ?? ''}
              onChange={event => update({ ...entry, adjustsFiscalYearId: Number(event.target.value) || null })}>
              <option value="">{closedYears.length === 0 ? 'No closed years yet' : 'Select closed year'}</option>
              {closedYears.map(year => <option key={year.id} value={year.id}>{year.fiscalYearCode}</option>)}
            </select>
          </label>
          {closedYears.length === 0
            ? <p className="text-sm text-[var(--text-secondary)] sm:col-span-2">Close a fiscal year under Year-end before posting a prior-period adjustment.</p>
            : <p className="text-sm text-[var(--text-secondary)] sm:col-span-2">{PPA_HELP}</p>}
        </>}
      </fieldset>
      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <caption className="sr-only">Manual journal debit and credit lines</caption>
          <thead><tr>{['Account', 'Side', 'Amount', 'Line memo', 'Actions'].map(label => <th scope="col" className="p-2 text-left" key={label}>{label}</th>)}</tr></thead>
          <tbody>{entry.journalEntryLines.map((line, index) => <tr key={index}>
            <td className="min-w-56 p-2"><select aria-label={`Account line ${index + 1}`} required disabled={!editable} className={inputClass} value={line.accountId || ''}
              onChange={event => updateLine(index, { accountId: Number(event.target.value) })}>
              <option value="">Select account</option>{accounts.filter(a => (a.canPost || a.id === line.accountId) && (!a.archived || a.id === line.accountId)).map(account =>
                <option key={account.id} value={account.id}>{account.accountCode} - {account.accountName}</option>)}
            </select></td>
            <td className="p-2"><select aria-label={`Side line ${index + 1}`} disabled={!editable} className={inputClass} value={line.drCr} onChange={event => updateLine(index, { drCr: Number(event.target.value) })}>
              <option value={1}>Debit</option><option value={2}>Credit</option></select></td>
            <td className="min-w-32 p-2"><input aria-label={`Amount line ${index + 1}`} type="number" min="0.01" step="0.01" required disabled={!editable}
              className={inputClass} value={line.amount || ''} onChange={event => updateLine(index, { amount: Number(event.target.value) })} /></td>
            <td className="min-w-40 p-2"><input aria-label={`Memo line ${index + 1}`} disabled={!editable} className={inputClass} value={line.memo || ''} onChange={event => updateLine(index, { memo: event.target.value })} /></td>
            <td className="p-2">{editable && <button type="button" disabled={entry.journalEntryLines.length <= 2} className="rounded p-2 underline disabled:opacity-50"
              aria-label={`Remove line ${index + 1}`} onClick={() => update({ ...entry, journalEntryLines: entry.journalEntryLines.filter((_, i) => i !== index) })}>Remove</button>}</td>
          </tr>)}</tbody>
        </table>
      </div>
      {editable && <HeaderButton label="Add line" icon={Plus} onClick={() => update({ ...entry, journalEntryLines: [...entry.journalEntryLines, { id: 0, accountId: 0, drCr: 1, amount: 0, memo: '' }] })} />}
      <p role="status" className={totals.debit === totals.credit ? 'text-emerald-600' : 'text-red-500'}>
        Debit {money(totals.debit)} / Credit {money(totals.credit)} - {totals.debit === totals.credit ? 'In balance' : 'Out of balance'}
      </p>
      {error && <p role="alert" className="text-red-500">{error}</p>}
      {!entry.posted && <div className="flex flex-wrap gap-3">
        {canCreate && !isImported && <button type="submit" disabled={!editable} className="rounded-lg bg-[var(--brand-primary)] px-4 py-2 font-semibold text-[var(--brand-text)] disabled:opacity-50">{busy ? 'Saving...' : 'Save draft'}</button>}
        {canPost && <HeaderButton label="Post journal" disabled={busy || !entry.id || dirty || !canPostJournal(entry)} onClick={() => void submit(true)} />}
        {canCreate && isImported && !!entry.id && (
          <HeaderButton label="Discard" icon={Trash2} disabled={busy} onClick={() => { setDiscardError(null); setConfirmDiscard(true); }} />
        )}
      </div>}
      {!entry.posted && canPost && (!entry.id || dirty) && <p className="text-sm text-[var(--text-secondary)]">Save the draft before posting.</p>}
      {showReviewPanel && <fieldset className="space-y-3 rounded-lg border border-[var(--border-subtle)] p-4">
        <legend className="px-1 text-sm font-semibold">Independent review required</legend>
        <p className="text-sm text-[var(--text-secondary)]">A different Owner or Admin who can post journals must approve this entry before it can be posted. You cannot approve a journal you prepared.</p>
        {canReview && <div className="space-y-2">
          <HeaderButton label="Record my review approval" disabled={busy} onClick={() => void recordReview()} />
          {reviewNotice && <p role="status" className="text-sm text-emerald-600">{reviewNotice}</p>}
        </div>}
        {canPost && <div className="grid gap-3 sm:grid-cols-2">
          <label className="text-sm">Approval request ID
            <input className="mt-1 w-full rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-base)] p-2" value={approvalRequestId} onChange={e => setApprovalRequestId(e.target.value)} />
          </label>
          <label className="text-sm">Approver user ID
            <input className="mt-1 w-full rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-base)] p-2" value={approverUserId} onChange={e => setApproverUserId(e.target.value)} />
          </label>
        </div>}
      </fieldset>}
    </form>
  </section>
  {confirmDiscard && (
    <ConfirmDialog
      title="Discard imported journal?"
      message={`This removes draft #${entry.id} (${entry.sourceSystem}: ${entry.sourceKey ?? ''}) and frees its source key so it can be imported again.`}
      confirmLabel="Discard draft"
      destructive
      busy={busy}
      error={discardError}
      onConfirm={() => void discardImported()}
      onCancel={() => setConfirmDiscard(false)}
    />
  )}
  {periodConfirm.dialog}
  </>;
}

export default function JournalEntries() {
  const { currentOrg, hasPermission, canWrite } = useTenantContext();
  const { authFetch } = useAuth();
  const [params, setParams] = useSearchParams();
  const statusFromLink = params.get('status');
  const [status, setStatus] = useState<'draft' | 'posted'>(statusFromLink === 'posted' ? 'posted' : 'draft');
  const journalFrom = params.get('from') || '';
  const journalTo = params.get('to') || '';
  const [sourceFilter, setSourceFilter] = useState<JournalSourceFilter>('all');
  const [selectedIds, setSelectedIds] = useState<number[]>([]);
  const [batchBusy, setBatchBusy] = useState(false);
  const [batchError, setBatchError] = useState('');
  const [batchResults, setBatchResults] = useState<PostBatchItemResult[] | null>(null);
  const [revision, setRevision] = useState(0);
  const [result, setResult] = useState<{ key: string; entries: JournalEntry[]; accounts: JournalAccount[]; closedYears: ClosedFiscalYear[] } | null>(null);
  const [failure, setFailure] = useState<{ key: string; message: string } | null>(null);
  const allowed = hasPermission('financials.journal.view');
  const financialsWritable = canWrite('financials');
  // Journals are not a metered create resource; gate on canWrite(financials) ∧ permission.
  const canCreate = hasPermission('financials.journal.create') && financialsWritable;
  const canPost = hasPermission('financials.journal.post') && financialsWritable;
  const key = JSON.stringify([currentOrg.slug, revision]);
  const data = result?.key === key ? result : null;
  const error = failure?.key === key ? failure.message : null;
  useEffect(() => {
    if (!allowed) return;
    let cancelled = false;
    Promise.all([
      read<JournalEntry[]>(authFetch, '/api/Financials/JournalEntries'),
      read<JournalAccount[]>(authFetch, '/api/Financials/Accounts'),
      read<ClosedFiscalYear[]>(authFetch, '/api/Financials/YearEnd').catch(() => [] as ClosedFiscalYear[]),
    ]).then(([entries, accounts, years]) => {
      if (!Array.isArray(entries) || !Array.isArray(accounts)) throw new Error('The journals API returned an invalid response.');
      const closedYears = (Array.isArray(years) ? years : []).filter(y => y.status === 3 || y.status === 'Closed');
      if (!cancelled) setResult({ key, entries, accounts, closedYears });
    }).catch(error => {
      if (!cancelled) setFailure({ key, message: error instanceof Error ? error.message : 'Unable to load manual journals.' });
    });
    return () => { cancelled = true; };
  }, [allowed, authFetch, key]);
  if (!allowed) return <PermissionDeniedCard permission="financials.journal.view" />;
  const isNew = params.has('new');
  const selected = data?.entries.find(entry => String(entry.id) === params.get('id'));
  const initial: JournalEntry | undefined = isNew ? {
    id: 0, journalDate: getDateRange('all', new Date()).to, memo: '', referenceNo: '', voucherType: 6,
    adjustsFiscalYearId: null, posted: false,
    journalEntryLines: [1, 2].map(drCr => ({ id: 0, accountId: 0, drCr, amount: 0, memo: '' })),
  } : selected;
  const flatten = (accounts: JournalAccount[]): JournalAccount[] => accounts.flatMap(account => [account, ...flatten(account.childAccounts || [])]);
  const visibleEntries = data ? filterJournalEntries(data.entries, status, sourceFilter, { from: journalFrom, to: journalTo }) : [];
  const draftVisibleIds = status === 'draft' ? visibleEntries.filter(e => !e.posted).map(e => e.id) : [];
  const selectedDraftIds = selectedIds.filter(id => draftVisibleIds.includes(id));
  const toggleSelect = (id: number) =>
    setSelectedIds(prev => prev.includes(id) ? prev.filter(x => x !== id) : [...prev, id]);
  const toggleSelectAll = () =>
    setSelectedIds(prev => selectedDraftIds.length === draftVisibleIds.length ? prev.filter(id => !draftVisibleIds.includes(id)) : Array.from(new Set([...prev, ...draftVisibleIds])));
  const postBatch = async () => {
    if (selectedDraftIds.length === 0) return;
    setBatchBusy(true); setBatchError(''); setBatchResults(null);
    try {
      const response = await authFetch('/api/Financials/JournalEntries/PostBatch', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ ids: selectedDraftIds }),
      });
      if (!response.ok) throw new Error(await problemText(response));
      const results: PostBatchItemResult[] = await response.json();
      setBatchResults(results);
      const postedIds = new Set(results.filter(r => r.posted).map(r => r.id));
      if (postedIds.size > 0) {
        setSelectedIds(prev => prev.filter(id => !postedIds.has(id)));
        setRevision(r => r + 1);
      }
    } catch (err) {
      setBatchError(err instanceof Error ? err.message : 'Unable to post selected journals.');
    } finally {
      setBatchBusy(false);
    }
  };
  return <div className="space-y-6 text-[var(--text-main)]">
    <ReadOnlyModuleBanner module="financials" orgSlug={currentOrg.slug} />
    <PageHeader title="Manual Journal Entries" subtitle="Draft vouchers are separate from the posted General Ledger"
      back={{ to: `/org/${currentOrg.slug}/financials`, label: 'General Ledger' }} actions={<>
        <HeaderButton label="Refresh" icon={RefreshCw} disabled={!data && !error} onClick={() => setRevision(r => r + 1)} />
        {canCreate && <HeaderButton label="New Journal Entry" icon={Plus} variant="primary" onClick={() => setParams({ new: '1' })} />}
      </>} />
    {error ? <p role="alert" className="text-red-500">{error} Use Refresh to retry.</p>
      : !data ? <p role="status">Loading manual journals...</p>
      : <>
        {!isNew && selected && <div className="flex justify-end"><RecordHistoryAction entityType="JournalEntryHeader" entityId={selected.id} /></div>}
        {isNew && !canCreate ? <PermissionDeniedCard permission="financials.journal.create" />
          : initial ? <JournalEditor key={`${key}-${isNew ? 'new' : initial.id}`} initial={initial} accounts={flatten(data.accounts)}
            closedYears={data.closedYears} canCreate={canCreate} canPost={canPost}
            independentReview={currentOrg.operatingPolicy?.journalWorkflow !== 'SingleActorAllowed'}
            canReview={canPost && (currentOrg.role === 'Owner' || currentOrg.role === 'Admin')}
            fetcher={authFetch} currency={currentOrg.currency}
            onSaved={id => { setParams({ id: String(id) }); setRevision(r => r + 1); }}
            onDiscarded={() => { setParams({}); setRevision(r => r + 1); }} />
            : params.has('id') && <p role="alert">Journal entry not found in this workspace.</p>}
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex flex-wrap items-center gap-3">
            <nav aria-label="Manual journal status" className="flex gap-2">{(['draft', 'posted'] as const).map(value =>
              <button type="button" key={value} aria-pressed={status === value} className="rounded-lg border border-[var(--border-subtle)] px-4 py-2 text-sm" onClick={() => setStatus(value)}>{value === 'draft' ? 'Drafts' : 'Posted manual journals'}</button>)}</nav>
            <nav aria-label="Journal source filter" className="flex gap-2">{([
              { value: 'all', label: 'All' },
              { value: 'imported', label: 'Imported' },
              { value: 'manual', label: 'Manual' },
            ] as const).map(option =>
              <button type="button" key={option.value} aria-pressed={sourceFilter === option.value}
                className="rounded-lg border border-[var(--border-subtle)] px-3 py-2 text-sm"
                onClick={() => setSourceFilter(option.value)}>{option.label}</button>)}</nav>
          </div>
          {status === 'draft' && canPost && (
            <button type="button" disabled={batchBusy || selectedDraftIds.length === 0}
              onClick={() => void postBatch()}
              className="rounded-lg bg-[var(--brand-primary)] px-4 py-2 text-sm font-semibold text-[var(--brand-text)] disabled:opacity-50">
              {batchBusy ? 'Posting...' : `Post selected (${selectedDraftIds.length})`}
            </button>
          )}
        </div>
        {batchError && <p role="alert" className="text-sm text-red-500">{batchError}</p>}
        {batchResults && (
          <div role="status" className="space-y-1 rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] p-4 text-sm">
            <p className="font-medium">
              Batch post finished: {batchResults.filter(r => r.posted).length} posted, {batchResults.filter(r => !r.posted).length} failed.
            </p>
            {batchResults.filter(r => !r.posted).map(r => (
              <p key={r.id} className="text-red-500">Journal #{r.id}: {r.message || r.code || 'Failed to post.'}</p>
            ))}
          </div>
        )}
        <div className="overflow-x-auto rounded-2xl bg-[var(--bg-surface)]">
          <table className="w-full text-sm"><caption className="sr-only">{status === 'draft' ? 'Draft journals (not in posted books)' : 'Posted manual journals'}</caption>
            <thead><tr>
              {status === 'draft' && canPost && (
                <th scope="col" className="w-10 p-4 text-left">
                  <input type="checkbox" aria-label="Select all draft journals"
                    checked={draftVisibleIds.length > 0 && selectedDraftIds.length === draftVisibleIds.length}
                    disabled={draftVisibleIds.length === 0 || batchBusy}
                    onChange={toggleSelectAll} />
                </th>
              )}
              {['Journal', 'Date', 'Type', 'Corrected year', 'Reference', 'Memo', 'Status'].map(label => <th scope="col" key={label} className="p-4 text-left">{label}</th>)}
            </tr></thead>
            <tbody>{visibleEntries.map(entry => <tr key={entry.id} className="border-t border-[var(--border-subtle)]">
              {status === 'draft' && canPost && (
                <td className="p-4">
                  <input type="checkbox" aria-label={`Select journal #${entry.id}`}
                    checked={selectedDraftIds.includes(entry.id)}
                    disabled={batchBusy}
                    onChange={() => toggleSelect(entry.id)} />
                </td>
              )}
              <th scope="row" className="p-4 text-left">
                <div className="flex flex-wrap items-center gap-2">
                  <Link className="underline" to={`?id=${entry.id}`}>Open #{entry.id}</Link>
                  {entry.sourceSystem && (
                    <span className="inline-flex items-center rounded-full border border-[var(--border-subtle)] px-2 py-0.5 text-xs font-medium text-[var(--text-secondary)]">
                      Imported · {entry.sourceSystem}
                    </span>
                  )}
                </div>
              </th>
              <td className="p-4">{entry.journalDate.slice(0, 10)}</td>
              <td className="p-4">{journalVoucherTypeLabel(entry.voucherType)}</td>
              <td className="p-4">{entry.adjustsFiscalYearCode || '—'}</td>
              <td className="p-4">{entry.referenceNo}</td><td className="p-4">{entry.memo}</td><td className="p-4">{entry.posted ? 'Posted' : 'Draft'}</td>
            </tr>)}</tbody>
          </table>
          {visibleEntries.length === 0 && <p role="status" className="p-4">{emptyJournalMessage(status, sourceFilter)}</p>}
        </div>
      </>}
  </div>;
}
