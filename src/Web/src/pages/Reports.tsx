import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { Download, Loader2, RefreshCw } from 'lucide-react';
import { useAuth } from '../auth/AuthContext';
import { PermissionDeniedCard, useTenantContext } from '../components/Gating';
import { HeaderButton, PageHeader } from '../components/PageHeader';
import { currencySymbol, DEFAULT_CURRENCY } from '../lib/currency';

export interface FinancialYear {
  fiscalYearCode: string;
  fiscalYearName: string;
  startDate: string;
  endDate: string;
  isActive: boolean;
}

interface Account {
  id: number;
  accountName: string;
  childAccounts: Account[];
}

interface TrialBalanceRow {
  accountId: number;
  accountCode: string;
  accountName: string;
  debit: number;
  credit: number;
}

interface IncomeRow {
  accountId: number;
  accountCode: string;
  accountName: string;
  amount: number;
  isExpense: boolean;
}

interface BalanceRow {
  accountId: number;
  accountClassId: number;
  accountCode: string;
  accountName: string;
  amount: number;
}

export interface ReportData {
  trialBalance: TrialBalanceRow[];
  income: IncomeRow[];
  balanceSheet: BalanceRow[];
  years: FinancialYear[];
  accounts: Account[];
}

export type Statement = 'trialBalance' | 'income' | 'balanceSheet';
export type AccountingBasis = 'accrual' | 'cash';
type Preset = 'month' | 'quarter' | 'year' | 'all' | 'custom';
export interface DateRange { from: string; to: string }
export interface StatementRow {
  code: string;
  label: string;
  kind: 'account' | 'section' | 'total';
  debit?: number;
  credit?: number;
  amount?: number;
}

const statements: { key: Statement; label: string }[] = [
  { key: 'trialBalance', label: 'Trial Balance' },
  { key: 'income', label: 'Profit & Loss' },
  { key: 'balanceSheet', label: 'Balance Sheet' },
];
const localDate = (date: Date) =>
  `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;

export function getDateRange(preset: Exclude<Preset, 'custom'>, today: Date, year?: FinancialYear): DateRange {
  const to = localDate(today);
  if (preset === 'all') return { from: '', to };
  if (preset === 'year') {
    if (!year || year.startDate.slice(0, 10) > to || year.endDate.slice(0, 10) < to)
      throw new Error('No active financial year covers today. Select a custom date range.');
    return { from: year.startDate.slice(0, 10), to };
  }
  const month = preset === 'quarter' ? Math.floor(today.getMonth() / 3) * 3 : today.getMonth();
  return { from: localDate(new Date(today.getFullYear(), month, 1)), to };
}

export function reportQuery(range: DateRange, basis: AccountingBasis = 'accrual'): string {
  const params = new URLSearchParams();
  if (range.from) params.set('from', `${range.from}T00:00:00`);
  // Include every posting on the selected end date, at DateTime's full precision.
  if (range.to) params.set('to', `${range.to}T23:59:59.9999999`);
  if (basis === 'cash') params.set('basis', 'Cash');
  return params.size ? `?${params}` : '';
}

export async function loadReportData(
  fetcher: (url: string) => Promise<Response>,
  range: DateRange,
  basis: AccountingBasis = 'accrual',
): Promise<ReportData> {
  async function read<T>(path: string): Promise<T[]> {
    const response = await fetcher(path);
    if (!response.ok) throw new Error(`Unable to load financial reports (HTTP ${response.status}).`);
    const data: T[] = await response.json();
    if (!Array.isArray(data)) throw new Error('The reporting API returned an invalid response.');
    return data;
  }
  const query = reportQuery(range, basis);
  const [trialBalance, income, balanceSheet, years, accounts] = await Promise.all([
    read<TrialBalanceRow>(`/api/Financials/trialbalance${query}`),
    read<IncomeRow>(`/api/Financials/IncomeStatement${query}`),
    read<BalanceRow>(`/api/Financials/BalanceSheet${query}`),
    read<FinancialYear>('/api/Administration/financialyears'),
    read<Account>('/api/Financials/Accounts'),
  ]);
  for (const row of trialBalance) { minorUnits(row.debit); minorUnits(row.credit); }
  for (const row of [...income, ...balanceSheet]) minorUnits(row.amount);
  return { trialBalance, income, balanceSheet, years, accounts };
}

function minorUnits(amount: number): number {
  const value = Math.sign(amount) * Math.round((Math.abs(amount) + Number.EPSILON) * 100);
  if (!Number.isFinite(amount) || !Number.isSafeInteger(value))
    throw new Error('The reporting API returned an invalid monetary amount.');
  return value;
}

const byCode = <T extends { accountCode: string }>(rows: T[]): T[] =>
  [...rows].sort((a, b) => a.accountCode.localeCompare(b.accountCode, undefined, { numeric: true }));

export function buildStatement(statement: Statement, data: ReportData, hideZero = true): StatementRow[] {
  if (statement === 'trialBalance') {
    const rows: StatementRow[] = byCode(data.trialBalance).map(a => {
      const net = minorUnits(a.debit) - minorUnits(a.credit);
      return { code: a.accountCode, label: a.accountName, kind: 'account' as const, debit: Math.max(net, 0), credit: Math.max(-net, 0) };
    }).filter(a => !hideZero || a.debit !== 0 || a.credit !== 0);
    const debit = rows.reduce((sum, a) => sum + (a.debit ?? 0), 0);
    const credit = rows.reduce((sum, a) => sum + (a.credit ?? 0), 0);
    rows.push({ code: '', label: 'Total Debits / Total Credits', kind: 'total', debit, credit });
    rows.push({ code: '', label: 'Difference', kind: 'total', debit: Math.max(debit - credit, 0), credit: Math.max(credit - debit, 0) });
    return rows;
  }

  const rows: StatementRow[] = [];
  const section = (label: string, accounts: (IncomeRow | BalanceRow)[]) => {
    rows.push({ code: '', label, kind: 'section' });
    const visible = byCode(accounts).filter(a => !hideZero || minorUnits(a.amount) !== 0);
    rows.push(...visible.map(a => ({
      code: a.accountCode, label: a.accountName, kind: 'account' as const, amount: minorUnits(a.amount),
    })));
    const amount = accounts.reduce((sum, a) => sum + minorUnits(a.amount), 0);
    rows.push({ code: '', label: `Total ${label}`, kind: 'total', amount });
    return amount;
  };
  if (statement === 'income') {
    const costIds = new Set<number>();
    const visit = (accounts: Account[], isCost = false) => {
      for (const account of accounts) {
        const cost = isCost || /\b(cost of (goods sold|sales)|direct costs?|purchases?)\b/i.test(account.accountName);
        if (cost) costIds.add(account.id);
        visit(account.childAccounts || [], cost);
      }
    };
    visit(data.accounts);
    const revenues = section('Revenue', data.income.filter(a => !a.isExpense));
    const costs = section('Cost of Goods Sold', data.income.filter(a => a.isExpense && costIds.has(a.accountId)));
    rows.push({ code: '', label: 'Gross Profit', kind: 'total', amount: revenues - costs });
    const expenses = section('Operating Expenses', data.income.filter(a => a.isExpense && !costIds.has(a.accountId)));
    rows.push({ code: '', label: 'Net Profit', kind: 'total', amount: revenues - costs - expenses });
  } else {
    const assets = section('Assets', data.balanceSheet.filter(a => a.accountClassId === 1));
    const liabilities = section('Liabilities', data.balanceSheet.filter(a => a.accountClassId === 2));
    const equity = section('Equity', data.balanceSheet.filter(a => a.accountClassId === 3));
    rows.push({ code: '', label: 'Total Liabilities + Equity', kind: 'total', amount: liabilities + equity });
    rows.push({ code: '', label: 'Difference', kind: 'total', amount: assets - liabilities - equity });
  }
  return rows;
}

export function statementCsv(statement: Statement, rows: StatementRow[], metadata: string[][]): string {
  const amount = (value?: number) => value === undefined ? '' : (value / 100).toFixed(2);
  const cells = [
    ...metadata,
    [],
    statement === 'trialBalance' ? ['Code', 'Account', 'Debit', 'Credit'] : ['Code', 'Account', 'Amount'],
    ...rows.map(row => statement === 'trialBalance'
      ? [row.code, row.label, amount(row.debit), amount(row.credit)]
      : [row.code, row.label, amount(row.amount)]),
  ];
  return cells.map(row => row.map(cell => {
    // Prevent spreadsheet formulas in user-controlled account codes and names.
    const safe = /^\s*[=+@\t\r-]/.test(cell) && !/^-?\d+(\.\d+)?$/.test(cell) ? `'${cell}` : cell;
    return `"${safe.replaceAll('"', '""')}"`;
  }).join(',')).join('\r\n');
}

export default function Reports() {
  const { currentOrg, hasPermission } = useTenantContext();
  const { authFetch } = useAuth();
  const allowed = hasPermission('reports.view');
  const [statement, setStatement] = useState<Statement>('trialBalance');
  const [basis, setBasis] = useState<AccountingBasis>('accrual');
  const [preset, setPreset] = useState<Preset>('month');
  const [range, setRange] = useState<DateRange>(() => getDateRange('month', new Date()));
  const [hideZero, setHideZero] = useState(true);
  const [revision, setRevision] = useState(0);
  const [result, setResult] = useState<{ key: string; slug: string; data: ReportData } | null>(null);
  const [failure, setFailure] = useState<{ key: string; message: string } | null>(null);
  const validRange = !!range.to && (!range.from || range.from <= range.to);
  const key = JSON.stringify([currentOrg.slug, range.from, range.to, basis, revision]);
  const data = result?.key === key ? result.data : null;
  const error = failure?.key === key ? failure.message : null;
  const loading = allowed && validRange && !data && !error;
  const tenantData = result?.slug === currentOrg.slug ? result.data : null;
  const today = localDate(new Date());
  const activeYear = tenantData?.years.find(y => y.isActive && y.startDate.slice(0, 10) <= today && y.endDate.slice(0, 10) >= today)
    ?? tenantData?.years.find(y => y.isActive);

  useEffect(() => {
    if (!allowed || !validRange) return;
    let cancelled = false;
    loadReportData(authFetch, range, basis).then(
      data => { if (!cancelled) { setResult({ key, slug: currentOrg.slug, data }); setFailure(null); } },
      error => { if (!cancelled) setFailure({ key, message: error instanceof Error ? error.message : 'Unable to load financial reports.' }); },
    );
    return () => { cancelled = true; };
  }, [authFetch, allowed, validRange, key, range, basis, currentOrg.slug]);

  if (!allowed) return <PermissionDeniedCard permission="reports.view" />;

  const rows = data ? buildStatement(statement, data, hideZero) : [];
  const title = statements.find(s => s.key === statement)!.label;
  const basisLabel = basis === 'cash' ? 'Cash Basis' : 'Accrual Basis';
  const period = statement === 'balanceSheet' ? `As of ${range.to}` : `${range.from || 'All time'} to ${range.to}`;
  const formatAmount = (value?: number) => value === undefined ? '' :
    `${currencySymbol(currentOrg.currency)}${(value / 100).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
  const difference = statement === 'trialBalance'
    ? (rows.at(-1)?.debit ?? 0) - (rows.at(-1)?.credit ?? 0)
    : rows.at(-1)?.amount ?? 0;
  const exportCsv = () => {
    if (!data || !validRange || error) return;
    const csv = statementCsv(statement, rows, [
      ['Statement', title], ['Accounting Basis', basisLabel], ['Organization', currentOrg.name], ['Currency', currentOrg.currency || DEFAULT_CURRENCY],
      ['Period', period], ['Financial Year', activeYear?.fiscalYearName || 'No active financial year'],
    ]);
    const url = URL.createObjectURL(new Blob(['\uFEFF', csv], { type: 'text/csv;charset=utf-8;' }));
    const link = document.createElement('a');
    link.href = url;
    link.download = `${currentOrg.slug}-${statement}-${basis}-${range.to}.csv`;
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  };
  const inputClass = 'rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-surface)] px-3 py-2 text-sm text-[var(--text-main)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-ring)]';

  return (
    <div className="space-y-6 text-[var(--text-main)]">
      <PageHeader
        title="Financial Reports"
        subtitle={activeYear
          ? `${activeYear.fiscalYearName} (${activeYear.fiscalYearCode}) | ${activeYear.startDate.slice(0, 10)} to ${activeYear.endDate.slice(0, 10)}`
          : loading ? 'Loading financial year...' : 'No active financial year available'}
        actions={<>
          <HeaderButton label="Refresh" icon={RefreshCw} disabled={loading || !validRange} onClick={() => setRevision(r => r + 1)} />
          <HeaderButton label="Export CSV" icon={Download} disabled={!data || !validRange || !!error} onClick={exportCsv} />
        </>}
      />
      <div className="flex flex-wrap items-end gap-4 rounded-2xl bg-[var(--bg-surface)] p-4">
        <label className="grid gap-1 text-xs text-[var(--text-secondary)]">Accounting Basis
          <select className={inputClass} value={basis} onChange={event => setBasis(event.target.value as AccountingBasis)}>
            <option value="accrual">Accrual</option>
            <option value="cash">Cash</option>
          </select>
        </label>
        <label className="grid gap-1 text-xs text-[var(--text-secondary)]">Period
          <select className={inputClass} value={preset} onChange={event => {
            const next = event.target.value as Preset;
            setPreset(next);
            if (next !== 'custom') {
              try { setRange(getDateRange(next, new Date(), activeYear)); }
              catch (error) { setFailure({ key, message: error instanceof Error ? error.message : 'Invalid financial year.' }); }
            }
          }}>
            <option value="month">This Month</option>
            <option value="quarter">Quarter-to-Date</option>
            <option value="year" disabled={!activeYear || activeYear.startDate.slice(0, 10) > today || activeYear.endDate.slice(0, 10) < today}>Year-to-Date</option>
            <option value="all">All Time / As-of Date</option>
            <option value="custom">Custom Range</option>
          </select>
        </label>
        <label className="grid gap-1 text-xs text-[var(--text-secondary)]">From
          <input type="date" className={inputClass} value={range.from} max={range.to} disabled={statement === 'balanceSheet'}
            onChange={event => { setPreset('custom'); setRange({ ...range, from: event.target.value }); }} />
        </label>
        <label className="grid gap-1 text-xs text-[var(--text-secondary)]">{statement === 'balanceSheet' ? 'As-of Date' : 'To'}
          <input type="date" className={inputClass} value={range.to} min={statement === 'balanceSheet' ? undefined : range.from} required
            onChange={event => { setPreset('custom'); setRange({ from: statement === 'balanceSheet' ? '' : range.from, to: event.target.value }); }} />
        </label>
        <label className="flex items-center gap-2 py-2 text-sm">
          <input type="checkbox" checked={hideZero} onChange={event => setHideZero(event.target.checked)} className="accent-[var(--brand-primary)]" />
          Hide zero balances
        </label>
      </div>
      <nav aria-label="Financial statements" className="flex flex-wrap gap-2">
        {statements.map(tab => <button key={tab.key} type="button" aria-pressed={statement === tab.key} onClick={() => setStatement(tab.key)}
          className={`rounded-lg px-4 py-2 text-sm font-semibold focus-visible:ring-2 focus-visible:ring-[var(--brand-ring)] ${statement === tab.key ? 'bg-[var(--brand-primary)] text-[var(--brand-text)]' : 'bg-[var(--bg-surface)] text-[var(--text-secondary)]'}`}>
          {tab.label}
        </button>)}
      </nav>
      {!validRange ? <p role="alert" className="text-red-500">Select an end date on or after the start date.</p>
        : error ? <p role="alert" className="text-red-500">{error} Use Refresh to retry.</p>
        : loading ? <div role="status" className="flex items-center gap-2 p-6"><Loader2 className="h-5 w-5 animate-spin" />Loading financial reports...</div>
        : data && <section aria-label={title} className="overflow-hidden rounded-2xl bg-[var(--bg-surface)]">
          <div className="flex flex-wrap items-center justify-between gap-3 p-5">
            <div><h2 className="text-lg font-semibold">{title}</h2><p className="text-sm text-[var(--text-secondary)]">{period} | {basisLabel} | {currentOrg.currency || DEFAULT_CURRENCY}</p></div>
            {statement !== 'income' && <p role="status" className={difference === 0 ? 'text-emerald-500' : 'text-red-500'}>
              {difference === 0 ? 'In balance' : `Out of balance: ${formatAmount(Math.abs(difference))}`}
            </p>}
          </div>
          {statement === 'balanceSheet' && <p className="px-5 pb-4 text-sm text-[var(--text-muted)]">Cumulative balances through the as-of date, including opening balances and unclosed earnings.</p>}
          {!rows.some(row => row.kind === 'account') && <p className="px-5 pb-4 text-sm text-[var(--text-muted)]">No ledger activity for the selected dates and filters.</p>}
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <caption className="sr-only">{title}, {period}, {currentOrg.currency || DEFAULT_CURRENCY}</caption>
              <thead className="bg-[var(--bg-subtle)] text-[var(--text-secondary)]">
                <tr><th scope="col" className="p-4 text-left">Code</th><th scope="col" className="p-4 text-left">Account</th>
                  {statement === 'trialBalance' ? <><th scope="col" className="p-4 text-right">Debit</th><th scope="col" className="p-4 text-right">Credit</th></>
                    : <th scope="col" className="p-4 text-right">Amount</th>}</tr>
              </thead>
              <tbody>
                {rows.filter(row => statement !== 'trialBalance' || row.kind !== 'total').map((row, index) =>
                  <tr key={index} className={`border-t border-[var(--border-subtle)] ${row.kind === 'account' ? '' : 'font-semibold bg-[var(--bg-subtle)]'}`}>
                    <td className="p-4 font-mono text-[var(--text-muted)]">{row.code}</td><th scope="row" className="p-4 text-left font-[inherit]">
                      {statement === 'trialBalance' && row.kind === 'account' && hasPermission('financials.journal.view')
                        ? <Link className="underline" to={`/org/${currentOrg.slug}/financials?${new URLSearchParams({ accountCode: row.code, from: range.from, to: range.to })}`}>{row.label}</Link>
                        : row.label}
                    </th>
                    {statement === 'trialBalance' ? <><td className="p-4 text-right tabular-nums">{formatAmount(row.debit)}</td><td className="p-4 text-right tabular-nums">{formatAmount(row.credit)}</td></>
                      : <td className="p-4 text-right tabular-nums">{formatAmount(row.amount)}</td>}
                  </tr>)}
              </tbody>
              {statement === 'trialBalance' && <tfoot className="bg-[var(--bg-subtle)] font-semibold">
                {rows.filter(row => row.kind === 'total').map(row => <tr key={row.label}>
                  <th scope="row" colSpan={2} className="p-4 text-left">{row.label}</th>
                  <td className="p-4 text-right tabular-nums">{formatAmount(row.debit)}</td><td className="p-4 text-right tabular-nums">{formatAmount(row.credit)}</td>
                </tr>)}
              </tfoot>}
            </table>
          </div>
        </section>}
    </div>
  );
}
