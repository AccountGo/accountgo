import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { Link, useNavigate, useOutletContext } from 'react-router-dom';
import { AlertTriangle, Ban, CheckCircle2, FileEdit, Loader2, Plus, ReceiptText, RefreshCw, Search, Wallet, X } from 'lucide-react';
import { PageHeader, HeaderButton } from '../components/PageHeader';
import { PurchasesTabs } from '../components/PurchasesTabs';
import { InvoiceStatusBadge } from '../components/InvoiceStatusBadge';
import { PayBillDialog } from '../components/PayBillDialog';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { usePermissions } from '../components/Gating';
import { Bill, TenantContextType } from '../types';
import { useAuth } from '../auth/AuthContext';
import { currencySymbol as getCurrencySymbol } from '../lib/currency';
import { BillTitle, billTitle } from '../lib/billTitle';

type StatusFilter = 'all' | 'unpaid' | 'overdue' | 'paid' | 'draft' | 'void';

/** The bills a workspace has to pay: what it owes, what is overdue, and the drafts waiting to be approved. */
export const Bills: React.FC = () => {
  const { currentOrg } = useOutletContext<TenantContextType>();
  const { authFetch } = useAuth();
  const navigate = useNavigate();
  const { hasPermission } = usePermissions();
  const canCreate = hasPermission('purchases.bills.create');
  const canPay = hasPermission('purchases.payments.create');
  const canVoid = hasPermission('purchases.bills.post');

  const [bills, setBills] = useState<Bill[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [query, setQuery] = useState('');
  const [filter, setFilter] = useState<StatusFilter>('all');
  const [paying, setPaying] = useState<Bill | null>(null);
  const [voiding, setVoiding] = useState<Bill | null>(null);
  const [isVoiding, setIsVoiding] = useState(false);

  const currencySymbol = getCurrencySymbol(currentOrg.currency);
  const formatCurrency = useCallback(
    (val: number) => `${currencySymbol}${val.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`,
    [currencySymbol]
  );
  const formatDate = (value?: string | null) =>
    value ? new Date(value).toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' }) : '—';
  const newBillPath = `/org/${currentOrg.slug}/purchases/new`;
  const billPath = (id: number) => `/org/${currentOrg.slug}/purchases/${id}`;

  const load = useCallback(
    async (quiet = false) => {
      if (quiet) setIsRefreshing(true);
      else setIsLoading(true);
      setError(null);
      try {
        const res = await authFetch('/api/Purchasing/Bills');
        if (!res.ok) throw new Error(`Bills could not be loaded (${res.status}).`);
        setBills(await res.json());
      } catch (err: any) {
        setError(err.message || 'Bills could not be loaded.');
      } finally {
        setIsLoading(false);
        setIsRefreshing(false);
      }
    },
    [authFetch]
  );

  useEffect(() => {
    load();
  }, [load]);

  const kpis = useMemo(() => {
    const now = new Date();
    let owed = 0;
    let overdue = 0;
    let paidThisMonth = 0;
    let drafts = 0;
    let draftsTotal = 0;
    for (const bill of bills) {
      if (bill.status === 'Draft') {
        drafts++;
        draftsTotal += bill.total;
        continue;
      }
      if (bill.status === 'Void') continue;
      owed += bill.balanceDue;
      if (bill.status === 'Overdue') overdue += bill.balanceDue;
      for (const payment of bill.payments) {
        const paid = new Date(payment.date);
        if (paid.getFullYear() === now.getFullYear() && paid.getMonth() === now.getMonth()) paidThisMonth += payment.amount;
      }
    }
    return { owed, overdue, paidThisMonth, drafts, draftsTotal };
  }, [bills]);

  const matchesFilter = (bill: Bill, key: StatusFilter) =>
    key === 'all' ||
    (key === 'unpaid' && (bill.status === 'Open' || bill.status === 'Partially Paid' || bill.status === 'Overdue')) ||
    (key === 'overdue' && bill.status === 'Overdue') ||
    (key === 'paid' && bill.status === 'Paid') ||
    (key === 'draft' && bill.status === 'Draft') ||
    (key === 'void' && bill.status === 'Void');

  const shown = useMemo(() => {
    const terms = query.trim().toLowerCase().split(/\s+/).filter(Boolean);
    return bills.filter((bill) => {
      if (!matchesFilter(bill, filter)) return false;
      const haystack = `${bill.vendorName ?? ''} ${bill.vendorInvoiceNo ?? ''} ${bill.no ?? ''} ${bill.referenceNo ?? ''}`.toLowerCase();
      return terms.every((t) => haystack.includes(t));
    });
  }, [bills, filter, query]);

  const replaceBill = (updated: Bill) => setBills((prev) => prev.map((b) => (b.id === updated.id ? updated : b)));

  const confirmVoid = async () => {
    if (!voiding) return;
    setIsVoiding(true);
    try {
      const res = await authFetch(`/api/Purchasing/VoidBill?id=${voiding.id}`, { method: 'POST' });
      const data = await res.json().catch(() => null);
      if (!res.ok) throw new Error(data?.message || 'The bill could not be voided.');
      replaceBill(data);
      setNotice(`${BillTitle(voiding)} is void.`);
    } catch (err: any) {
      setError(err.message);
    } finally {
      setIsVoiding(false);
      setVoiding(null);
    }
  };

  const tiles = [
    { label: 'You owe', value: formatCurrency(kpis.owed), note: 'Unpaid on approved bills', tone: 'text-[var(--text-primary)]', icon: ReceiptText, iconTone: 'bg-blue-500/10 text-blue-500' },
    { label: 'Overdue', value: formatCurrency(kpis.overdue), note: 'Past the due date', tone: kpis.overdue ? 'text-rose-600 dark:text-rose-400' : 'text-[var(--text-primary)]', icon: AlertTriangle, iconTone: 'bg-rose-500/10 text-rose-500' },
    { label: 'Paid this month', value: formatCurrency(kpis.paidThisMonth), note: 'Payments made this month', tone: 'text-emerald-600 dark:text-emerald-400', icon: CheckCircle2, iconTone: 'bg-emerald-500/10 text-emerald-500' },
    { label: 'Drafts', value: `${kpis.drafts}`, note: `${formatCurrency(kpis.draftsTotal)} waiting to be approved`, tone: 'text-[var(--text-primary)]', icon: FileEdit, iconTone: 'bg-slate-500/10 text-slate-400' },
  ];

  return (
    <div className="space-y-6">
      <PageHeader
        title="Bills"
        subtitle="Enter the bills your vendors send, approve them, and keep track of what you owe."
        actions={
          <>
            <HeaderButton label="Refresh bills" icon={RefreshCw} iconOnly busy={isRefreshing} onClick={() => load(true)} />
            {canCreate && <HeaderButton label="New bill" icon={Plus} variant="primary" to={newBillPath} />}
          </>
        }
      />
      <PurchasesTabs orgSlug={currentOrg.slug} />

      {notice && (
        <div role="status" className="p-4 rounded-xl bg-emerald-500/10 border border-emerald-500/20 text-emerald-600 dark:text-emerald-400 text-sm flex items-center justify-between">
          <div className="flex items-center gap-2">
            <CheckCircle2 className="w-4 h-4 shrink-0" />
            <span>{notice}</span>
          </div>
          <button onClick={() => setNotice(null)} aria-label="Dismiss" className="p-1 hover:opacity-75">
            <X className="w-4 h-4" />
          </button>
        </div>
      )}
      {error && (
        <div role="alert" className="p-4 rounded-xl bg-rose-500/10 border border-rose-500/20 text-rose-600 dark:text-rose-400 text-sm flex items-center justify-between">
          <div className="flex items-center gap-2">
            <AlertTriangle className="w-4 h-4 shrink-0" />
            <span>{error}</span>
          </div>
          <button onClick={() => setError(null)} aria-label="Dismiss" className="p-1 hover:opacity-75">
            <X className="w-4 h-4" />
          </button>
        </div>
      )}

      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        {tiles.map((tile) => (
          <div key={tile.label} className="p-5 rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] shadow-sm">
            <div className="flex items-center justify-between">
              <span className="text-xs font-semibold uppercase tracking-wider text-[var(--text-secondary)]">{tile.label}</span>
              <div className={`w-8 h-8 rounded-lg flex items-center justify-center ${tile.iconTone}`}>
                <tile.icon className="w-4 h-4" />
              </div>
            </div>
            <div className={`mt-3 text-2xl font-bold ${tile.tone}`}>{tile.value}</div>
            <p className="text-xs text-[var(--text-secondary)] mt-1">{tile.note}</p>
          </div>
        ))}
      </div>

      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 border-b border-[var(--border-subtle)] pb-4">
        <div className="flex items-center gap-1 overflow-x-auto no-scrollbar">
          {([
            ['all', 'All'],
            ['unpaid', 'Unpaid'],
            ['overdue', 'Overdue'],
            ['paid', 'Paid'],
            ['draft', 'Drafts'],
            ['void', 'Void'],
          ] as const).map(([key, label]) => {
            const isActive = filter === key;
            return (
              <button
                key={key}
                onClick={() => setFilter(key)}
                aria-pressed={isActive}
                className={`px-3 py-1.5 rounded-lg text-xs font-medium transition-colors whitespace-nowrap flex items-center gap-1.5 ${
                  isActive
                    ? 'bg-[var(--brand-primary)] text-white'
                    : 'text-[var(--text-secondary)] hover:text-[var(--text-primary)] hover:bg-[var(--bg-surface)]'
                }`}
              >
                <span>{label}</span>
                <span className={`px-1.5 rounded-full text-[10px] ${isActive ? 'bg-white/20 text-white' : 'bg-[var(--bg-surface)] text-[var(--text-secondary)] border border-[var(--border-subtle)]'}`}>
                  {bills.filter((b) => matchesFilter(b, key)).length}
                </span>
              </button>
            );
          })}
        </div>
        <div className="relative w-full sm:w-64">
          <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-[var(--text-secondary)] pointer-events-none" />
          <input
            type="search"
            aria-label="Search bills"
            placeholder="Search by vendor or bill #"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            className="w-full pl-9 pr-3 py-1.5 text-xs rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-surface)] text-[var(--text-primary)] placeholder-[var(--text-secondary)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-primary)]"
          />
        </div>
      </div>

      <div className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] overflow-hidden shadow-sm">
        {isLoading ? (
          <div className="py-20 flex flex-col items-center justify-center text-[var(--text-secondary)] gap-3">
            <Loader2 className="w-8 h-8 animate-spin text-[var(--brand-primary)]" />
            <p className="text-sm">Loading bills...</p>
          </div>
        ) : shown.length === 0 ? (
          <div className="py-16 px-4 text-center">
            <div className="w-12 h-12 rounded-full bg-[var(--bg-muted)] text-[var(--text-secondary)] mx-auto flex items-center justify-center mb-3">
              <ReceiptText className="w-6 h-6" />
            </div>
            <h3 className="text-base font-semibold text-[var(--text-primary)]">{bills.length === 0 ? 'No bills yet' : 'No matching bills'}</h3>
            <p className="text-xs text-[var(--text-secondary)] max-w-sm mx-auto mt-1">
              {bills.length === 0
                ? 'When a vendor sends you a bill, enter it here. Approving it records what you owe; paying it records the money going out.'
                : query
                  ? `No bills match "${query}".`
                  : 'No bills in this view.'}
            </p>
            {bills.length === 0 && canCreate && (
              <Link
                to={newBillPath}
                className="mt-4 inline-flex items-center gap-1.5 px-4 py-2 rounded-lg bg-[var(--brand-primary)] text-white text-xs font-semibold hover:opacity-90"
              >
                <Plus className="w-3.5 h-3.5" />
                Enter a bill
              </Link>
            )}
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs border-collapse">
              <thead>
                <tr className="border-b border-[var(--border-subtle)] bg-[var(--bg-muted)]/50 text-[var(--text-secondary)] font-medium">
                  <th className="py-3 px-4">Status</th>
                  <th className="py-3 px-4">Vendor</th>
                  <th className="hidden sm:table-cell py-3 px-4">Bill #</th>
                  <th className="hidden md:table-cell py-3 px-4">Date</th>
                  <th className="hidden sm:table-cell py-3 px-4">Due date</th>
                  <th className="hidden md:table-cell py-3 px-4 text-right">Total</th>
                  <th className="py-3 px-4 text-right">Balance due</th>
                  <th className="py-3 px-4 text-right">
                    <span className="sr-only">Actions</span>
                  </th>
                </tr>
              </thead>
              <tbody className="divide-y divide-[var(--border-subtle)]">
                {shown.map((bill) => {
                  const to = billPath(bill.id);
                  const payable = bill.status !== 'Draft' && bill.status !== 'Void' && bill.balanceDue > 0;
                  return (
                    <tr key={bill.id} className="hover:bg-[var(--bg-muted)]/30 transition-colors cursor-pointer" onClick={() => navigate(to)}>
                      <td className="py-3.5 px-4 whitespace-nowrap">
                        <InvoiceStatusBadge status={bill.status} />
                      </td>
                      <td className="py-3.5 px-4 max-w-[220px]">
                        <Link to={to} onClick={(e) => e.stopPropagation()} className="block truncate font-medium text-[var(--text-primary)] hover:underline">
                          {bill.vendorName}
                        </Link>
                        <div className="sm:hidden text-[10px] text-[var(--text-secondary)] truncate">
                          {bill.vendorInvoiceNo || (bill.no ? `#${bill.no}` : 'Draft')} · Due {formatDate(bill.dueDate)}
                        </div>
                      </td>
                      <td className="hidden sm:table-cell py-3.5 px-4 whitespace-nowrap">
                        <div className="font-medium text-[var(--text-primary)]">{bill.vendorInvoiceNo || '—'}</div>
                        <div className="text-[10px] text-[var(--text-secondary)]">{bill.no ? `#${bill.no}` : 'Draft'}</div>
                      </td>
                      <td className="hidden md:table-cell py-3.5 px-4 text-[var(--text-secondary)] whitespace-nowrap">{formatDate(bill.date)}</td>
                      <td className="hidden sm:table-cell py-3.5 px-4 whitespace-nowrap">
                        <span className={bill.status === 'Overdue' ? 'font-medium text-rose-600 dark:text-rose-400' : 'text-[var(--text-secondary)]'}>
                          {formatDate(bill.dueDate)}
                        </span>
                      </td>
                      <td className="hidden md:table-cell py-3.5 px-4 text-right font-medium text-[var(--text-primary)] whitespace-nowrap">{formatCurrency(bill.total)}</td>
                      <td className="py-3.5 px-4 text-right whitespace-nowrap font-semibold text-[var(--text-primary)]">
                        {bill.status === 'Draft' || bill.status === 'Void' ? <span className="font-normal text-[var(--text-muted)]">—</span> : formatCurrency(bill.balanceDue)}
                      </td>
                      <td className="py-3.5 px-4 text-right whitespace-nowrap" onClick={(e) => e.stopPropagation()}>
                        <div className="flex items-center justify-end gap-1">
                          {canPay && payable && (
                            <button
                              onClick={() => setPaying(bill)}
                              className="p-1.5 rounded-lg text-emerald-600 dark:text-emerald-400 hover:bg-emerald-500/10 transition-colors"
                              title="Pay bill"
                              aria-label={`Pay ${billTitle(bill)}`}
                            >
                              <Wallet className="w-4 h-4" />
                            </button>
                          )}
                          {canVoid && bill.status !== 'Void' && bill.status !== 'Draft' && bill.amountPaid === 0 && (
                            <button
                              onClick={() => setVoiding(bill)}
                              className="p-1.5 rounded-lg text-slate-400 hover:text-rose-500 hover:bg-rose-500/10 transition-colors"
                              title="Void bill"
                              aria-label={`Void ${billTitle(bill)}`}
                            >
                              <Ban className="w-4 h-4" />
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {paying && (
        <PayBillDialog
          bill={paying}
          currencySymbol={currencySymbol}
          formatCurrency={formatCurrency}
          onClose={() => setPaying(null)}
          onPaid={(updated) => {
            replaceBill(updated);
            setNotice(`Payment recorded on ${billTitle(updated)}.`);
            setPaying(null);
          }}
        />
      )}

      {voiding && (
        <ConfirmDialog
          title={`Void ${billTitle(voiding)}?`}
          message="Voiding reverses what approving it posted, so it no longer counts as owed or spent. It stays in the list as void. This can't be undone."
          confirmLabel="Void bill"
          destructive
          busy={isVoiding}
          onConfirm={confirmVoid}
          onCancel={() => setVoiding(null)}
        />
      )}
    </div>
  );
};
