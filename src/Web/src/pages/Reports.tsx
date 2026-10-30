import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { Download, Loader2, RefreshCw } from 'lucide-react';
import { useAuth } from '../auth/AuthContext';
import { PermissionDeniedCard, useTenantContext } from '../components/Gating';
import { HeaderButton, PageHeader } from '../components/PageHeader';
import { BankCloseStatusLoader } from '../components/BankCloseStatusCard';
import { currencySymbol, DEFAULT_CURRENCY } from '../lib/currency';
import { comparativeRange } from '../lib/comparativeRange';

const COMPARE_STORAGE_KEY = 'accountgo.reports.comparePreviousPeriod';

function readCompareToggle(): boolean {
  try {
    return localStorage.getItem(COMPARE_STORAGE_KEY) === '1';
  } catch {
    return false;
  }
}

function writeCompareToggle(value: boolean): void {
  try {
    localStorage.setItem(COMPARE_STORAGE_KEY, value ? '1' : '0');
  } catch {
    /* ignore quota / private mode */
  }
}

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

export interface CashFlowLineDto {
  accountId: number;
  accountCode: string;
  accountName: string;
  lineType: string;
  amount: number;
}

export interface CashFlowStatementDto {
  fromDate?: string;
  toDate?: string;
  accountingBasis: string;
  netIncome: number;
  operatingCashFlow: number;
  investingCashFlow: number;
  financingCashFlow: number;
  netChangeInCash: number;
  startingCash: number;
  endingCash: number;
  operatingActivities: CashFlowLineDto[];
  investingActivities: CashFlowLineDto[];
  financingActivities: CashFlowLineDto[];
  message?: string;
}

export interface ReportData {
  trialBalance: TrialBalanceRow[];
  income: IncomeRow[];
  balanceSheet: BalanceRow[];
  cashFlow: CashFlowStatementDto;
  years: FinancialYear[];
  accounts: Account[];
}

export type Statement = 'trialBalance' | 'income' | 'balanceSheet' | 'cashFlow';
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
  /** Minor units for the comparative period (P&L / Balance Sheet compare mode). */
  comparativeAmount?: number;
  change?: number;
  /** Percent change; null when the comparative amount is 0. */
  changePercent?: number | null;
  accountId?: number;
}

const statements: { key: Statement; label: string }[] = [
  { key: 'trialBalance', label: 'Trial Balance' },
  { key: 'income', label: 'Profit & Loss' },
  { key: 'balanceSheet', label: 'Balance Sheet' },
  { key: 'cashFlow', label: 'Cash Flow' },
];
const localDate = (date: Date) =>
  `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;

export function getDateRange(
  preset: Exclude<Preset, 'custom'>,
  today: Date,
  yearOrYears?: FinancialYear | FinancialYear[],
): DateRange {
  const to = localDate(today);
  if (preset === 'all') return { from: '', to };
  if (preset === 'year') {
    const years = yearOrYears == null ? [] : Array.isArray(yearOrYears) ? yearOrYears : [yearOrYears];
    const year = years.find(y =>
      y.isActive !== false
      && y.startDate.slice(0, 10) <= to
      && y.endDate.slice(0, 10) >= to);
    if (!year)
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
  async function readObject<T>(path: string): Promise<T> {
    const response = await fetcher(path);
    if (!response.ok) throw new Error(`Unable to load financial reports (HTTP ${response.status}).`);
    const data: T = await response.json();
    if (!data || typeof data !== 'object' || Array.isArray(data)) throw new Error('The reporting API returned an invalid response.');
    return data;
  }
  const query = reportQuery(range, basis);
  const [trialBalance, income, balanceSheet, cashFlow, years, accounts] = await Promise.all([
    read<TrialBalanceRow>(`/api/Financials/trialbalance${query}`),
    read<IncomeRow>(`/api/Financials/IncomeStatement${query}`),
    read<BalanceRow>(`/api/Financials/BalanceSheet${query}`),
    readObject<CashFlowStatementDto>(`/api/Financials/CashFlow${query}`),
    read<FinancialYear>('/api/Administration/financialyears'),
    read<Account>('/api/Financials/Accounts'),
  ]);
  for (const row of trialBalance) { minorUnits(row.debit); minorUnits(row.credit); }
  for (const row of [...income, ...balanceSheet]) minorUnits(row.amount);
  minorUnits(cashFlow.netIncome ?? 0);
  minorUnits(cashFlow.operatingCashFlow ?? 0);
  minorUnits(cashFlow.investingCashFlow ?? 0);
  minorUnits(cashFlow.financingCashFlow ?? 0);
  minorUnits(cashFlow.netChangeInCash ?? 0);
  minorUnits(cashFlow.startingCash ?? 0);
  minorUnits(cashFlow.endingCash ?? 0);
  for (const row of [
    ...(cashFlow.operatingActivities || []),
    ...(cashFlow.investingActivities || []),
    ...(cashFlow.financingActivities || []),
  ]) {
    minorUnits(row.amount);
  }
  return { trialBalance, income, balanceSheet, cashFlow, years, accounts };
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
      code: a.accountCode,
      label: a.accountName,
      kind: 'account' as const,
      amount: minorUnits(a.amount),
      accountId: a.accountId,
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
  } else if (statement === 'cashFlow') {
    const cf = data.cashFlow;
    if (cf?.message) {
      rows.push({ code: '', label: cf.message, kind: 'section' });
    } else {
      // Cash flows from operating activities
      rows.push({ code: '', label: 'Cash flows from operating activities', kind: 'section' });
      rows.push({ code: '', label: 'Net Profit', kind: 'account', amount: minorUnits(cf?.netIncome ?? 0) });
      const opVisible = byCode(cf?.operatingActivities || []).filter(a => !hideZero || minorUnits(a.amount) !== 0);
      rows.push(...opVisible.map(a => ({
        code: a.accountCode, label: a.accountName, kind: 'account' as const, amount: minorUnits(a.amount),
      })));
      rows.push({ code: '', label: 'Net cash provided by operating activities', kind: 'total', amount: minorUnits(cf?.operatingCashFlow ?? 0) });

      // Cash flows from investing activities
      rows.push({ code: '', label: 'Cash flows from investing activities', kind: 'section' });
      const invVisible = byCode(cf?.investingActivities || []).filter(a => !hideZero || minorUnits(a.amount) !== 0);
      rows.push(...invVisible.map(a => ({
        code: a.accountCode, label: a.accountName, kind: 'account' as const, amount: minorUnits(a.amount),
      })));
      rows.push({ code: '', label: 'Net cash provided by investing activities', kind: 'total', amount: minorUnits(cf?.investingCashFlow ?? 0) });

      // Cash flows from financing activities
      rows.push({ code: '', label: 'Cash flows from financing activities', kind: 'section' });
      const finVisible = byCode(cf?.financingActivities || []).filter(a => !hideZero || minorUnits(a.amount) !== 0);
      rows.push(...finVisible.map(a => ({
        code: a.accountCode, label: a.accountName, kind: 'account' as const, amount: minorUnits(a.amount),
      })));
      rows.push({ code: '', label: 'Net cash provided by financing activities', kind: 'total', amount: minorUnits(cf?.financingCashFlow ?? 0) });
    }

    // Cash reconciliation summary bridge (rendered for both Accrual and Cash basis)
    rows.push({ code: '', label: 'Cash and cash equivalents', kind: 'section' });
    rows.push({ code: '', label: 'Net increase / (decrease) in cash', kind: 'total', amount: minorUnits(cf?.netChangeInCash ?? 0) });
    rows.push({ code: '', label: 'Cash at beginning of period', kind: 'account', amount: minorUnits(cf?.startingCash ?? 0) });
    rows.push({ code: '', label: 'Cash at end of period', kind: 'total', amount: minorUnits(cf?.endingCash ?? 0) });

    const diff = cf?.message
      ? (minorUnits(cf?.startingCash ?? 0) + minorUnits(cf?.netChangeInCash ?? 0)) - minorUnits(cf?.endingCash ?? 0)
      : (minorUnits(cf?.operatingCashFlow ?? 0) + minorUnits(cf?.investingCashFlow ?? 0) + minorUnits(cf?.financingCashFlow ?? 0)) - minorUnits(cf?.netChangeInCash ?? 0);
    rows.push({ code: '', label: 'Difference', kind: 'total', amount: diff });
  } else {
    const assets = section('Assets', data.balanceSheet.filter(a => a.accountClassId === 1));
    const liabilities = section('Liabilities', data.balanceSheet.filter(a => a.accountClassId === 2));
    const equity = section('Equity', data.balanceSheet.filter(a => a.accountClassId === 3));
    rows.push({ code: '', label: 'Total Liabilities + Equity', kind: 'total', amount: liabilities + equity });
    rows.push({ code: '', label: 'Difference', kind: 'total', amount: assets - liabilities - equity });
  }
  return rows;
}

export function statementCsv(
  statement: Statement,
  rows: StatementRow[],
  metadata: string[][],
  options?: { compare?: boolean; currentHeader?: string; comparativeHeader?: string },
): string {
  const amount = (value?: number) => value === undefined ? '' : (value / 100).toFixed(2);
  const pct = (value: number | null | undefined) =>
    value === undefined || value === null ? '' : `${value.toFixed(1)}%`;
  const compare = !!options?.compare && (statement === 'income' || statement === 'balanceSheet');
  const header = statement === 'trialBalance'
    ? ['Code', 'Account', 'Debit', 'Credit']
    : compare
      ? ['Code', 'Account', options?.currentHeader || 'Current', options?.comparativeHeader || 'Comparative', 'Change', 'Change %']
      : ['Code', 'Account', 'Amount'];
  const cells = [
    ...metadata,
    [],
    header,
    ...rows.map(row => statement === 'trialBalance'
      ? [row.code, row.label, amount(row.debit), amount(row.credit)]
      : compare
        ? [row.code, row.label, amount(row.amount), amount(row.comparativeAmount), amount(row.change), pct(row.changePercent)]
        : [row.code, row.label, amount(row.amount)]),
  ];
  return cells.map(row => row.map(cell => {
    // Prevent spreadsheet formulas in user-controlled account codes and names.
    const safe = /^\s*[=+@\t\r-]/.test(cell) && !/^-?\d+(\.\d+)?$/.test(cell) ? `'${cell}` : cell;
    return `"${safe.replaceAll('"', '""')}"`;
  }).join(',')).join('\r\n');
}

/** Merge current and comparative P&L / Balance Sheet statement rows by account id (or label for totals). */
export function applyComparativeAmounts(
  currentRows: StatementRow[],
  comparativeRows: StatementRow[],
): StatementRow[] {
  const byKey = new Map<string, StatementRow>();
  for (const row of comparativeRows) {
    const key = row.kind === 'account' && row.accountId
      ? `a:${row.accountId}`
      : `${row.kind}:${row.code}:${row.label}`;
    byKey.set(key, row);
  }
  const seen = new Set<string>();
  const merged: StatementRow[] = currentRows.map(row => {
    const key = row.kind === 'account' && row.accountId
      ? `a:${row.accountId}`
      : `${row.kind}:${row.code}:${row.label}`;
    seen.add(key);
    const other = byKey.get(key);
    const amount = row.amount ?? 0;
    const comparativeAmount = other?.amount ?? 0;
    const change = amount - comparativeAmount;
    const changePercent = comparativeAmount === 0 ? null : (change / Math.abs(comparativeAmount)) * 100;
    return { ...row, comparativeAmount, change, changePercent };
  });
  for (const row of comparativeRows) {
    const key = row.kind === 'account' && row.accountId
      ? `a:${row.accountId}`
      : `${row.kind}:${row.code}:${row.label}`;
    if (seen.has(key)) continue;
    const comparativeAmount = row.amount ?? 0;
    merged.push({
      ...row,
      amount: 0,
      comparativeAmount,
      change: -comparativeAmount,
      changePercent: comparativeAmount === 0 ? null : -100,
    });
  }
  return merged;
}

export default function Reports() {
  const { currentOrg, hasPermission, hasModule } = useTenantContext();
  const { authFetch } = useAuth();
  const allowed = hasPermission('reports.view');
  const showBankCloseStatus = hasModule('financials') && hasPermission('banking.accounts.view');
  const [statement, setStatement] = useState<Statement>('trialBalance');
  const [basis, setBasis] = useState<AccountingBasis>('accrual');
  const [preset, setPreset] = useState<Preset>('month');
  const [range, setRange] = useState<DateRange>(() => getDateRange('month', new Date()));
  const [hideZero, setHideZero] = useState(true);
  const [compare, setCompare] = useState(readCompareToggle);
  const [revision, setRevision] = useState(0);
  const [result, setResult] = useState<{ key: string; slug: string; data: ReportData; comparative?: ReportData; comparativeRange?: DateRange | null } | null>(null);
  const [failure, setFailure] = useState<{ key: string; message: string } | null>(null);
  const validRange = !!range.to && (!range.from || range.from <= range.to);
  const canCompare = statement === 'income' || statement === 'balanceSheet';
  const key = JSON.stringify([currentOrg.slug, range.from, range.to, basis, revision, compare && canCompare]);
  const data = result?.key === key ? result.data : null;
  const comparativeData = result?.key === key ? result.comparative : undefined;
  const activeCompareRange = result?.key === key ? result.comparativeRange : undefined;
  const error = failure?.key === key ? failure.message : null;
  const loading = allowed && validRange && !data && !error;
  const tenantData = result?.slug === currentOrg.slug ? result.data : null;
  const today = localDate(new Date());
  const activeYear = tenantData?.years.find(y => y.isActive && y.startDate.slice(0, 10) <= today && y.endDate.slice(0, 10) >= today);

  useEffect(() => {
    if (!allowed || !validRange) return;
    let cancelled = false;
    (async () => {
      try {
        const [data, policyRes] = await Promise.all([
          loadReportData(authFetch, range, basis),
          authFetch('/api/organizations/current/accounting-policy'),
        ]);
        let booksStart: string | null = null;
        if (policyRes.ok) {
          const policy: { booksStartDate?: string } = await policyRes.json();
          if (typeof policy.booksStartDate === 'string' && policy.booksStartDate)
            booksStart = policy.booksStartDate.slice(0, 10);
        }
        let comparative: ReportData | undefined;
        let prior: DateRange | null = null;
        if (compare && canCompare) {
          prior = comparativeRange(
            range,
            preset,
            data.years,
            statement === 'balanceSheet' ? 'balanceSheet' : 'income',
            booksStart,
          );
          if (prior) comparative = await loadReportData(authFetch, prior, basis);
        }
        if (!cancelled) {
          setResult({ key, slug: currentOrg.slug, data, comparative, comparativeRange: prior });
          setFailure(null);
        }
      } catch (error) {
        if (!cancelled) setFailure({ key, message: error instanceof Error ? error.message : 'Unable to load financial reports.' });
      }
    })();
    return () => { cancelled = true; };
  }, [authFetch, allowed, validRange, key, range, basis, currentOrg.slug, compare, canCompare, preset, statement]);

  if (!allowed) return <PermissionDeniedCard permission="reports.view" />;

  const showingCompare = !!(compare && canCompare && comparativeData && activeCompareRange);
  const baseRows = data ? buildStatement(statement, data, hideZero) : [];
  const rows = showingCompare && (statement === 'income' || statement === 'balanceSheet')
    ? applyComparativeAmounts(baseRows, buildStatement(statement, comparativeData!, hideZero))
    : baseRows;
  const title = statements.find(s => s.key === statement)!.label;
  const basisLabel = basis === 'cash' ? 'Cash Basis' : 'Accrual Basis';
  const period = statement === 'balanceSheet' ? `As of ${range.to}` : `${range.from || 'All time'} to ${range.to}`;
  const comparativePeriod = !activeCompareRange ? ''
    : statement === 'balanceSheet' ? `As of ${activeCompareRange.to}` : `${activeCompareRange.from} to ${activeCompareRange.to}`;
  const formatAmount = (value?: number) => value === undefined ? '' :
    `${currencySymbol(currentOrg.currency)}${(value / 100).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
  const formatPercent = (value: number | null | undefined) =>
    value === undefined || value === null ? '' : `${value.toLocaleString(undefined, { maximumFractionDigits: 1 })}%`;
  const difference = statement === 'trialBalance'
    ? (rows.at(-1)?.debit ?? 0) - (rows.at(-1)?.credit ?? 0)
    : rows.at(-1)?.amount ?? 0;
  const exportCsv = () => {
    if (!data || !validRange || error) return;
    const csv = statementCsv(statement, rows, [
      ['Statement', title], ['Accounting Basis', basisLabel], ['Organization', currentOrg.name], ['Currency', currentOrg.currency || DEFAULT_CURRENCY],
      ['Period', period],
      ...(showingCompare ? [['Comparative period', comparativePeriod]] : []),
      ['Financial Year', activeYear?.fiscalYearName || 'No active financial year'],
    ], {
      compare: showingCompare,
      currentHeader: period,
      comparativeHeader: comparativePeriod,
    });
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
  const setCompareAndPersist = (value: boolean) => {
    setCompare(value);
    writeCompareToggle(value);
  };

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
      {showBankCloseStatus && <BankCloseStatusLoader orgSlug={currentOrg.slug} />}
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
              try { setRange(getDateRange(next, new Date(), next === 'year' ? tenantData?.years : activeYear)); }
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
        {canCompare && <label className="flex items-center gap-2 py-2 text-sm">
          <input type="checkbox" checked={compare} onChange={event => setCompareAndPersist(event.target.checked)} className="accent-[var(--brand-primary)]" />
          Compare with previous period
        </label>}
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
          {compare && canCompare && !showingCompare && data && <p className="px-5 pb-4 text-sm text-[var(--text-muted)]">No prior period is available (comparative range would start before the books start).</p>}
          {statement === 'cashFlow' && basis === 'cash' && data.cashFlow?.message && <p className="px-5 pb-4 text-sm text-[var(--text-muted)]">{data.cashFlow.message}</p>}
          {!rows.some(row => row.kind === 'account') && (!data.cashFlow?.message || statement !== 'cashFlow') && <p className="px-5 pb-4 text-sm text-[var(--text-muted)]">No ledger activity for the selected dates and filters.</p>}
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <caption className="sr-only">{title}, {period}, {currentOrg.currency || DEFAULT_CURRENCY}</caption>
              <thead className="bg-[var(--bg-subtle)] text-[var(--text-secondary)]">
                <tr><th scope="col" className="p-4 text-left">Code</th><th scope="col" className="p-4 text-left">Account</th>
                  {statement === 'trialBalance' ? <><th scope="col" className="p-4 text-right">Debit</th><th scope="col" className="p-4 text-right">Credit</th></>
                    : showingCompare ? <>
                      <th scope="col" className="p-4 text-right">{period}</th>
                      <th scope="col" className="p-4 text-right">{comparativePeriod}</th>
                      <th scope="col" className="p-4 text-right">Change</th>
                      <th scope="col" className="p-4 text-right">Change %</th>
                    </>
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
                      : showingCompare ? <>
                        <td className="p-4 text-right tabular-nums">{formatAmount(row.amount)}</td>
                        <td className="p-4 text-right tabular-nums">{formatAmount(row.comparativeAmount)}</td>
                        <td className="p-4 text-right tabular-nums">{formatAmount(row.change)}</td>
                        <td className="p-4 text-right tabular-nums">{formatPercent(row.changePercent)}</td>
                      </>
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
