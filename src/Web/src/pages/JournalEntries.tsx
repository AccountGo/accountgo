import { useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { Plus, RefreshCw } from 'lucide-react';
import { useAuth } from '../auth/AuthContext';
import { PermissionDeniedCard, useTenantContext } from '../components/Gating';
import { HeaderButton, PageHeader } from '../components/PageHeader';
import { currencySymbol } from '../lib/currency';
import { getDateRange } from './Reports';

interface JournalLine { id: number; accountId: number; drCr: number; amount: number; memo: string | null }
export interface JournalEntry {
  id: number;
  journalDate: string;
  voucherType: number;
  referenceNo: string | null;
  memo: string | null;
  posted: boolean | null;
  journalEntryLines: JournalLine[];
}
interface JournalAccount { id: number; accountCode: string; accountName: string; canPost: boolean; childAccounts: JournalAccount[] }
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
async function read<T>(fetcher: Fetcher, path: string, init?: RequestInit): Promise<T> {
  const response = await fetcher(path, init);
  if (!response.ok) throw new Error(`Journal request failed (HTTP ${response.status}): ${await response.text()}`);
  return response.json();
}
const inputClass = 'w-full rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-surface)] px-3 py-2 text-sm text-[var(--text-main)] focus:ring-2 focus:ring-[var(--brand-ring)]';
const voucherTypes = ['Opening balances', 'Closing entries', 'Adjustment entries', 'Correction entries', 'Transfer entries', 'Other'];

export function JournalEditor({ initial, accounts, canCreate, canPost, fetcher, onSaved, currency }: {
  initial: JournalEntry; accounts: JournalAccount[]; canCreate: boolean; canPost: boolean;
  fetcher: Fetcher; onSaved: (id: number) => void; currency: string | undefined;
}) {
  const [entry, setEntry] = useState(initial);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [dirty, setDirty] = useState(false);
  const editable = canCreate && !entry.posted && !busy;
  const totals = journalTotals(entry);
  const update = (next: JournalEntry) => { setEntry(next); setDirty(true); setError(''); };
  const updateLine = (index: number, values: Partial<JournalLine>) =>
    update({ ...entry, journalEntryLines: entry.journalEntryLines.map((line, i) => i === index ? { ...line, ...values } : line) });
  const submit = async (post: boolean) => {
    setBusy(true); setError('');
    try {
      const result = await read<{ id: number }>(fetcher, `/api/Financials/${post ? 'PostJournalEntry' : 'SaveJournalEntry'}`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(entry),
      });
      onSaved(post ? entry.id : result.id);
    } catch (error) {
      setError(error instanceof Error ? error.message : 'Unable to save journal.');
      setBusy(false);
    }
  };
  const money = (cents: number) => `${currencySymbol(currency)}${(cents / 100).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
  return <section aria-label="Journal entry" className="space-y-4 rounded-2xl bg-[var(--bg-surface)] p-5">
    <h2 className="text-lg font-semibold">{entry.id ? `Journal #${entry.id}` : 'New Journal Entry'} - {entry.posted ? 'Posted (read-only)' : 'Draft'}</h2>
    <form className="space-y-4" onSubmit={event => { event.preventDefault(); void submit(false); }}>
      <fieldset disabled={!editable} className="grid gap-4 sm:grid-cols-2">
        <label className="grid gap-1 text-sm">Journal date<input type="date" required className={inputClass} value={entry.journalDate.slice(0, 10)}
          onChange={event => update({ ...entry, journalDate: event.target.value })} /></label>
        <label className="grid gap-1 text-sm">Voucher type<select className={inputClass} value={entry.voucherType}
          onChange={event => update({ ...entry, voucherType: Number(event.target.value) })}>
          {voucherTypes.map((label, index) => <option key={label} value={index + 1}>{label}</option>)}
        </select></label>
        <label className="grid gap-1 text-sm">Reference<input className={inputClass} value={entry.referenceNo || ''} onChange={event => update({ ...entry, referenceNo: event.target.value })} /></label>
        <label className="grid gap-1 text-sm">Memo<input className={inputClass} value={entry.memo || ''} onChange={event => update({ ...entry, memo: event.target.value })} /></label>
      </fieldset>
      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <caption className="sr-only">Manual journal debit and credit lines</caption>
          <thead><tr>{['Account', 'Side', 'Amount', 'Line memo', 'Actions'].map(label => <th scope="col" className="p-2 text-left" key={label}>{label}</th>)}</tr></thead>
          <tbody>{entry.journalEntryLines.map((line, index) => <tr key={index}>
            <td className="min-w-56 p-2"><select aria-label={`Account line ${index + 1}`} required disabled={!editable} className={inputClass} value={line.accountId || ''}
              onChange={event => updateLine(index, { accountId: Number(event.target.value) })}>
              <option value="">Select account</option>{accounts.filter(a => a.canPost || a.id === line.accountId).map(account =>
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
        {canCreate && <button type="submit" disabled={!editable} className="rounded-lg bg-[var(--brand-primary)] px-4 py-2 font-semibold text-[var(--brand-text)] disabled:opacity-50">{busy ? 'Saving...' : 'Save draft'}</button>}
        {canPost && <HeaderButton label="Post journal" disabled={busy || !entry.id || dirty || !canPostJournal(entry)} onClick={() => void submit(true)} />}
      </div>}
      {!entry.posted && canPost && (!entry.id || dirty) && <p className="text-sm text-[var(--text-secondary)]">Save the draft before posting.</p>}
    </form>
  </section>;
}

export default function JournalEntries() {
  const { currentOrg, hasPermission } = useTenantContext();
  const { authFetch } = useAuth();
  const [params, setParams] = useSearchParams();
  const [status, setStatus] = useState('draft');
  const [revision, setRevision] = useState(0);
  const [result, setResult] = useState<{ key: string; entries: JournalEntry[]; accounts: JournalAccount[] } | null>(null);
  const [failure, setFailure] = useState<{ key: string; message: string } | null>(null);
  const allowed = hasPermission('financials.journal.view');
  const canCreate = hasPermission('financials.journal.create');
  const canPost = hasPermission('financials.journal.post');
  const key = JSON.stringify([currentOrg.slug, revision]);
  const data = result?.key === key ? result : null;
  const error = failure?.key === key ? failure.message : null;
  useEffect(() => {
    if (!allowed) return;
    let cancelled = false;
    Promise.all([
      read<JournalEntry[]>(authFetch, '/api/Financials/JournalEntries'),
      read<JournalAccount[]>(authFetch, '/api/Financials/Accounts'),
    ]).then(([entries, accounts]) => {
      if (!Array.isArray(entries) || !Array.isArray(accounts)) throw new Error('The journals API returned an invalid response.');
      if (!cancelled) setResult({ key, entries, accounts });
    }).catch(error => {
      if (!cancelled) setFailure({ key, message: error instanceof Error ? error.message : 'Unable to load manual journals.' });
    });
    return () => { cancelled = true; };
  }, [allowed, authFetch, key]);
  if (!allowed) return <PermissionDeniedCard permission="financials.journal.view" />;
  const isNew = params.has('new');
  const selected = data?.entries.find(entry => String(entry.id) === params.get('id'));
  const initial: JournalEntry | undefined = isNew ? {
    id: 0, journalDate: getDateRange('all', new Date()).to, memo: '', referenceNo: '', voucherType: 6, posted: false,
    journalEntryLines: [1, 2].map(drCr => ({ id: 0, accountId: 0, drCr, amount: 0, memo: '' })),
  } : selected;
  const flatten = (accounts: JournalAccount[]): JournalAccount[] => accounts.flatMap(account => [account, ...flatten(account.childAccounts || [])]);
  return <div className="space-y-6 text-[var(--text-main)]">
    <PageHeader title="Manual Journal Entries" subtitle="Draft vouchers are separate from the posted General Ledger"
      back={{ to: `/org/${currentOrg.slug}/financials`, label: 'General Ledger' }} actions={<>
        <HeaderButton label="Refresh" icon={RefreshCw} disabled={!data && !error} onClick={() => setRevision(r => r + 1)} />
        {canCreate && <HeaderButton label="New Journal Entry" icon={Plus} variant="primary" onClick={() => setParams({ new: '1' })} />}
      </>} />
    {error ? <p role="alert" className="text-red-500">{error} Use Refresh to retry.</p>
      : !data ? <p role="status">Loading manual journals...</p>
      : <>
        {isNew && !canCreate ? <PermissionDeniedCard permission="financials.journal.create" />
          : initial ? <JournalEditor key={`${key}-${isNew ? 'new' : initial.id}`} initial={initial} accounts={flatten(data.accounts)} canCreate={canCreate} canPost={canPost}
            fetcher={authFetch} currency={currentOrg.currency} onSaved={id => { setParams({ id: String(id) }); setRevision(r => r + 1); }} />
            : params.has('id') && <p role="alert">Journal entry not found in this workspace.</p>}
        <nav aria-label="Manual journal status" className="flex gap-3">{['draft', 'posted'].map(value =>
          <button type="button" key={value} aria-pressed={status === value} className="rounded-lg border border-[var(--border-subtle)] px-4 py-2" onClick={() => setStatus(value)}>{value === 'draft' ? 'Drafts' : 'Posted manual journals'}</button>)}</nav>
        <div className="overflow-x-auto rounded-2xl bg-[var(--bg-surface)]">
          <table className="w-full text-sm"><caption className="sr-only">{status === 'draft' ? 'Draft journals (not in posted books)' : 'Posted manual journals'}</caption>
            <thead><tr>{['Journal', 'Date', 'Reference', 'Memo', 'Status'].map(label => <th scope="col" key={label} className="p-4 text-left">{label}</th>)}</tr></thead>
            <tbody>{data.entries.filter(entry => !!entry.posted === (status === 'posted')).map(entry => <tr key={entry.id} className="border-t border-[var(--border-subtle)]">
              <th scope="row" className="p-4 text-left"><Link className="underline" to={`?id=${entry.id}`}>Open #{entry.id}</Link></th><td className="p-4">{entry.journalDate.slice(0, 10)}</td>
              <td className="p-4">{entry.referenceNo}</td><td className="p-4">{entry.memo}</td><td className="p-4">{entry.posted ? 'Posted' : 'Draft'}</td>
            </tr>)}</tbody>
          </table>
          {!data.entries.some(entry => !!entry.posted === (status === 'posted')) && <p role="status" className="p-4">No {status} manual journals.</p>}
        </div>
      </>}
  </div>;
}
