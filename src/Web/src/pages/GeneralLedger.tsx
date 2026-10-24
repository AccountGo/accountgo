import { Fragment, useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { ChevronDown, ChevronRight, Download, Plus, RefreshCw } from 'lucide-react';
import { useAuth } from '../auth/AuthContext';
import { PermissionDeniedCard, useTenantContext } from '../components/Gating';
import { HeaderButton, PageHeader } from '../components/PageHeader';
import { currencySymbol } from '../lib/currency';
import { getDateRange, reportQuery, type FinancialYear } from './Reports';

export interface GlLine {
  id: number;
  accountId: number;
  accountCode: string;
  accountName: string;
  debit: number;
  credit: number;
}
export interface GlTransaction {
  id: number;
  date: string;
  documentType: string;
  documentTypeLabel: string;
  description: string | null;
  sourceDocumentId: number | null;
  sourcePath: string | null;
  reversedGeneralLedgerHeaderId: number | null;
  totalDebit: number;
  totalCredit: number;
  lines: GlLine[];
}

export function ledgerFilters(params: URLSearchParams, today = new Date()) {
  const defaults = getDateRange('month', today);
  const explicitPeriod = params.has('from') || params.has('to');
  return {
    from: explicitPeriod ? (params.get('from') || '').slice(0, 10) : defaults.from,
    to: explicitPeriod ? (params.get('to') || '').slice(0, 10) : defaults.to,
    accountCode: params.get('accountCode') || '',
    transactionNo: params.get('transactionNo') || '',
    documentType: params.get('documentType') || '',
  };
}

export function ledgerQuery(filters: ReturnType<typeof ledgerFilters>) {
  const query = new URLSearchParams(reportQuery(filters));
  for (const name of ['accountCode', 'transactionNo', 'documentType'] as const)
    if (filters[name]) query.set(name, filters[name]);
  return `?${query}`;
}

export function searchLedger(entries: GlTransaction[], search: string) {
  const query = search.trim().toLocaleLowerCase();
  return entries.filter(entry => [String(entry.id), entry.description || '', ...entry.lines.flatMap(l => [l.accountCode, l.accountName])]
    .some(value => value.toLocaleLowerCase().includes(query)));
}

export function ledgerCsv(entries: GlTransaction[]) {
  const rows = [
    ['Transaction', 'Date', 'Document type', 'Description', 'Source ID', 'Reverses transaction', 'Total debit', 'Total credit', 'Line ID', 'Account code', 'Account name', 'Debit', 'Credit'],
    ...entries.flatMap(entry => {
      const header = [String(entry.id), entry.date, entry.documentTypeLabel, entry.description || '',
        String(entry.sourceDocumentId ?? ''), String(entry.reversedGeneralLedgerHeaderId ?? ''), entry.totalDebit.toFixed(2), entry.totalCredit.toFixed(2)];
      return entry.lines.length ? entry.lines.map(line => [...header, String(line.id), line.accountCode, line.accountName, line.debit.toFixed(2), line.credit.toFixed(2)])
        : [[...header, '', '', '', '', '']];
    }),
  ];
  return rows.map(row => row.map(value => {
    const safe = /^\s*[=+@\t\r-]/.test(value) && !/^-?\d+(\.\d+)?$/.test(value) ? `'${value}` : value;
    return `"${safe.replaceAll('"', '""')}"`;
  }).join(',')).join('\r\n');
}

export function isLedgerBalanced(entry: GlTransaction) {
  const cents = (amount: number) => Math.round((amount + Number.EPSILON) * 100);
  return entry.lines.length >= 2 && cents(entry.totalDebit) === cents(entry.totalCredit)
    && cents(entry.lines.reduce((sum, l) => sum + l.debit, 0)) === cents(entry.totalDebit)
    && cents(entry.lines.reduce((sum, l) => sum + l.credit, 0)) === cents(entry.totalCredit);
}

export function LedgerRows({ entries, slug, formatAmount }: {
  entries: GlTransaction[]; slug: string; formatAmount: (value: number) => string;
}) {
  const [expanded, setExpanded] = useState<Set<number>>(new Set());
  return <table className="w-full text-sm">
    <caption className="sr-only">Posted General Ledger transactions with debit and credit totals</caption>
    <thead><tr>{['Date / Transaction', 'Type', 'Description', 'Source', 'Debit', 'Credit'].map(label =>
      <th scope="col" key={label} className="p-4 text-left">{label}</th>)}</tr></thead>
    <tbody>{entries.map(entry => {
      const open = expanded.has(entry.id);
      const balanced = isLedgerBalanced(entry);
      return <Fragment key={entry.id}>
        <tr className="border-t border-[var(--border-subtle)]">
          <th scope="row" className="p-4 text-left">
            <button type="button" aria-expanded={open} aria-controls={`gl-${entry.id}`} className="flex min-h-10 items-center gap-2 rounded focus-visible:ring-2 focus-visible:ring-[var(--brand-ring)]"
              onClick={() => setExpanded(previous => {
                const next = new Set(previous);
                if (next.has(entry.id)) next.delete(entry.id); else next.add(entry.id);
                return next;
              })}>
              {open ? <ChevronDown aria-hidden="true" size={16} /> : <ChevronRight aria-hidden="true" size={16} />}
              {entry.date.slice(0, 10)} / #{entry.id}
            </button>
          </th>
          <td className="p-4">{entry.documentTypeLabel}</td>
          <td className="p-4">{entry.description || '\u2014'}
            {entry.reversedGeneralLedgerHeaderId != null && <div className="text-amber-600">Reversal of <Link className="underline" to={`/org/${slug}/financials?${new URLSearchParams({ transactionNo: String(entry.reversedGeneralLedgerHeaderId), from: '', to: '' })}`}>#{entry.reversedGeneralLedgerHeaderId}</Link></div>}
            {!balanced && <p role="alert" className="text-red-500">Out of balance: review this posted transaction.</p>}
          </td>
          <td className="p-4">{entry.sourcePath ? <Link className="text-[var(--brand-primary)] underline" to={`/org/${slug}${entry.sourcePath}`}>Open source #{entry.sourceDocumentId}</Link>
            : entry.sourceDocumentId ? `${entry.documentTypeLabel} #${entry.sourceDocumentId}` : '\u2014'}</td>
          <td className="p-4 text-right tabular-nums">{formatAmount(entry.totalDebit)}</td>
          <td className="p-4 text-right tabular-nums">{formatAmount(entry.totalCredit)}</td>
        </tr>
        <tr id={`gl-${entry.id}`} hidden={!open}><td colSpan={6} className="bg-[var(--bg-subtle)] p-4">
          <table className="w-full text-sm">
            <caption className="pb-3 text-left font-semibold">Transaction #{entry.id} account lines</caption>
            <thead><tr>{['Account code', 'Account name', 'Debit', 'Credit'].map(label => <th scope="col" key={label} className="p-2 text-left">{label}</th>)}</tr></thead>
            <tbody>{entry.lines.map(line => <tr key={line.id}>
              <td className="p-2 font-mono">{line.accountCode}</td><th scope="row" className="p-2 text-left font-normal">{line.accountName}</th>
              <td className="p-2 text-right tabular-nums">{line.debit ? formatAmount(line.debit) : '\u2014'}</td>
              <td className="p-2 text-right tabular-nums">{line.credit ? formatAmount(line.credit) : '\u2014'}</td>
            </tr>)}</tbody>
            <tfoot><tr className="font-semibold"><th scope="row" colSpan={2} className="p-2 text-left">Totals ({balanced ? 'In balance' : 'Out of balance'})</th>
              <td className="p-2 text-right">{formatAmount(entry.totalDebit)}</td><td className="p-2 text-right">{formatAmount(entry.totalCredit)}</td></tr></tfoot>
          </table>
        </td></tr>
      </Fragment>;
    })}</tbody>
  </table>;
}

export default function GeneralLedger() {
  const { currentOrg, hasPermission } = useTenantContext();
  const { authFetch } = useAuth();
  const [params, setParams] = useSearchParams();
  const filters = ledgerFilters(params);
  const preset = params.get('period') || (params.has('from') || params.has('to') ? 'custom' : 'month');
  const [search, setSearch] = useState('');
  const [revision, setRevision] = useState(0);
  const [result, setResult] = useState<{ key: string; entries: GlTransaction[]; years: FinancialYear[] } | null>(null);
  const [failure, setFailure] = useState<{ key: string; message: string } | null>(null);
  const allowed = hasPermission('financials.journal.view');
  const valid = (!filters.from || /^\d{4}-\d{2}-\d{2}$/.test(filters.from))
    && (!filters.to || /^\d{4}-\d{2}-\d{2}$/.test(filters.to))
    && (!filters.from || !filters.to || filters.from <= filters.to)
    && (!filters.transactionNo || /^[1-9]\d*$/.test(filters.transactionNo));
  const query = ledgerQuery(filters);
  const key = JSON.stringify([currentOrg.slug, query, revision]);
  const data = result?.key === key ? result : null;
  const error = failure?.key === key ? failure.message : null;
  const loading = allowed && valid && !data && !error;
  useEffect(() => {
    if (!allowed || !valid) return;
    let cancelled = false;
    async function load() {
      const [response, yearsResponse] = await Promise.all([
        authFetch(`/api/Financials/GeneralLedger${query}`), authFetch('/api/Administration/financialyears'),
      ]);
      if (!response.ok || !yearsResponse.ok) throw new Error(`Unable to load General Ledger (HTTP ${!response.ok ? response.status : yearsResponse.status}).`);
      const entries: GlTransaction[] = await response.json();
      const years: FinancialYear[] = await yearsResponse.json();
      if (!Array.isArray(entries) || !Array.isArray(years)) throw new Error('The ledger API returned an invalid response.');
      if (!cancelled) setResult({ key, entries, years });
    }
    load().catch(error => { if (!cancelled) setFailure({ key, message: error instanceof Error ? error.message : 'Unable to load General Ledger.' }); });
    return () => { cancelled = true; };
  }, [allowed, valid, authFetch, query, key]);
  if (!allowed) return <PermissionDeniedCard permission="financials.journal.view" />;
  const update = (values: Record<string, string>) => {
    const next = new URLSearchParams(params);
    // Persist the default dates too, so a shared link always describes the same period.
    next.set('from', filters.from); next.set('to', filters.to);
    for (const [name, value] of Object.entries(values)) next.set(name, value);
    setParams(next, { replace: true });
  };
  const entries = searchLedger(data?.entries || [], search);
  const formatAmount = (amount: number) => `${currencySymbol(currentOrg.currency)}${amount.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
  const today = getDateRange('all', new Date()).to;
  const activeYear = data?.years.find(y => y.isActive && y.startDate.slice(0, 10) <= today && y.endDate.slice(0, 10) >= today);
  const documentTypes = ['SalesDelivery', 'SalesInvoice', 'SalesReceipt', 'SalesDebitMemo', 'SalesCreditMemo', 'PurchaseReceipt',
    'PurchaseInvoice', 'PurchaseDebitMemo', 'PurchaseCreditMemo', 'PurchaseInvoicePayment', 'JournalEntry', 'CustomerAllocation', 'InventoryAdjustment'];
  const inputClass = 'rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-surface)] px-3 py-2 text-sm text-[var(--text-main)] focus:ring-2 focus:ring-[var(--brand-ring)]';
  const exportCsv = () => {
    const url = URL.createObjectURL(new Blob(['\uFEFF', ledgerCsv(entries)], { type: 'text/csv;charset=utf-8;' }));
    const link = document.createElement('a');
    link.href = url; link.download = `${currentOrg.slug}-general-ledger-${filters.to || 'all'}.csv`;
    document.body.appendChild(link); link.click(); link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  };
  return <div className="space-y-6 text-[var(--text-main)]">
    <PageHeader title="General Ledger" subtitle="Journal register / posted transactions"
      actions={<>
        <HeaderButton label="Refresh" icon={RefreshCw} disabled={loading || !valid} onClick={() => setRevision(r => r + 1)} />
        <HeaderButton label="Export CSV" icon={Download} disabled={!data || !entries.length} onClick={exportCsv} />
        {hasPermission('financials.journal.create') && <HeaderButton label="New Journal Entry" icon={Plus} variant="primary" to={`/org/${currentOrg.slug}/financials/journals?new=1`} />}
      </>} />
    <nav aria-label="Ledger views" className="flex flex-wrap gap-4 text-sm">
      <span aria-current="page" className="font-semibold">Posted transactions</span>
      <Link className="underline" to={`/org/${currentOrg.slug}/financials/journals`}>Manual journals / drafts</Link>
      {hasPermission('financials.accounts.view') && <Link className="underline" to={`/org/${currentOrg.slug}/financials/accounts`}>Chart of Accounts</Link>}
    </nav>
    <div className="flex flex-wrap items-end gap-4 rounded-2xl bg-[var(--bg-surface)] p-4">
      <label className="grid gap-1 text-xs">Period<select className={inputClass} value={preset} onChange={event => {
        const value = event.target.value;
        if (value === 'month' || value === 'quarter' || value === 'year' || value === 'all') update({ ...getDateRange(value, new Date(), activeYear), period: value });
        else update({ period: 'custom' });
      }}>
        <option value="month">This Month</option><option value="quarter">Quarter-to-Date</option>
        <option value="year" disabled={!activeYear}>Year-to-Date</option><option value="all">All Time</option><option value="custom">Custom Range</option>
      </select></label>
      {(['from', 'to'] as const).map(name => <label key={name} className="grid gap-1 text-xs">{name === 'from' ? 'From' : 'To'}
        <input type="date" className={inputClass} value={filters[name]} onChange={event => update({ [name]: event.target.value, period: 'custom' })} />
      </label>)}
      <label className="grid gap-1 text-xs">Document type<select className={inputClass} value={filters.documentType} onChange={event => update({ documentType: event.target.value })}>
        <option value="">All document types</option>{documentTypes.map(type => <option key={type} value={type}>{type.replace(/([a-z])([A-Z])/g, '$1 $2')}</option>)}
      </select></label>
      <label className="grid gap-1 text-xs">Account code<input className={inputClass} value={filters.accountCode} onChange={event => update({ accountCode: event.target.value })} /></label>
      <label className="grid gap-1 text-xs">Transaction #<input type="number" min="1" step="1" className={inputClass} value={filters.transactionNo} onChange={event => update({ transactionNo: event.target.value })} /></label>
      <label className="grid gap-1 text-xs">Search<input type="search" className={inputClass} placeholder="Transaction, description, account" value={search} onChange={event => setSearch(event.target.value)} /></label>
    </div>
    {!valid ? <p role="alert" className="text-red-500">Select a valid date range (from must not be after to) and positive transaction number.</p>
      : error ? <p role="alert" className="text-red-500">{error} Use Refresh to retry.</p>
      : loading ? <p role="status">Loading posted transactions...</p>
      : <section aria-label="Posted transactions" className="overflow-x-auto rounded-2xl bg-[var(--bg-surface)]">
        {!entries.length ? <p role="status" className="p-6">No posted transactions for the selected dates and filters.</p>
          : <LedgerRows key={currentOrg.slug} entries={entries} slug={currentOrg.slug} formatAmount={formatAmount} />}
      </section>}
  </div>;
}
