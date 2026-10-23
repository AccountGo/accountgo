import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Link, useOutletContext } from 'react-router-dom';
import { ChevronDown, ChevronRight, Download, Loader2, RefreshCw, Search } from 'lucide-react';
import { TenantContextType } from '../types';
import { useAuth } from '../auth/AuthContext';
import { PermissionDeniedCard, useTenantContext } from '../components/Gating';
import { currencySymbol as getCurrencySymbol } from '../lib/currency';
import { PageHeader, HeaderButton } from '../components/PageHeader';

interface ApiAccount {
  id: number;
  accountClassId: number;
  accountCode: string;
  accountName: string;
  accountClassName?: string;
  normalBalance?: 'Dr' | 'Cr' | 'NA';
  isCash: boolean;
  isContraAccount: boolean;
  canPost: boolean;
  // Signed on the account class's normal side, so contra accounts come back negative.
  balance: number;
  totalBalance: number;
  childAccounts: ApiAccount[];
}

interface Row {
  account: ApiAccount;
  level: number;
  rollup: number;
  hasChildren: boolean;
  parentId: number | null;
}

// Statement order: balance sheet first, then income statement, then closing accounts.
const CLASS_LABELS: Record<number, string> = {
  1: 'Assets',
  2: 'Liabilities',
  3: 'Equity',
  4: 'Revenue',
  5: 'Expenses',
  6: 'Closing accounts',
};

const rollupOf = (a: ApiAccount): number => (a.balance || 0) + (a.totalBalance || 0);

// A contra account's natural balance is the opposite of its class's sign.
const isAbnormal = (a: ApiAccount, rollup: number) => (a.isContraAccount ? -rollup : rollup) < 0;

const sortByCode = (list: ApiAccount[]) =>
  [...list].sort((x, y) => x.accountCode.localeCompare(y.accountCode, undefined, { numeric: true }));

function flatten(accounts: ApiAccount[], level = 1, parentId: number | null = null, out: Row[] = []): Row[] {
  for (const account of sortByCode(accounts)) {
    const children = account.childAccounts || [];
    out.push({ account, level, rollup: rollupOf(account), hasChildren: children.length > 0, parentId });
    flatten(children, level + 1, account.id, out);
  }
  return out;
}

export const ChartOfAccounts: React.FC = () => {
  const { currentOrg } = useOutletContext<TenantContextType>();
  const { hasPermission } = useTenantContext();
  const { authFetch } = useAuth();
  const symbol = getCurrencySymbol(currentOrg.currency);

  const [accounts, setAccounts] = useState<ApiAccount[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadFailed, setLoadFailed] = useState(false);
  const [query, setQuery] = useState('');
  const [classFilter, setClassFilter] = useState<number | null>(null);
  const [hideZero, setHideZero] = useState(false);
  const [collapsed, setCollapsed] = useState<Set<number>>(new Set());
  const tableRef = useRef<HTMLTableSectionElement>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setLoadFailed(false);
    try {
      const res = await authFetch('/api/Financials/Accounts');
      if (!res.ok) throw new Error('Failed to load accounts');
      const data = await res.json() as ApiAccount[];
      setAccounts(Array.isArray(data) ? data : []);
    } catch {
      setLoadFailed(true);
    } finally {
      setLoading(false);
    }
  }, [authFetch]);

  useEffect(() => {
    load();
  }, [load, currentOrg.slug]);

  const allRows = useMemo(() => flatten(accounts), [accounts]);

  // Search and filters keep the ancestors of every match so the hierarchy stays readable.
  const visibleIds = useMemo(() => {
    const q = query.trim().toLowerCase();
    const byId = new Map(allRows.map((r) => [r.account.id, r]));
    const keep = new Set<number>();
    for (const row of allRows) {
      const a = row.account;
      if (classFilter !== null && a.accountClassId !== classFilter) continue;
      if (q && !a.accountCode.toLowerCase().includes(q) && !a.accountName.toLowerCase().includes(q)) continue;
      if (hideZero && (row.hasChildren || row.rollup === 0)) continue;
      for (let r: Row | undefined = row; r && !keep.has(r.account.id); r = r.parentId === null ? undefined : byId.get(r.parentId)) {
        keep.add(r.account.id);
      }
    }
    return keep;
  }, [allRows, query, classFilter, hideZero]);

  const filtering = query.trim() !== '' || hideZero;

  const rows = useMemo(() => {
    const hiddenUnder = new Set<number>();
    return allRows.filter((row) => {
      if (row.parentId !== null && hiddenUnder.has(row.parentId)) {
        hiddenUnder.add(row.account.id);
        return false;
      }
      if (!visibleIds.has(row.account.id)) return false;
      if (!filtering && collapsed.has(row.account.id)) hiddenUnder.add(row.account.id);
      return true;
    });
  }, [allRows, visibleIds, collapsed, filtering]);

  const classes = useMemo(
    () => Array.from(new Set(accounts.map((a) => a.accountClassId))).sort((a, b) => a - b),
    [accounts]
  );

  const toggle = (id: number, open?: boolean) =>
    setCollapsed((prev) => {
      const next = new Set(prev);
      const isOpen = !next.has(id);
      if (open ?? !isOpen) next.delete(id);
      else next.add(id);
      return next;
    });

  const formatAmount = (value: number) => {
    const text = `${symbol}${Math.abs(value).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
    return value < 0 ? `(${text})` : text;
  };

  const exportCsv = () => {
    const escape = (v: string) => (/[",\n]/.test(v) ? `"${v.replace(/"/g, '""')}"` : v);
    const lines = [['Code', 'Account', 'Class', 'Level', 'Normal balance', 'Header', 'Contra', 'Cash', 'Balance'].join(',')];
    for (const { account: a, level, rollup, hasChildren } of allRows) {
      lines.push([
        a.accountCode, a.accountName, CLASS_LABELS[a.accountClassId] || a.accountClassName || '', String(level),
        a.normalBalance || '', hasChildren ? 'yes' : 'no', a.isContraAccount ? 'yes' : 'no', a.isCash ? 'yes' : 'no',
        rollup.toFixed(2),
      ].map(escape).join(','));
    }
    const url = URL.createObjectURL(new Blob([lines.join('\n')], { type: 'text/csv' }));
    const link = document.createElement('a');
    link.href = url;
    link.download = `${currentOrg.slug}-chart-of-accounts.csv`;
    link.click();
    URL.revokeObjectURL(url);
  };

  // Tree-grid keyboard support: Up/Down move between rows, Right expands, Left collapses or goes to the parent.
  const onRowKeyDown = (e: React.KeyboardEvent<HTMLTableRowElement>, row: Row, index: number) => {
    const focusRow = (i: number) =>
      tableRef.current?.querySelectorAll<HTMLTableRowElement>('tr[data-row]')[i]?.focus();
    const isOpen = !collapsed.has(row.account.id) || filtering;
    if (e.key === 'ArrowDown') focusRow(index + 1);
    else if (e.key === 'ArrowUp') focusRow(index - 1);
    else if (e.key === 'ArrowRight' && row.hasChildren && !isOpen) toggle(row.account.id, true);
    else if (e.key === 'ArrowLeft' && row.hasChildren && isOpen && !filtering) toggle(row.account.id, false);
    else if (e.key === 'ArrowLeft' && row.parentId !== null) focusRow(rows.findIndex((r) => r.account.id === row.parentId));
    else if ((e.key === 'Enter' || e.key === ' ') && row.hasChildren && !filtering) toggle(row.account.id);
    else return;
    e.preventDefault();
  };

  if (!hasPermission('financials.accounts.view')) {
    return <PermissionDeniedCard permission="financials.accounts.view" />;
  }

  const chip = (active: boolean) =>
    `h-8 px-3 rounded-full text-xs font-semibold transition-colors cursor-pointer ${
      active
        ? 'bg-[var(--brand-subtle)] text-[var(--brand-primary)] ring-1 ring-[var(--brand-ring)]'
        : 'bg-[var(--bg-subtle)] text-[var(--text-secondary)] hover:bg-[var(--bg-surface-hover)]'
    }`;

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow={
          <nav aria-label="Breadcrumb">
            <Link to={`/org/${currentOrg.slug}/financials`} className="hover:underline">Financials</Link>
            <span aria-hidden="true"> / </span>
            <span>Chart of Accounts</span>
          </nav>
        }
        title="Chart of Accounts"
        subtitle="Accounts in financial statement order. Group rows show the total of their sub-accounts."
        actions={
          <>
            <HeaderButton label="Refresh accounts" icon={RefreshCw} iconOnly busy={loading} onClick={load} />
            <HeaderButton label="Export CSV" icon={Download} compact disabled={allRows.length === 0} onClick={exportCsv} />
          </>
        }
      />

      <div className="p-4 rounded-2xl bg-[var(--bg-surface)] shadow-md space-y-3">
        <div className="flex flex-col md:flex-row md:items-center gap-3">
          <div className="relative w-full md:w-72">
            <Search className="w-4 h-4 text-[var(--text-muted)] absolute left-3 top-2.5 pointer-events-none" />
            <input
              type="search"
              aria-label="Search accounts by code or name"
              placeholder="Search code or name..."
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              className="w-full h-9 pl-9 pr-3 rounded-xl bg-[var(--bg-subtle)] text-xs text-[var(--text-main)] focus:outline-none focus:ring-1 focus:ring-[var(--brand-ring)]"
            />
          </div>
          <div className="flex flex-wrap items-center gap-2" role="group" aria-label="Filter by account class">
            <button type="button" aria-pressed={classFilter === null} className={chip(classFilter === null)} onClick={() => setClassFilter(null)}>
              All
            </button>
            {classes.map((id) => (
              <button key={id} type="button" aria-pressed={classFilter === id} className={chip(classFilter === id)} onClick={() => setClassFilter(id)}>
                {CLASS_LABELS[id] || `Class ${id}`}
              </button>
            ))}
          </div>
          <label className="md:ml-auto flex items-center gap-2 text-xs text-[var(--text-secondary)] cursor-pointer">
            <input type="checkbox" checked={hideZero} onChange={(e) => setHideZero(e.target.checked)} className="accent-[var(--brand-primary)]" />
            Hide zero balances
          </label>
        </div>

        {loading && accounts.length === 0 ? (
          <div className="py-12 flex items-center justify-center gap-2 text-xs text-[var(--text-muted)]">
            <Loader2 className="w-4 h-4 animate-spin text-[var(--brand-primary)]" />
            Loading chart of accounts...
          </div>
        ) : loadFailed ? (
          <div role="alert" className="py-12 text-center text-xs text-[var(--text-muted)]">
            Could not load the chart of accounts. Try refreshing.
          </div>
        ) : rows.length === 0 ? (
          <div className="py-12 text-center text-xs text-[var(--text-muted)]">No accounts match these filters.</div>
        ) : (
          <div className="overflow-x-auto">
            <table role="treegrid" aria-label="Chart of accounts" className="w-full text-left text-xs border-collapse">
              <thead>
                <tr className="border-b border-[var(--border-subtle)] text-[var(--text-secondary)] font-medium">
                  <th scope="col" className="py-2.5 px-3 w-24">Code</th>
                  <th scope="col" className="py-2.5 px-3">Account</th>
                  <th scope="col" className="py-2.5 px-3 w-20 hidden sm:table-cell">Normal</th>
                  <th scope="col" className="py-2.5 px-3 w-36 text-right">Balance</th>
                </tr>
              </thead>
              <tbody ref={tableRef}>
                {rows.map((row, index) => {
                  const { account: a, level, rollup, hasChildren } = row;
                  const isRoot = level === 1;
                  const open = filtering || !collapsed.has(a.id);
                  const abnormal = isAbnormal(a, rollup);
                  return (
                    <tr
                      key={a.id}
                      data-row
                      tabIndex={index === 0 ? 0 : -1}
                      aria-level={level}
                      aria-expanded={hasChildren ? open : undefined}
                      onKeyDown={(e) => onRowKeyDown(e, row, index)}
                      className={`border-b border-[var(--border-subtle)] focus:outline-none focus:bg-[var(--bg-surface-hover)] ${
                        isRoot ? 'bg-[var(--bg-subtle)] font-bold text-[var(--text-primary)]' : hasChildren ? 'font-semibold text-[var(--text-main)]' : 'text-[var(--text-secondary)]'
                      }`}
                    >
                      <td className="py-2 px-3 font-mono whitespace-nowrap">{a.accountCode}</td>
                      <td className="py-2 px-3">
                        <div className="flex items-center gap-1.5 min-w-0" style={{ paddingLeft: `${(level - 1) * 1.25}rem` }}>
                          {hasChildren ? (
                            <button
                              type="button"
                              tabIndex={-1}
                              aria-label={`${open ? 'Collapse' : 'Expand'} ${a.accountName}`}
                              disabled={filtering}
                              onClick={() => toggle(a.id)}
                              className="p-0.5 rounded text-[var(--text-muted)] hover:text-[var(--text-primary)] cursor-pointer disabled:cursor-default"
                            >
                              {open ? <ChevronDown className="w-3.5 h-3.5" /> : <ChevronRight className="w-3.5 h-3.5" />}
                            </button>
                          ) : (
                            <span className="w-4.5 shrink-0" aria-hidden="true" />
                          )}
                          <span className="truncate">{a.accountName}</span>
                          {a.isContraAccount && <Badge>Contra</Badge>}
                          {a.isCash && <Badge>Cash</Badge>}
                          {hasChildren && !isRoot && <Badge muted>Header</Badge>}
                        </div>
                      </td>
                      <td className="py-2 px-3 hidden sm:table-cell font-mono text-[var(--text-muted)]">
                        {a.normalBalance === 'NA' ? '—' : a.normalBalance}
                      </td>
                      <td className={`py-2 px-3 text-right font-mono whitespace-nowrap ${abnormal ? 'text-amber-500' : ''}`}>
                        {formatAmount(rollup)}
                        {abnormal && <span className="sr-only"> (abnormal balance)</span>}
                        {abnormal && <span aria-hidden="true" title="Abnormal balance: opposite to the account's normal side"> !</span>}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
};

const Badge: React.FC<{ muted?: boolean; children: React.ReactNode }> = ({ muted, children }) => (
  <span
    className={`shrink-0 px-1.5 py-0.5 rounded text-[9px] font-bold uppercase tracking-wide ${
      muted ? 'bg-[var(--bg-subtle)] text-[var(--text-muted)]' : 'bg-[var(--brand-subtle)] text-[var(--brand-primary)]'
    }`}
  >
    {children}
  </span>
);

export default ChartOfAccounts;
