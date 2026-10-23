import React, { useState, useEffect, useMemo, useCallback } from 'react';
import { useOutletContext, Link, useNavigate } from 'react-router-dom';
import {
  Receipt,
  Plus,
  Search,
  RefreshCw,
  AlertTriangle,
  CheckCircle2,
  FileEdit,
  Eye,
  CreditCard,
  Ban,
  X,
  Mail,
  Loader2,
} from 'lucide-react';
import { PageHeader, HeaderButton } from '../components/PageHeader';
import { InvoiceStatusBadge } from '../components/InvoiceStatusBadge';
import { SalesTabs } from '../components/SalesTabs';
import { TenantContextType, Invoice, InvoicingKPIs, RecordPaymentPayload } from '../types';
import { useAuth } from '../auth/AuthContext';
import { currencySymbol as getCurrencySymbol } from '../lib/currency';
import { invoiceTitle } from '../lib/invoiceNumber';

export const Invoices: React.FC = () => {
  const { currentOrg } = useOutletContext<TenantContextType>();
  const { authFetch } = useAuth();
  const navigate = useNavigate();

  // State
  const [invoices, setInvoices] = useState<Invoice[]>([]);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [isRefreshing, setIsRefreshing] = useState<boolean>(false);
  const [errorBanner, setErrorBanner] = useState<string | null>(null);
  const [successBanner, setSuccessBanner] = useState<string | null>(null);

  // Filters
  const [searchQuery, setSearchQuery] = useState('');
  const [statusFilter, setStatusFilter] = useState<'all' | 'unpaid' | 'overdue' | 'paid' | 'draft' | 'void'>('all');

  // Record Payment Modal State
  const [paymentModalInvoice, setPaymentModalInvoice] = useState<Invoice | null>(null);
  const [paymentAmount, setPaymentAmount] = useState<string>('');
  const [paymentDate, setPaymentDate] = useState<string>(new Date().toISOString().split('T')[0]);
  const [paymentMethod, setPaymentMethod] = useState<string>('Bank Transfer');
  const [paymentReference, setPaymentReference] = useState<string>('');
  const [paymentNotes, setPaymentNotes] = useState<string>('');
  const [isSubmittingPayment, setIsSubmittingPayment] = useState<boolean>(false);
  const [paymentError, setPaymentError] = useState<string | null>(null);

  // Void Modal State
  const [voidModalInvoice, setVoidModalInvoice] = useState<Invoice | null>(null);
  const [isSubmittingVoid, setIsSubmittingVoid] = useState<boolean>(false);
  const [voidError, setVoidError] = useState<string | null>(null);

  // Currency Formatter
  const currencySymbol = getCurrencySymbol(currentOrg.currency);

  const formatCurrency = useCallback((val: number) => {
    return `${currencySymbol}${val.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
  }, [currencySymbol]);

  // Fetch Invoices
  const fetchInvoices = useCallback(async (quiet = false) => {
    if (!quiet) setIsLoading(true);
    else setIsRefreshing(true);
    setErrorBanner(null);

    try {
      const res = await authFetch('/api/Sales/SalesInvoices');
      if (!res.ok) {
        throw new Error(`Failed to load invoices (${res.status} ${res.statusText})`);
      }
      const data: Invoice[] = await res.json();
      setInvoices(Array.isArray(data) ? data : []);
    } catch (err: any) {
      console.error('Error fetching invoices:', err);
      setErrorBanner(err.message || 'Failed to load invoices from backend.');
    } finally {
      setIsLoading(false);
      setIsRefreshing(false);
    }
  }, [authFetch]);

  useEffect(() => {
    fetchInvoices();
  }, [fetchInvoices]);

  // KPIs
  const kpis: InvoicingKPIs = useMemo(() => {
    let totalOutstanding = 0;
    let totalOverdue = 0;
    let paidThisMonth = 0;
    let draftsCount = 0;
    let draftsTotal = 0;

    const currentYear = new Date().getFullYear();
    const currentMonth = new Date().getMonth();

    invoices.forEach((inv) => {
      const grandTotal = inv.totalAmountAfterTax ?? inv.amount;

      if (inv.status === 'Draft') {
        draftsCount++;
        draftsTotal += grandTotal;
      } else if (inv.status === 'Void') {
        // Void invoices excluded from totals
      } else {
        // Unpaid or partially paid
        if (inv.balanceDue > 0) {
          totalOutstanding += inv.balanceDue;
          if (inv.status === 'Overdue') {
            totalOverdue += inv.balanceDue;
          }
        }

        // Paid this month
        if (inv.invoiceDate) {
          const invDate = new Date(inv.invoiceDate);
          if (invDate.getFullYear() === currentYear && invDate.getMonth() === currentMonth) {
            paidThisMonth += (inv.paidAmount ?? inv.totalAllocatedAmount ?? 0);
          }
        }
      }
    });

    return {
      totalOutstanding,
      totalOverdue,
      paidThisMonth,
      draftsCount,
      draftsTotal,
    };
  }, [invoices]);

  // Filtered List
  const filteredInvoices = useMemo(() => {
    return invoices.filter((inv) => {
      // 1. Status Filter
      if (statusFilter === 'draft' && inv.status !== 'Draft') return false;
      if (statusFilter === 'overdue' && inv.status !== 'Overdue') return false;
      if (statusFilter === 'paid' && inv.status !== 'Paid') return false;
      if (statusFilter === 'void' && inv.status !== 'Void') return false;
      if (statusFilter === 'unpaid' && (inv.status === 'Paid' || inv.status === 'Draft' || inv.status === 'Void')) return false;

      // 2. Query Search
      if (searchQuery.trim()) {
        const q = searchQuery.toLowerCase().trim();
        const matchNo = (inv.no || '').toLowerCase().includes(q);
        const matchCust = (inv.customerName || '').toLowerCase().includes(q);
        const matchEmail = (inv.customerEmail || '').toLowerCase().includes(q);
        const matchRef = (inv.referenceNo || '').toLowerCase().includes(q);
        return matchNo || matchCust || matchEmail || matchRef;
      }

      return true;
    });
  }, [invoices, statusFilter, searchQuery]);

  // Handle Record Payment Open
  const handleOpenPayment = (inv: Invoice) => {
    setPaymentModalInvoice(inv);
    setPaymentAmount(inv.balanceDue > 0 ? inv.balanceDue.toFixed(2) : inv.amount.toFixed(2));
    setPaymentDate(new Date().toISOString().split('T')[0]);
    setPaymentMethod('Bank Transfer');
    setPaymentReference('');
    setPaymentNotes('');
    setPaymentError(null);
  };

  // Submit Record Payment
  const handleSubmitPayment = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!paymentModalInvoice) return;

    const parsedAmount = parseFloat(paymentAmount);
    if (isNaN(parsedAmount) || parsedAmount <= 0) {
      setPaymentError('Please enter a valid positive payment amount.');
      return;
    }
    const maxPayable = Number(paymentModalInvoice.balanceDue.toFixed(2));
    if (parsedAmount > maxPayable + 0.005) {
      setPaymentError(`Amount cannot exceed the remaining balance due (${formatCurrency(maxPayable)}).`);
      return;
    }

    setIsSubmittingPayment(true);
    setPaymentError(null);

    const payload: RecordPaymentPayload = {
      invoiceId: paymentModalInvoice.id,
      amount: parsedAmount,
      paymentDate: new Date(paymentDate).toISOString(),
      paymentMethod,
      referenceNo: paymentReference.trim() || undefined,
      notes: paymentNotes.trim() || undefined,
    };

    try {
      const res = await authFetch('/api/Sales/RecordPayment', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      });

      if (!res.ok) {
        const errorData = await res.json().catch(() => ({}));
        throw new Error(errorData.message || `Payment recording failed (${res.status})`);
      }

      setSuccessBanner(`Payment of ${formatCurrency(parsedAmount)} recorded for ${invoiceTitle(paymentModalInvoice)}`);
      setPaymentModalInvoice(null);
      await fetchInvoices(true);
      setTimeout(() => setSuccessBanner(null), 5000);
    } catch (err: any) {
      console.error('Record payment error:', err);
      setPaymentError(err.message || 'Failed to record payment.');
    } finally {
      setIsSubmittingPayment(false);
    }
  };

  // Handle Void
  const handleOpenVoid = (inv: Invoice) => {
    setVoidModalInvoice(inv);
    setVoidError(null);
  };

  const handleConfirmVoid = async () => {
    if (!voidModalInvoice) return;
    setIsSubmittingVoid(true);
    setVoidError(null);

    try {
      const res = await authFetch(`/api/Sales/VoidSalesInvoice?id=${voidModalInvoice.id}`, {
        method: 'POST',
      });

      if (!res.ok) {
        const errorData = await res.json().catch(() => ({}));
        throw new Error(errorData.message || `Void failed (${res.status})`);
      }

      setSuccessBanner(`${invoiceTitle(voidModalInvoice)} marked as Void.`);
      setVoidModalInvoice(null);
      await fetchInvoices(true);
      setTimeout(() => setSuccessBanner(null), 5000);
    } catch (err: any) {
      console.error('Void invoice error:', err);
      setVoidError(err.message || 'Failed to void invoice.');
    } finally {
      setIsSubmittingVoid(false);
    }
  };

  // Format Dates
  const formatDate = (dateStr?: string) => {
    if (!dateStr) return '—';
    try {
      const d = new Date(dateStr);
      return d.toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' });
    } catch {
      return dateStr;
    }
  };


  return (
    <div className="space-y-6">
      <PageHeader
        title="Invoices"
        subtitle="Create invoices, track what's been paid, and see who still owes you."
        actions={
          <>
            <HeaderButton
              label="Refresh invoices"
              icon={RefreshCw}
              iconOnly
              busy={isRefreshing}
              onClick={() => fetchInvoices(true)}
            />
            <HeaderButton label="New invoice" icon={Plus} variant="primary" to={`/org/${currentOrg.slug}/sales/new`} />
          </>
        }
      />
      <SalesTabs orgSlug={currentOrg.slug} />

      {/* Success & Error Banners */}
      {successBanner && (
        <div className="p-4 rounded-xl bg-emerald-500/10 border border-emerald-500/20 text-emerald-600 dark:text-emerald-400 text-sm flex items-center justify-between">
          <div className="flex items-center gap-2">
            <CheckCircle2 className="w-4 h-4 shrink-0" />
            <span>{successBanner}</span>
          </div>
          <button onClick={() => setSuccessBanner(null)} className="p-1 hover:opacity-75">
            <X className="w-4 h-4" />
          </button>
        </div>
      )}

      {errorBanner && (
        <div className="p-4 rounded-xl bg-rose-500/10 border border-rose-500/20 text-rose-600 dark:text-rose-400 text-sm flex items-center justify-between">
          <div className="flex items-center gap-2">
            <AlertTriangle className="w-4 h-4 shrink-0" />
            <span>{errorBanner}</span>
          </div>
          <button onClick={() => setErrorBanner(null)} className="p-1 hover:opacity-75">
            <X className="w-4 h-4" />
          </button>
        </div>
      )}

      {/* KPI Summary Cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        {/* Outstanding Receivables */}
        <div className="p-5 rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] shadow-sm">
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold uppercase tracking-wider text-[var(--text-secondary)]">Outstanding</span>
            <div className="w-8 h-8 rounded-lg bg-blue-500/10 text-blue-500 flex items-center justify-center">
              <Receipt className="w-4 h-4" />
            </div>
          </div>
          <div className="mt-3">
            <div className="text-2xl font-bold text-[var(--text-primary)]">
              {formatCurrency(kpis.totalOutstanding)}
            </div>
            <p className="text-xs text-[var(--text-secondary)] mt-1">Total unpaid client balance</p>
          </div>
        </div>

        {/* Overdue Amount */}
        <div className="p-5 rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] shadow-sm">
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold uppercase tracking-wider text-rose-500">Overdue</span>
            <div className="w-8 h-8 rounded-lg bg-rose-500/10 text-rose-500 flex items-center justify-center">
              <AlertTriangle className="w-4 h-4" />
            </div>
          </div>
          <div className="mt-3">
            <div className="text-2xl font-bold text-rose-600 dark:text-rose-400">
              {formatCurrency(kpis.totalOverdue)}
            </div>
            <p className="text-xs text-[var(--text-secondary)] mt-1">Past scheduled payment terms</p>
          </div>
        </div>

        {/* Collected This Month */}
        <div className="p-5 rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] shadow-sm">
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold uppercase tracking-wider text-emerald-500">Paid This Month</span>
            <div className="w-8 h-8 rounded-lg bg-emerald-500/10 text-emerald-500 flex items-center justify-center">
              <CheckCircle2 className="w-4 h-4" />
            </div>
          </div>
          <div className="mt-3">
            <div className="text-2xl font-bold text-emerald-600 dark:text-emerald-400">
              {formatCurrency(kpis.paidThisMonth)}
            </div>
            <p className="text-xs text-[var(--text-secondary)] mt-1">Collected revenue in current period</p>
          </div>
        </div>

        {/* Drafts */}
        <div className="p-5 rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] shadow-sm">
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold uppercase tracking-wider text-slate-400">Drafts</span>
            <div className="w-8 h-8 rounded-lg bg-slate-500/10 text-slate-400 flex items-center justify-center">
              <FileEdit className="w-4 h-4" />
            </div>
          </div>
          <div className="mt-3">
            <div className="text-2xl font-bold text-[var(--text-primary)]">
              {kpis.draftsCount} <span className="text-xs font-normal text-[var(--text-secondary)]">({formatCurrency(kpis.draftsTotal)})</span>
            </div>
            <p className="text-xs text-[var(--text-secondary)] mt-1">Unsent invoices ready to issue</p>
          </div>
        </div>
      </div>

      {/* Controls: Search & Status Filter Tabs */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 border-b border-[var(--border-subtle)] pb-4">
        {/* Filter Tabs */}
        <div className="flex items-center gap-1 overflow-x-auto no-scrollbar">
          {[
            { key: 'all', label: 'All', count: invoices.length },
            { key: 'unpaid', label: 'Unpaid', count: invoices.filter((i) => i.status === 'Open' || i.status === 'Partially Paid' || i.status === 'Overdue').length },
            { key: 'overdue', label: 'Overdue', count: invoices.filter((i) => i.status === 'Overdue').length },
            { key: 'paid', label: 'Paid', count: invoices.filter((i) => i.status === 'Paid').length },
            { key: 'draft', label: 'Drafts', count: invoices.filter((i) => i.status === 'Draft').length },
            { key: 'void', label: 'Void', count: invoices.filter((i) => i.status === 'Void').length },
          ].map((tab) => {
            const isActive = statusFilter === tab.key;
            return (
              <button
                key={tab.key}
                onClick={() => setStatusFilter(tab.key as any)}
                className={`px-3 py-1.5 rounded-lg text-xs font-medium transition-colors whitespace-nowrap flex items-center gap-1.5 ${
                  isActive
                    ? 'bg-[var(--brand-primary)] text-white'
                    : 'text-[var(--text-secondary)] hover:text-[var(--text-primary)] hover:bg-[var(--bg-surface)]'
                }`}
              >
                <span>{tab.label}</span>
                <span className={`px-1.5 py-0.2 rounded-full text-[10px] ${isActive ? 'bg-white/20 text-white' : 'bg-[var(--bg-surface)] text-[var(--text-secondary)] border border-[var(--border-subtle)]'}`}>
                  {tab.count}
                </span>
              </button>
            );
          })}
        </div>

        {/* Search Input */}
        <div className="relative w-full sm:w-64">
          <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-[var(--text-secondary)] pointer-events-none" />
          <input
            type="text"
            placeholder="Search by invoice # or client..."
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            className="w-full pl-9 pr-3 py-1.5 text-xs rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-surface)] text-[var(--text-primary)] placeholder-[var(--text-secondary)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-primary)]"
          />
        </div>
      </div>

      {/* Main Table / Empty State */}
      <div className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] overflow-hidden shadow-sm">
        {isLoading ? (
          <div className="py-20 flex flex-col items-center justify-center text-[var(--text-secondary)] gap-3">
            <Loader2 className="w-8 h-8 animate-spin text-[var(--brand-primary)]" />
            <p className="text-sm">Loading your invoices...</p>
          </div>
        ) : filteredInvoices.length === 0 ? (
          <div className="py-16 px-4 text-center">
            <div className="w-12 h-12 rounded-full bg-[var(--bg-muted)] text-[var(--text-secondary)] mx-auto flex items-center justify-center mb-3">
              <Receipt className="w-6 h-6" />
            </div>
            <h3 className="text-base font-semibold text-[var(--text-primary)]">No invoices found</h3>
            <p className="text-xs text-[var(--text-secondary)] max-w-sm mx-auto mt-1">
              {searchQuery
                ? `No invoices match "${searchQuery}". Try clearing your search or status filter.`
                : 'You have no invoices matching this view. Create your first client invoice to get started.'}
            </p>
            {!searchQuery && (
              <div className="mt-4">
                <Link
                  to={`/org/${currentOrg.slug}/sales/new`}
                  className="inline-flex items-center gap-1.5 px-4 py-2 rounded-lg bg-[var(--brand-primary)] text-white text-xs font-semibold hover:opacity-90"
                >
                  <Plus className="w-3.5 h-3.5" />
                  <span>Create First Invoice</span>
                </Link>
              </div>
            )}
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs border-collapse">
              <thead>
                <tr className="border-b border-[var(--border-subtle)] bg-[var(--bg-muted)]/50 text-[var(--text-secondary)] font-medium">
                  <th className="py-3 px-4">Status</th>
                  <th className="py-3 px-4">Invoice #</th>
                  <th className="py-3 px-4">Client</th>
                  <th className="py-3 px-4">Date</th>
                  <th className="py-3 px-4">Due Date</th>
                  <th className="py-3 px-4 text-right">Total</th>
                  <th className="py-3 px-4 text-right">Balance Due</th>
                  <th className="py-3 px-4 text-right">Actions</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-[var(--border-subtle)]">
                {filteredInvoices.map((inv) => {
                  const grandTotal = inv.totalAmountAfterTax ?? inv.amount;
                  return (
                    <tr
                      key={inv.id}
                      className="hover:bg-[var(--bg-muted)]/30 transition-colors group cursor-pointer"
                      onClick={() => navigate(`/org/${currentOrg.slug}/sales/${inv.id}`)}
                    >
                      {/* Status */}
                      <td className="py-3.5 px-4 whitespace-nowrap" onClick={(e) => e.stopPropagation()}>
                        <InvoiceStatusBadge status={inv.status} />
                      </td>

                      {/* Invoice No */}
                      <td className="py-3.5 px-4 font-semibold text-[var(--brand-primary)] hover:underline whitespace-nowrap">
                        <Link to={`/org/${currentOrg.slug}/sales/${inv.id}`} onClick={(e) => e.stopPropagation()}>
                          {inv.no ? `#${inv.no}` : <span className="font-medium italic text-[var(--text-muted)]">Draft</span>}
                        </Link>
                        {inv.referenceNo && (
                          <div className="text-[10px] text-[var(--text-secondary)] font-normal">
                            Ref: {inv.referenceNo}
                          </div>
                        )}
                      </td>

                      {/* Client */}
                      <td className="py-3.5 px-4 max-w-[200px] truncate">
                        <div className="font-medium text-[var(--text-primary)] truncate">{inv.customerName}</div>
                        {inv.customerEmail && (
                          <div className="text-[10px] text-[var(--text-secondary)] truncate flex items-center gap-1 mt-0.5">
                            <Mail className="w-3 h-3 shrink-0" />
                            <span>{inv.customerEmail}</span>
                          </div>
                        )}
                      </td>

                      {/* Date */}
                      <td className="py-3.5 px-4 text-[var(--text-secondary)] whitespace-nowrap">
                        {formatDate(inv.invoiceDate)}
                      </td>

                      {/* Due Date */}
                      <td className="py-3.5 px-4 whitespace-nowrap">
                        <div className="text-[var(--text-primary)]">{formatDate(inv.dueDate)}</div>
                        {inv.status === 'Overdue' && (
                          <span className="text-[10px] text-rose-500 font-medium">Overdue</span>
                        )}
                      </td>

                      {/* Total Amount */}
                      <td className="py-3.5 px-4 text-right font-medium text-[var(--text-primary)] whitespace-nowrap">
                        {formatCurrency(grandTotal)}
                      </td>

                      {/* Balance Due */}
                      <td className="py-3.5 px-4 text-right whitespace-nowrap">
                        {inv.balanceDue > 0 ? (
                          <span className="font-semibold text-rose-600 dark:text-rose-400">
                            {formatCurrency(inv.balanceDue)}
                          </span>
                        ) : (
                          <span className="text-emerald-600 dark:text-emerald-400 font-medium">Paid</span>
                        )}
                      </td>

                      {/* Actions */}
                      <td className="py-3.5 px-4 text-right whitespace-nowrap" onClick={(e) => e.stopPropagation()}>
                        <div className="flex items-center justify-end gap-1">
                          <Link
                            to={`/org/${currentOrg.slug}/sales/${inv.id}`}
                            className="p-1.5 rounded-lg text-[var(--text-secondary)] hover:text-[var(--text-primary)] hover:bg-[var(--bg-muted)] transition-colors"
                            title="View Invoice"
                          >
                            <Eye className="w-4 h-4" />
                          </Link>

                          {/* Record Payment (issued with a balance due; drafts are issued first) */}
                          {inv.balanceDue > 0 && inv.status !== 'Void' && inv.status !== 'Draft' && (
                            <button
                              onClick={() => handleOpenPayment(inv)}
                              className="p-1.5 rounded-lg text-emerald-600 dark:text-emerald-400 hover:bg-emerald-500/10 transition-colors"
                              title="Record Payment"
                              aria-label="Record payment"
                            >
                              <CreditCard className="w-4 h-4" />
                            </button>
                          )}

                          {/* Void Action */}
                          {inv.status !== 'Void' && inv.status !== 'Paid' && (
                            <button
                              onClick={() => handleOpenVoid(inv)}
                              className="p-1.5 rounded-lg text-slate-400 hover:text-rose-500 hover:bg-rose-500/10 transition-colors"
                              title="Void Invoice"
                              aria-label="Void invoice"
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

      {/* Record Payment Modal */}
      {paymentModalInvoice && (
        <div className="fixed inset-0 z-50 bg-black/60 backdrop-blur-xs flex items-center justify-center p-4">
          <div className="w-full max-w-md bg-[var(--bg-surface)] border border-[var(--border-subtle)] rounded-xl shadow-xl overflow-hidden animate-in fade-in zoom-in-95 duration-150">
            <div className="p-5 border-b border-[var(--border-subtle)] flex items-center justify-between">
              <div>
                <h3 className="text-base font-bold text-[var(--text-primary)]">Record Payment</h3>
                <p className="text-xs text-[var(--text-secondary)] mt-0.5">
                  {invoiceTitle(paymentModalInvoice)} — {paymentModalInvoice.customerName}
                </p>
              </div>
              <button
                onClick={() => setPaymentModalInvoice(null)}
                className="p-1 text-[var(--text-secondary)] hover:text-[var(--text-primary)] rounded-lg"
              >
                <X className="w-5 h-5" />
              </button>
            </div>

            <form onSubmit={handleSubmitPayment} className="p-5 space-y-4 text-xs">
              {paymentError && (
                <div className="p-3 rounded-lg bg-rose-500/10 border border-rose-500/20 text-rose-500 text-xs">
                  {paymentError}
                </div>
              )}

              {/* Outstanding Summary */}
              <div className="p-3 rounded-lg bg-[var(--bg-muted)] border border-[var(--border-subtle)] flex justify-between items-center">
                <span className="text-[var(--text-secondary)]">Remaining Balance Due:</span>
                <span className="font-bold text-sm text-[var(--text-primary)]">
                  {formatCurrency(paymentModalInvoice.balanceDue)}
                </span>
              </div>

              {/* Amount */}
              <div>
                <label className="block font-medium text-[var(--text-primary)] mb-1">
                  Payment Amount ({currencySymbol}) *
                </label>
                <input
                  type="number"
                  step="0.01"
                  min="0.01"
                  max={Number(paymentModalInvoice.balanceDue.toFixed(2))}
                  required
                  value={paymentAmount}
                  onChange={(e) => setPaymentAmount(e.target.value)}
                  className="w-full px-3 py-2 rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-surface)] text-[var(--text-primary)] font-semibold text-sm focus:outline-none focus:ring-2 focus:ring-[var(--brand-primary)]"
                />
              </div>

              {/* Payment Date */}
              <div>
                <label className="block font-medium text-[var(--text-primary)] mb-1">Payment Date *</label>
                <input
                  type="date"
                  required
                  value={paymentDate}
                  onChange={(e) => setPaymentDate(e.target.value)}
                  className="w-full px-3 py-2 rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-surface)] text-[var(--text-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-primary)]"
                />
              </div>

              {/* Payment Method */}
              <div>
                <label className="block font-medium text-[var(--text-primary)] mb-1">Payment Method *</label>
                <select
                  value={paymentMethod}
                  onChange={(e) => setPaymentMethod(e.target.value)}
                  className="w-full px-3 py-2 rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-surface)] text-[var(--text-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-primary)]"
                >
                  <option value="Bank Transfer">Bank Wire / Transfer</option>
                  <option value="Credit Card">Credit Card</option>
                  <option value="Cash">Cash</option>
                  <option value="Check">Check</option>
                  <option value="PayPal">PayPal</option>
                  <option value="Stripe">Stripe</option>
                  <option value="Other">Other</option>
                </select>
              </div>

              {/* Reference */}
              <div>
                <label className="block font-medium text-[var(--text-primary)] mb-1">
                  Reference # (e.g. Check / Wire ID)
                </label>
                <input
                  type="text"
                  placeholder="Optional reference code"
                  value={paymentReference}
                  onChange={(e) => setPaymentReference(e.target.value)}
                  className="w-full px-3 py-2 rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-surface)] text-[var(--text-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-primary)]"
                />
              </div>

              {/* Notes */}
              <div>
                <label className="block font-medium text-[var(--text-primary)] mb-1">Notes / Memo</label>
                <textarea
                  rows={2}
                  placeholder="Optional notes for this payment..."
                  value={paymentNotes}
                  onChange={(e) => setPaymentNotes(e.target.value)}
                  className="w-full px-3 py-2 rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-surface)] text-[var(--text-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-primary)]"
                />
              </div>

              {/* Buttons */}
              <div className="pt-2 flex justify-end gap-2">
                <button
                  type="button"
                  onClick={() => setPaymentModalInvoice(null)}
                  disabled={isSubmittingPayment}
                  className="px-4 py-2 rounded-lg border border-[var(--border-subtle)] text-[var(--text-secondary)] hover:bg-[var(--bg-muted)] font-medium"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={isSubmittingPayment}
                  className="px-4 py-2 rounded-lg bg-emerald-600 hover:bg-emerald-500 text-white font-semibold flex items-center gap-2"
                >
                  {isSubmittingPayment && <Loader2 className="w-3.5 h-3.5 animate-spin" />}
                  <span>Record Payment</span>
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Void Modal */}
      {voidModalInvoice && (
        <div className="fixed inset-0 z-50 bg-black/60 backdrop-blur-xs flex items-center justify-center p-4">
          <div className="w-full max-w-md bg-[var(--bg-surface)] border border-[var(--border-subtle)] rounded-xl shadow-xl overflow-hidden animate-in fade-in zoom-in-95 duration-150">
            <div className="p-5 border-b border-[var(--border-subtle)] flex items-center justify-between">
              <div className="flex items-center gap-2 text-rose-500">
                <AlertTriangle className="w-5 h-5 shrink-0" />
                <h3 className="text-base font-bold text-[var(--text-primary)]">Void Invoice</h3>
              </div>
              <button
                onClick={() => setVoidModalInvoice(null)}
                className="p-1 text-[var(--text-secondary)] hover:text-[var(--text-primary)] rounded-lg"
              >
                <X className="w-5 h-5" />
              </button>
            </div>

            <div className="p-5 space-y-3 text-xs">
              {voidError && (
                <div className="p-3 rounded-lg bg-rose-500/10 border border-rose-500/20 text-rose-500 text-xs">
                  {voidError}
                </div>
              )}
              <p className="text-[var(--text-secondary)]">
                Are you sure you want to void{' '}
                <strong className="text-[var(--text-primary)]">
                  {voidModalInvoice.no ? `invoice #${voidModalInvoice.no}` : 'this draft'}
                </strong>{' '}
                for <strong className="text-[var(--text-primary)]">{voidModalInvoice.customerName}</strong>?
              </p>
              <p className="text-xs text-[var(--text-secondary)] bg-[var(--bg-muted)] p-3 rounded-lg border border-[var(--border-subtle)]">
                Voiding marks this invoice as cancelled. If this invoice was already posted, double-entry audit records are preserved in full compliance with accounting standards.
              </p>

              <div className="pt-2 flex justify-end gap-2">
                <button
                  type="button"
                  onClick={() => setVoidModalInvoice(null)}
                  disabled={isSubmittingVoid}
                  className="px-4 py-2 rounded-lg border border-[var(--border-subtle)] text-[var(--text-secondary)] hover:bg-[var(--bg-muted)] font-medium"
                >
                  Cancel
                </button>
                <button
                  type="button"
                  onClick={handleConfirmVoid}
                  disabled={isSubmittingVoid}
                  className="px-4 py-2 rounded-lg bg-rose-600 hover:bg-rose-500 text-white font-semibold flex items-center gap-2"
                >
                  {isSubmittingVoid && <Loader2 className="w-3.5 h-3.5 animate-spin" />}
                  <span>Confirm Void</span>
                </button>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
