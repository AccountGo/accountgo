import React, { useState, useEffect, useCallback } from 'react';
import { useOutletContext, useParams, Link } from 'react-router-dom';
import {
  ArrowLeft,
  Printer,
  CreditCard,
  FileEdit,
  Ban,
  Clock,
  CheckCircle2,
  AlertTriangle,
  Receipt,
  X,
  Loader2,
  Mail,
  Phone,
  MapPin,
  Send,
  FileDown,
} from 'lucide-react';
import {
  TenantContextType,
  Invoice,
  InvoiceStatus,
  RecordPaymentPayload,
} from '../types';
import { useAuth } from '../auth/AuthContext';
import { currencySymbol as getCurrencySymbol } from '../lib/currency';

export const InvoiceDetail: React.FC = () => {
  const { currentOrg } = useOutletContext<TenantContextType>();
  const { authFetch } = useAuth();
  const { orgSlug, invoiceId } = useParams<{ orgSlug: string; invoiceId: string }>();

  // State
  const [invoice, setInvoice] = useState<Invoice | null>(null);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [errorBanner, setErrorBanner] = useState<string | null>(null);
  const [successBanner, setSuccessBanner] = useState<string | null>(null);

  // Payment Modal State
  const [isPaymentModalOpen, setIsPaymentModalOpen] = useState<boolean>(false);
  const [paymentAmount, setPaymentAmount] = useState<string>('');
  const [paymentDate, setPaymentDate] = useState<string>(
    new Date().toISOString().split('T')[0]
  );
  const [paymentMethod, setPaymentMethod] = useState<string>('Bank Transfer');
  const [paymentReference, setPaymentReference] = useState<string>('');
  const [paymentNotes, setPaymentNotes] = useState<string>('');
  const [isSubmittingPayment, setIsSubmittingPayment] = useState<boolean>(false);
  const [paymentError, setPaymentError] = useState<string | null>(null);

  // Void Modal State
  const [isVoidModalOpen, setIsVoidModalOpen] = useState<boolean>(false);
  const [isSubmittingVoid, setIsSubmittingVoid] = useState<boolean>(false);
  const [voidError, setVoidError] = useState<string | null>(null);

  // Currency
  const currencySymbol = getCurrencySymbol(currentOrg.currency);

  const formatCurrency = useCallback(
    (val: number) => {
      return `${currencySymbol}${val.toLocaleString(undefined, {
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
      })}`;
    },
    [currencySymbol]
  );

  // Fetch Invoice
  const fetchInvoice = useCallback(async () => {
    if (!invoiceId) return;
    setIsLoading(true);
    setErrorBanner(null);

    try {
      const res = await authFetch(`/api/Sales/SalesInvoice?id=${invoiceId}`);
      if (!res.ok) {
        throw new Error(`Failed to load invoice #${invoiceId} (${res.status})`);
      }
      const data: Invoice = await res.json();
      setInvoice(data);
      if (data.balanceDue > 0) {
        setPaymentAmount(data.balanceDue.toFixed(2));
      }
    } catch (err: any) {
      console.error('Error loading invoice:', err);
      setErrorBanner(err.message || 'Invoice not found.');
    } finally {
      setIsLoading(false);
    }
  }, [authFetch, invoiceId]);

  useEffect(() => {
    fetchInvoice();
  }, [fetchInvoice]);

  // PDF is rendered by the API so it looks the same whatever browser saves it.
  const [isDownloadingPdf, setIsDownloadingPdf] = useState<boolean>(false);
  const handleDownloadPdf = async () => {
    if (!invoice) return;
    setIsDownloadingPdf(true);
    setErrorBanner(null);
    try {
      const res = await authFetch(`/api/Sales/SalesInvoicePdf?id=${invoice.id}`);
      if (!res.ok) {
        throw new Error(`Could not generate the PDF (${res.status})`);
      }
      const blob = await res.blob();
      const fileName =
        res.headers.get('Content-Disposition')?.match(/filename="?([^";]+)"?/)?.[1] ||
        `Invoice-${invoice.no || invoice.id}.pdf`;
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = fileName;
      document.body.appendChild(link);
      link.click();
      link.remove();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
    } catch (err: any) {
      setErrorBanner(err.message || 'Could not generate the PDF.');
    } finally {
      setIsDownloadingPdf(false);
    }
  };

  // Payment Submission
  const handleRecordPayment = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!invoice) return;

    const amountNum = parseFloat(paymentAmount);
    if (isNaN(amountNum) || amountNum <= 0) {
      setPaymentError('Please enter a valid positive payment amount.');
      return;
    }
    const maxPayable = Number(invoice.balanceDue.toFixed(2));
    if (amountNum > maxPayable + 0.005) {
      setPaymentError(
        `Amount cannot exceed the current balance due (${formatCurrency(maxPayable)}).`
      );
      return;
    }

    setIsSubmittingPayment(true);
    setPaymentError(null);

    try {
      const payload: RecordPaymentPayload = {
        invoiceId: invoice.id,
        amount: amountNum,
        paymentDate: paymentDate,
        paymentMethod: paymentMethod,
        referenceNo: paymentReference.trim() || undefined,
        notes: paymentNotes.trim() || undefined,
      };

      const res = await authFetch('/api/Sales/RecordPayment', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      });

      if (!res.ok) {
        const errorData = await res.json().catch(() => null);
        throw new Error(errorData?.message || 'Failed to record payment.');
      }

      setSuccessBanner(`Payment of ${formatCurrency(amountNum)} recorded successfully!`);
      setIsPaymentModalOpen(false);
      setPaymentReference('');
      setPaymentNotes('');
      // Reload updated invoice
      await fetchInvoice();
    } catch (err: any) {
      setPaymentError(err.message || 'An error occurred while saving the payment.');
    } finally {
      setIsSubmittingPayment(false);
    }
  };

  // Void Submission
  const handleVoidInvoice = async () => {
    if (!invoice) return;
    setIsSubmittingVoid(true);
    setVoidError(null);

    try {
      const res = await authFetch(`/api/Sales/VoidSalesInvoice?id=${invoice.id}`, {
        method: 'POST',
      });

      if (!res.ok) {
        const errorData = await res.json().catch(() => null);
        throw new Error(errorData?.message || 'Failed to void invoice.');
      }

      setSuccessBanner(`Invoice #${invoice.no || invoice.id} has been voided.`);
      setIsVoidModalOpen(false);
      await fetchInvoice();
    } catch (err: any) {
      setVoidError(err.message || 'An error occurred while voiding the invoice.');
    } finally {
      setIsSubmittingVoid(false);
    }
  };

  // Issue Draft Invoice
  const [isSubmittingIssue, setIsSubmittingIssue] = useState<boolean>(false);
  const handleIssueInvoice = async () => {
    if (!invoice) return;
    setIsSubmittingIssue(true);
    setErrorBanner(null);

    try {
      const res = await authFetch(`/api/Sales/IssueSalesInvoice?id=${invoice.id}`, {
        method: 'POST',
      });

      if (!res.ok) {
        const errorData = await res.json().catch(() => null);
        throw new Error(errorData?.message || 'Failed to issue invoice.');
      }

      setSuccessBanner(`Invoice #${invoice.no || invoice.id} has been issued successfully!`);
      await fetchInvoice();
    } catch (err: any) {
      setErrorBanner(err.message || 'An error occurred while issuing the invoice.');
    } finally {
      setIsSubmittingIssue(false);
    }
  };

  // Status Badge Component
  const getStatusBadge = (status: InvoiceStatus) => {
    switch (status) {
      case 'Paid':
        return (
          <span className="inline-flex items-center gap-1.5 px-3 py-1 text-xs font-semibold rounded-full bg-emerald-500/10 text-emerald-500 border border-emerald-500/20">
            <CheckCircle2 className="w-3.5 h-3.5" /> Paid
          </span>
        );
      case 'Partially Paid':
        return (
          <span className="inline-flex items-center gap-1.5 px-3 py-1 text-xs font-semibold rounded-full bg-amber-500/10 text-amber-500 border border-amber-500/20">
            <Clock className="w-3.5 h-3.5" /> Partially Paid
          </span>
        );
      case 'Overdue':
        return (
          <span className="inline-flex items-center gap-1.5 px-3 py-1 text-xs font-semibold rounded-full bg-rose-500/10 text-rose-500 border border-rose-500/20">
            <AlertTriangle className="w-3.5 h-3.5" /> Overdue
          </span>
        );
      case 'Draft':
        return (
          <span className="inline-flex items-center gap-1.5 px-3 py-1 text-xs font-semibold rounded-full bg-slate-500/10 text-slate-400 border border-slate-500/20">
            <FileEdit className="w-3.5 h-3.5" /> Draft
          </span>
        );
      case 'Void':
        return (
          <span className="inline-flex items-center gap-1.5 px-3 py-1 text-xs font-semibold rounded-full bg-neutral-500/10 text-neutral-400 border border-neutral-500/20 line-through">
            <Ban className="w-3.5 h-3.5" /> Void
          </span>
        );
      case 'Open':
      default:
        return (
          <span className="inline-flex items-center gap-1.5 px-3 py-1 text-xs font-semibold rounded-full bg-blue-500/10 text-blue-400 border border-blue-500/20">
            <Clock className="w-3.5 h-3.5" /> Open / Unpaid
          </span>
        );
    }
  };

  if (isLoading) {
    return (
      <div className="flex flex-col items-center justify-center min-h-[50vh] text-[var(--text-muted)]">
        <Loader2 className="w-8 h-8 animate-spin text-[var(--brand-primary)] mb-3" />
        <p className="text-sm">Loading invoice details...</p>
      </div>
    );
  }

  if (!invoice) {
    return (
      <div className="max-w-4xl mx-auto px-4 py-12 text-center">
        <AlertTriangle className="w-12 h-12 text-amber-500 mx-auto mb-3" />
        <h2 className="text-xl font-bold text-[var(--text-main)] mb-2">
          Invoice Not Found
        </h2>
        <p className="text-sm text-[var(--text-secondary)] mb-6">
          The requested invoice #{invoiceId} could not be retrieved.
        </p>
        <Link
          to={`/org/${orgSlug}/sales`}
          className="inline-flex items-center gap-2 px-5 py-2.5 text-sm font-semibold text-[var(--brand-text)] bg-[var(--brand-primary)] rounded-xl hover:bg-[var(--brand-hover)] transition shadow-sm"
        >
          <ArrowLeft className="w-4 h-4" /> Back to Invoices
        </Link>
      </div>
    );
  }

  const isOverdue =
    invoice.status === 'Overdue' ||
    (invoice.dueDate &&
      new Date(invoice.dueDate).getTime() < new Date().setHours(0, 0, 0, 0) &&
      invoice.balanceDue > 0);

  // Seller and display flags are resolved by the API from the workspace's invoice settings, same as the PDF.
  const sellerName = invoice.seller?.name || currentOrg.name || invoice.companyName || 'AccountGo Organization';
  const sellerContact = [invoice.seller?.email, invoice.seller?.phone].filter(Boolean).join(' · ');
  const showCustomerEmail = invoice.display?.showCustomerEmail ?? true;
  const showNotes = invoice.display?.showNotes ?? true;
  const showPaymentInstructions = invoice.display?.showPaymentInstructions ?? true;

  return (
    <div className="print-page max-w-5xl mx-auto px-4 sm:px-6 py-6 pb-24 text-[var(--text-main)]">

      {/* Top Navigation & Action Bar (Hidden on Print) */}
      <div className="no-print flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 mb-6">
        <div className="flex items-center gap-3">
          <Link
            to={`/org/${orgSlug}/sales`}
            className="p-2 text-[var(--text-secondary)] hover:text-[var(--text-main)] rounded-xl hover:bg-[var(--bg-surface-hover)] border border-[var(--border-subtle)] transition"
            title="Back to Invoices"
          >
            <ArrowLeft className="w-5 h-5" />
          </Link>
          <div className="flex items-center gap-3">
            <h1 className="text-2xl font-bold tracking-tight text-[var(--text-main)]">
              Invoice {invoice.no ? `#${invoice.no}` : `#${invoice.id}`}
            </h1>
            {getStatusBadge(invoice.status)}
          </div>
        </div>

        {/* Action Buttons */}
        <div className="flex items-center flex-wrap gap-2.5">
          {/* Download PDF Button */}
          <button
            type="button"
            onClick={handleDownloadPdf}
            disabled={isDownloadingPdf}
            className="inline-flex items-center gap-1.5 px-3.5 py-2 text-sm font-medium text-[var(--text-secondary)] bg-[var(--bg-surface)] border border-[var(--border-subtle)] rounded-xl hover:bg-[var(--bg-surface-hover)] hover:text-[var(--text-main)] shadow-sm transition disabled:opacity-50"
          >
            {isDownloadingPdf ? (
              <Loader2 className="w-4 h-4 animate-spin text-[var(--text-muted)]" />
            ) : (
              <FileDown className="w-4 h-4 text-[var(--text-muted)]" />
            )}
            Download PDF
          </button>

          {/* Print Button */}
          <button
            type="button"
            onClick={() => window.print()}
            title="Print"
            aria-label="Print invoice"
            className="inline-flex items-center gap-1.5 px-2.5 py-2 text-sm font-medium text-[var(--text-secondary)] bg-[var(--bg-surface)] border border-[var(--border-subtle)] rounded-xl hover:bg-[var(--bg-surface-hover)] hover:text-[var(--text-main)] shadow-sm transition"
          >
            <Printer className="w-4 h-4 text-[var(--text-muted)]" />
          </button>

          {/* Edit Button (if unposted draft or open with no payments) */}
          {invoice.status !== 'Void' && invoice.paidAmount === 0 && (
            <Link
              to={`/org/${orgSlug}/sales/${invoice.id}/edit`}
              className="inline-flex items-center gap-1.5 px-3.5 py-2 text-sm font-medium text-[var(--text-secondary)] bg-[var(--bg-surface)] border border-[var(--border-subtle)] rounded-xl hover:bg-[var(--bg-surface-hover)] hover:text-[var(--text-main)] shadow-sm transition"
            >
              <FileEdit className="w-4 h-4 text-[var(--text-muted)]" />
              Edit
            </Link>
          )}

          {/* Issue Invoice Button (if Draft) */}
          {invoice.status === 'Draft' && (
            <button
              type="button"
              onClick={handleIssueInvoice}
              disabled={isSubmittingIssue}
              className="inline-flex items-center gap-1.5 px-4 py-2 text-sm font-semibold text-[var(--brand-text)] bg-[var(--brand-primary)] rounded-xl hover:bg-[var(--brand-hover)] shadow-sm transition disabled:opacity-50"
            >
              {isSubmittingIssue ? (
                <Loader2 className="w-4 h-4 animate-spin" />
              ) : (
                <Send className="w-4 h-4" />
              )}
              Issue Invoice
            </button>
          )}

          {/* Record Payment Button (if balance due and not void) */}
          {invoice.status !== 'Void' && invoice.balanceDue > 0 && (
            <button
              type="button"
              onClick={() => {
                setPaymentAmount(invoice.balanceDue.toFixed(2));
                setPaymentError(null);
                setIsPaymentModalOpen(true);
              }}
              className="inline-flex items-center gap-1.5 px-4 py-2 text-sm font-semibold text-white bg-emerald-600 rounded-xl hover:bg-emerald-700 shadow-sm transition"
            >
              <CreditCard className="w-4 h-4" />
              Record Payment
            </button>
          )}

          {/* Void Button (if not void and no payments) */}
          {invoice.status !== 'Void' && invoice.paidAmount === 0 && (
            <button
              type="button"
              onClick={() => {
                setVoidError(null);
                setIsVoidModalOpen(true);
              }}
              className="inline-flex items-center gap-1.5 px-3.5 py-2 text-sm font-medium text-rose-500 hover:bg-rose-500/10 rounded-xl transition"
            >
              <Ban className="w-4 h-4" />
              Void
            </button>
          )}
        </div>
      </div>

      {/* Notifications */}
      {errorBanner && (
        <div className="no-print mb-6 p-4 bg-rose-500/10 border border-rose-500/20 text-rose-500 rounded-xl flex items-start gap-3">
          <AlertTriangle className="w-5 h-5 shrink-0 mt-0.5" />
          <div className="flex-1 text-sm font-medium">{errorBanner}</div>
          <button
            onClick={() => setErrorBanner(null)}
            className="p-1 hover:opacity-75 transition"
          >
            <X className="w-4 h-4" />
          </button>
        </div>
      )}

      {successBanner && (
        <div className="no-print mb-6 p-4 bg-emerald-500/10 border border-emerald-500/20 text-emerald-500 rounded-xl flex items-center gap-3">
          <CheckCircle2 className="w-5 h-5 shrink-0" />
          <div className="flex-1 text-sm font-medium">{successBanner}</div>
          <button
            onClick={() => setSuccessBanner(null)}
            className="p-1 hover:opacity-75 ml-auto transition"
          >
            <X className="w-4 h-4" />
          </button>
        </div>
      )}

      {/* Main Printable Invoice Sheet */}
      <div className="print-sheet bg-[var(--bg-surface)] border border-[var(--border-subtle)] rounded-2xl shadow-sm p-8 sm:p-12 space-y-10">
        {/* Document Header */}
        <div className="flex flex-col sm:flex-row sm:items-start sm:justify-between gap-6 border-b border-[var(--border-subtle)] pb-8">
          <div>
            <div className="flex items-center gap-2.5 mb-2">
              <div className="w-10 h-10 rounded-xl bg-[var(--brand-primary)] text-[var(--brand-text)] flex items-center justify-center font-bold text-lg shadow-sm">
                {sellerName.substring(0, 1)}
              </div>
              <h2 className="text-2xl font-bold tracking-tight text-[var(--text-main)]">
                {sellerName}
              </h2>
            </div>
            <div className="space-y-0.5 text-xs text-[var(--text-secondary)]">
              {invoice.seller?.addressLines.map((line) => (
                <p key={line}>{line}</p>
              ))}
              {sellerContact && <p>{sellerContact}</p>}
            </div>
            <p className="mt-1 text-xs text-[var(--text-muted)]">
              Tax ID / CRA: {(invoice.seller ? invoice.seller.taxId : currentOrg.cra) || 'N/A'}
            </p>
          </div>

          <div className="text-left sm:text-right">
            <h3 className="text-3xl font-extrabold tracking-tight text-[var(--text-main)] uppercase mb-1">
              INVOICE
            </h3>
            <p className="text-base font-bold font-mono text-[var(--brand-primary)]">
              #{invoice.no || invoice.id}
            </p>
            <div className="mt-2 flex sm:justify-end">{getStatusBadge(invoice.status)}</div>
          </div>
        </div>

        {/* Metadata & Billing Party Info */}
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-8 text-sm">
          {/* Bill To */}
          <div className="space-y-1.5">
            <span className="text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] block mb-1">
              Billed To
            </span>
            <div className="font-bold text-[var(--text-main)] text-base">
              {invoice.customerName}
            </div>
            {showCustomerEmail && invoice.customerEmail && (
              <div className="flex items-center gap-1.5 text-[var(--text-secondary)]">
                <Mail className="w-3.5 h-3.5 text-[var(--text-muted)]" />
                {invoice.customerEmail}
              </div>
            )}
            {invoice.customerPhone && (
              <div className="flex items-center gap-1.5 text-[var(--text-secondary)]">
                <Phone className="w-3.5 h-3.5 text-[var(--text-muted)]" />
                {invoice.customerPhone}
              </div>
            )}
            {invoice.customerAddress && (
              <div className="flex items-start gap-1.5 text-[var(--text-secondary)] pt-1">
                <MapPin className="w-3.5 h-3.5 text-[var(--text-muted)] shrink-0 mt-0.5" />
                <span>{invoice.customerAddress}</span>
              </div>
            )}
          </div>

          {/* Invoice Dates & Reference */}
          <div className="space-y-2.5 sm:text-right">
            <div className="flex justify-between sm:justify-end gap-6">
              <span className="text-[var(--text-muted)]">Invoice Date:</span>
              <span className="font-semibold text-[var(--text-main)]">
                {new Date(invoice.invoiceDate).toLocaleDateString(undefined, {
                  year: 'numeric',
                  month: 'short',
                  day: 'numeric',
                })}
              </span>
            </div>

            {invoice.dueDate && (
              <div className="flex justify-between sm:justify-end gap-6">
                <span className="text-[var(--text-muted)]">Due Date:</span>
                <span
                  className={`font-semibold ${
                    isOverdue
                      ? 'text-rose-500'
                      : 'text-[var(--text-main)]'
                  }`}
                >
                  {new Date(invoice.dueDate).toLocaleDateString(undefined, {
                    year: 'numeric',
                    month: 'short',
                    day: 'numeric',
                  })}
                </span>
              </div>
            )}

            {invoice.referenceNo && (
              <div className="flex justify-between sm:justify-end gap-6">
                <span className="text-[var(--text-muted)]">Reference / PO:</span>
                <span className="font-medium text-[var(--text-main)]">
                  {invoice.referenceNo}
                </span>
              </div>
            )}
          </div>
        </div>

        {/* Line Items Table */}
        <div className="overflow-x-auto rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)]">
          <table className="w-full text-left border-collapse">
            <thead>
              <tr className="bg-[var(--bg-subtle)] border-b border-[var(--border-subtle)] text-xs font-semibold text-[var(--text-muted)] uppercase tracking-wider">
                <th className="py-3 px-3 w-12 print:w-10 text-center">#</th>
                <th className="py-3 px-4">Description</th>
                <th className="py-3 px-3 text-right w-24 print:w-16">Qty</th>
                <th className="py-3 px-3 text-right w-28">Unit Price</th>
                <th className="py-3 px-3 text-right w-24 print:w-20">Disc %</th>
                <th className="py-3 px-4 text-right w-32">Total</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-[var(--border-subtle)] text-sm">
              {invoice.salesInvoiceLines?.map((line, idx) => {
                const qty = line.quantity || 1;
                const price = line.amount || 0;
                const gross = qty * price;
                const disc = (gross * (line.discount || 0)) / 100;
                const net = line.lineTotal ?? Math.max(0, gross - disc);

                return (
                  <tr key={line.id || idx} className="hover:bg-[var(--bg-surface-hover)]/30 transition">
                    <td className="py-3.5 px-3 text-center text-[var(--text-muted)] text-xs">
                      {idx + 1}
                    </td>
                    <td className="py-3.5 px-4 font-medium text-[var(--text-main)]">
                      {line.itemDescription || 'Service / Product'}
                    </td>
                    <td className="py-3.5 px-3 text-right text-[var(--text-secondary)]">
                      {line.quantity}
                    </td>
                    <td className="py-3.5 px-3 text-right font-mono text-[var(--text-secondary)]">
                      {formatCurrency(line.amount || 0)}
                    </td>
                    <td className="py-3.5 px-3 text-right text-[var(--text-secondary)]">
                      {line.discount ? `${line.discount}%` : '—'}
                    </td>
                    <td className="py-3.5 px-4 text-right font-semibold font-mono text-[var(--text-main)]">
                      {formatCurrency(net)}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>

        {/* Totals Summary */}
        <div className="flex flex-col sm:flex-row justify-between gap-8 border-t border-[var(--border-subtle)] pt-6">
          {/* Notes & Payment Instructions */}
          <div className="sm:max-w-md space-y-4 text-xs text-[var(--text-secondary)]">
            {showNotes && invoice.notes && (
              <div>
                <span className="font-semibold uppercase tracking-wider text-[var(--text-muted)] block mb-1">
                  Notes
                </span>
                <p className="whitespace-pre-line">{invoice.notes}</p>
              </div>
            )}

            {showPaymentInstructions && invoice.paymentInstructions && (
              <div>
                <span className="font-semibold uppercase tracking-wider text-[var(--text-muted)] block mb-1">
                  Payment Instructions
                </span>
                <p className="whitespace-pre-line p-3.5 bg-[var(--bg-subtle)] rounded-xl border border-[var(--border-subtle)] text-[var(--text-main)]">
                  {invoice.paymentInstructions}
                </p>
              </div>
            )}
          </div>

          {/* Breakdown Box */}
          <div className="print-avoid-break w-full sm:w-80 space-y-2.5 text-sm bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-2xl p-5">
            <div className="flex justify-between text-[var(--text-secondary)]">
              <span>Subtotal:</span>
              <span className="font-medium font-mono text-[var(--text-main)]">
                {formatCurrency(invoice.amount || 0)}
              </span>
            </div>

            {(invoice.totalTax || 0) > 0 && (
              <div className="flex justify-between text-[var(--text-secondary)]">
                <span>Tax:</span>
                <span className="font-medium font-mono text-[var(--text-main)]">
                  {formatCurrency(invoice.totalTax || 0)}
                </span>
              </div>
            )}

            <div className="flex justify-between font-bold text-base text-[var(--text-main)] border-t border-[var(--border-subtle)] pt-2.5">
              <span>Total Amount:</span>
              <span className="font-mono">{formatCurrency(invoice.totalAmountAfterTax || invoice.amount)}</span>
            </div>

            {invoice.paidAmount > 0 && (
              <div className="flex justify-between text-emerald-500 font-medium">
                <span>Amount Paid:</span>
                <span className="font-mono">-{formatCurrency(invoice.paidAmount)}</span>
              </div>
            )}

            <div className="flex justify-between font-bold text-lg border-t border-[var(--border-subtle)] pt-2.5">
              <span className="text-[var(--text-main)]">Balance Due:</span>
              <span className="font-mono text-[var(--brand-primary)]">{formatCurrency(invoice.balanceDue)}</span>
            </div>
          </div>
        </div>
      </div>

      {/* Payment History Section (Hidden on Print) */}
      <div className="no-print mt-8 bg-[var(--bg-surface)] border border-[var(--border-subtle)] rounded-2xl shadow-sm p-6 sm:p-8">
        <div className="flex items-center justify-between mb-4">
          <div className="flex items-center gap-2">
            <Receipt className="w-5 h-5 text-[var(--brand-primary)]" />
            <h3 className="font-bold text-[var(--text-main)] text-base">
              Payment History
            </h3>
          </div>
          {invoice.paidAmount > 0 && (
            <span className="text-xs font-semibold text-emerald-500 bg-emerald-500/10 px-2.5 py-1 rounded-full border border-emerald-500/20">
              Total Paid: {formatCurrency(invoice.paidAmount)}
            </span>
          )}
        </div>

        {invoice.payments && invoice.payments.length > 0 ? (
          <div className="overflow-x-auto rounded-xl border border-[var(--border-subtle)]">
            <table className="w-full text-left border-collapse">
              <thead>
                <tr className="bg-[var(--bg-subtle)] text-xs font-semibold text-[var(--text-muted)] uppercase tracking-wider border-b border-[var(--border-subtle)]">
                  <th className="py-3 px-4">Payment Date</th>
                  <th className="py-3 px-4">Method</th>
                  <th className="py-3 px-4">Reference #</th>
                  <th className="py-3 px-4">Notes</th>
                  <th className="py-3 px-4 text-right">Amount</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-[var(--border-subtle)] text-sm">
                {invoice.payments.map((p) => (
                  <tr key={p.id} className="hover:bg-[var(--bg-surface-hover)]/30 transition">
                    <td className="py-3 px-4 text-[var(--text-main)]">
                      {new Date(p.date).toLocaleDateString(undefined, {
                        year: 'numeric',
                        month: 'short',
                        day: 'numeric',
                      })}
                    </td>
                    <td className="py-3 px-4 font-medium text-[var(--text-main)]">
                      {p.paymentMethod || 'Payment'}
                    </td>
                    <td className="py-3 px-4 text-[var(--text-muted)] text-xs font-mono">
                      {p.referenceNo || p.receiptNo || '—'}
                    </td>
                    <td className="py-3 px-4 text-[var(--text-secondary)] text-xs">
                      {p.notes || '—'}
                    </td>
                    <td className="py-3 px-4 text-right font-semibold font-mono text-emerald-500">
                      {formatCurrency(p.amount)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <div className="text-center py-6 text-sm text-[var(--text-muted)] bg-[var(--bg-subtle)]/50 rounded-xl border border-dashed border-[var(--border-subtle)]">
            No payments have been recorded for this invoice yet.
          </div>
        )}
      </div>

      {/* Record Payment Modal */}
      {isPaymentModalOpen && (
        <div className="no-print fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/60 backdrop-blur-sm animate-fade-in">
          <div className="bg-[var(--bg-surface)] rounded-2xl max-w-md w-full border border-[var(--border-subtle)] shadow-2xl overflow-hidden">
            <div className="px-6 py-4 border-b border-[var(--border-subtle)] flex items-center justify-between">
              <div className="flex items-center gap-2">
                <CreditCard className="w-5 h-5 text-emerald-500" />
                <h3 className="font-bold text-[var(--text-main)] text-base">
                  Record Payment
                </h3>
              </div>
              <button
                type="button"
                onClick={() => setIsPaymentModalOpen(false)}
                className="text-[var(--text-muted)] hover:text-[var(--text-main)] p-1 rounded-lg transition"
              >
                <X className="w-5 h-5" />
              </button>
            </div>

            <form onSubmit={handleRecordPayment} className="p-6 space-y-4">
              {paymentError && (
                <div className="p-3 bg-rose-500/10 border border-rose-500/20 rounded-xl text-xs text-rose-500">
                  {paymentError}
                </div>
              )}

              <div className="p-3.5 bg-[var(--bg-subtle)] rounded-xl border border-[var(--border-subtle)] text-xs flex justify-between items-center">
                <span className="text-[var(--text-secondary)]">Balance Remaining:</span>
                <span className="font-bold font-mono text-[var(--brand-primary)] text-sm">
                  {formatCurrency(invoice.balanceDue)}
                </span>
              </div>

              <div>
                <label className="block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1">
                  Payment Amount ({currencySymbol}) <span className="text-rose-500">*</span>
                </label>
                <input
                  type="number"
                  step="0.01"
                  min="0.01"
                  max={Number(invoice.balanceDue.toFixed(2))}
                  required
                  value={paymentAmount}
                  onChange={(e) => setPaymentAmount(e.target.value)}
                  className="w-full px-3.5 py-2.5 text-sm bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-xl focus:ring-2 focus:ring-[var(--brand-ring)] focus:outline-none text-[var(--text-main)] font-mono font-bold transition"
                />
              </div>

              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1">
                    Payment Date <span className="text-rose-500">*</span>
                  </label>
                  <input
                    type="date"
                    required
                    value={paymentDate}
                    onChange={(e) => setPaymentDate(e.target.value)}
                    className="w-full px-3 py-2 text-xs bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-xl focus:ring-2 focus:ring-[var(--brand-ring)] focus:outline-none text-[var(--text-main)] transition"
                  />
                </div>

                <div>
                  <label className="block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1">
                    Payment Method
                  </label>
                  <select
                    value={paymentMethod}
                    onChange={(e) => setPaymentMethod(e.target.value)}
                    className="w-full px-3 py-2 text-xs bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-xl focus:ring-2 focus:ring-[var(--brand-ring)] focus:outline-none text-[var(--text-main)] transition"
                  >
                    <option value="Bank Transfer">Bank Wire / Transfer</option>
                    <option value="Credit Card">Credit Card</option>
                    <option value="Debit Card">Debit Card</option>
                    <option value="Cash">Cash</option>
                    <option value="Check">Check</option>
                    <option value="PayPal">PayPal / Stripe</option>
                  </select>
                </div>
              </div>

              <div>
                <label className="block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1">
                  Reference / Transaction #
                </label>
                <input
                  type="text"
                  placeholder="e.g. Wire Ref #TX-98421"
                  value={paymentReference}
                  onChange={(e) => setPaymentReference(e.target.value)}
                  className="w-full px-3.5 py-2.5 text-sm bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-xl focus:ring-2 focus:ring-[var(--brand-ring)] focus:outline-none text-[var(--text-main)] placeholder-[var(--text-muted)] transition"
                />
              </div>

              <div>
                <label className="block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1">
                  Memo / Notes
                </label>
                <input
                  type="text"
                  placeholder="Optional internal memo"
                  value={paymentNotes}
                  onChange={(e) => setPaymentNotes(e.target.value)}
                  className="w-full px-3.5 py-2.5 text-sm bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-xl focus:ring-2 focus:ring-[var(--brand-ring)] focus:outline-none text-[var(--text-main)] placeholder-[var(--text-muted)] transition"
                />
              </div>

              <div className="pt-2 flex items-center justify-end gap-3">
                <button
                  type="button"
                  onClick={() => setIsPaymentModalOpen(false)}
                  className="px-4 py-2.5 text-sm font-medium text-[var(--text-secondary)] bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-xl hover:bg-[var(--bg-surface-hover)] hover:text-[var(--text-main)] transition"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={isSubmittingPayment}
                  className="inline-flex items-center gap-2 px-5 py-2.5 text-sm font-semibold text-white bg-emerald-600 rounded-xl hover:bg-emerald-700 transition disabled:opacity-50 shadow-sm"
                >
                  {isSubmittingPayment && <Loader2 className="w-4 h-4 animate-spin" />}
                  Confirm Payment
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Void Invoice Confirmation Modal */}
      {isVoidModalOpen && (
        <div className="no-print fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/60 backdrop-blur-sm animate-fade-in">
          <div className="bg-[var(--bg-surface)] rounded-2xl max-w-md w-full border border-[var(--border-subtle)] shadow-2xl p-6 space-y-4">
            <div className="flex items-center gap-3 text-rose-500">
              <Ban className="w-6 h-6 shrink-0" />
              <h3 className="font-bold text-lg text-[var(--text-main)]">
                Void Invoice #{invoice.no || invoice.id}?
              </h3>
            </div>

            <p className="text-sm text-[var(--text-secondary)] leading-relaxed">
              Are you sure you want to void this invoice? In accordance with standard accounting
              controls, this will mark the invoice as Void and auto-post a reversing journal entry
              to cancel all receivables and revenues.
            </p>

            {voidError && (
              <div className="p-3 bg-rose-500/10 border border-rose-500/20 rounded-xl text-xs text-rose-500">
                {voidError}
              </div>
            )}

            <div className="pt-2 flex items-center justify-end gap-3">
              <button
                type="button"
                onClick={() => setIsVoidModalOpen(false)}
                className="px-4 py-2.5 text-sm font-medium text-[var(--text-secondary)] bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-xl hover:bg-[var(--bg-surface-hover)] hover:text-[var(--text-main)] transition"
              >
                Cancel
              </button>
              <button
                type="button"
                onClick={handleVoidInvoice}
                disabled={isSubmittingVoid}
                className="inline-flex items-center gap-2 px-5 py-2.5 text-sm font-semibold text-white bg-rose-600 rounded-xl hover:bg-rose-700 transition disabled:opacity-50 shadow-sm"
              >
                {isSubmittingVoid && <Loader2 className="w-4 h-4 animate-spin" />}
                Yes, Void Invoice
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
