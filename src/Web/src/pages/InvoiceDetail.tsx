import React, { useState, useEffect, useMemo, useCallback } from 'react';
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
} from 'lucide-react';
import {
  TenantContextType,
  Invoice,
  InvoiceStatus,
  RecordPaymentPayload,
} from '../types';
import { useAuth } from '../auth/AuthContext';

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
  const currencySymbol = useMemo(() => {
    switch (currentOrg.currency) {
      case 'EUR': return '€';
      case 'GBP': return '£';
      case 'PHP': return '₱';
      case 'CAD': return 'CA$';
      case 'AUD': return 'AU$';
      default: return '$';
    }
  }, [currentOrg.currency]);

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

  // Payment Submission
  const handleRecordPayment = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!invoice) return;

    const amountNum = parseFloat(paymentAmount);
    if (isNaN(amountNum) || amountNum <= 0) {
      setPaymentError('Please enter a valid positive payment amount.');
      return;
    }
    if (amountNum > invoice.balanceDue + 0.001) {
      setPaymentError(
        `Amount cannot exceed the current balance due (${formatCurrency(invoice.balanceDue)}).`
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

  // Status Badge Component
  const getStatusBadge = (status: InvoiceStatus) => {
    switch (status) {
      case 'Paid':
        return (
          <span className="inline-flex items-center gap-1 px-3 py-1 text-xs font-semibold rounded-full bg-emerald-50 text-emerald-700 dark:bg-emerald-950/50 dark:text-emerald-400 border border-emerald-200 dark:border-emerald-800">
            <CheckCircle2 className="w-3.5 h-3.5" /> Paid
          </span>
        );
      case 'Partially Paid':
        return (
          <span className="inline-flex items-center gap-1 px-3 py-1 text-xs font-semibold rounded-full bg-blue-50 text-blue-700 dark:bg-blue-950/50 dark:text-blue-400 border border-blue-200 dark:border-blue-800">
            <Clock className="w-3.5 h-3.5" /> Partially Paid
          </span>
        );
      case 'Overdue':
        return (
          <span className="inline-flex items-center gap-1 px-3 py-1 text-xs font-semibold rounded-full bg-rose-50 text-rose-700 dark:bg-rose-950/50 dark:text-rose-400 border border-rose-200 dark:border-rose-800">
            <AlertTriangle className="w-3.5 h-3.5" /> Overdue
          </span>
        );
      case 'Draft':
        return (
          <span className="inline-flex items-center gap-1 px-3 py-1 text-xs font-semibold rounded-full bg-slate-100 text-slate-700 dark:bg-slate-800 dark:text-slate-300 border border-slate-200 dark:border-slate-700">
            <FileEdit className="w-3.5 h-3.5" /> Draft
          </span>
        );
      case 'Void':
        return (
          <span className="inline-flex items-center gap-1 px-3 py-1 text-xs font-semibold rounded-full bg-slate-100 text-slate-500 dark:bg-slate-800 dark:text-slate-400 border border-slate-200 dark:border-slate-700 line-through">
            <Ban className="w-3.5 h-3.5" /> Void
          </span>
        );
      case 'Open':
      default:
        return (
          <span className="inline-flex items-center gap-1 px-3 py-1 text-xs font-semibold rounded-full bg-amber-50 text-amber-700 dark:bg-amber-950/50 dark:text-amber-400 border border-amber-200 dark:border-amber-800">
            <Clock className="w-3.5 h-3.5" /> Open / Unpaid
          </span>
        );
    }
  };

  if (isLoading) {
    return (
      <div className="flex flex-col items-center justify-center min-h-[50vh] text-slate-400">
        <Loader2 className="w-8 h-8 animate-spin text-blue-600 mb-3" />
        <p className="text-sm">Loading invoice details...</p>
      </div>
    );
  }

  if (!invoice) {
    return (
      <div className="max-w-4xl mx-auto px-4 py-12 text-center">
        <AlertTriangle className="w-12 h-12 text-amber-500 mx-auto mb-3" />
        <h2 className="text-xl font-bold text-slate-900 dark:text-white mb-2">
          Invoice Not Found
        </h2>
        <p className="text-sm text-slate-500 dark:text-slate-400 mb-6">
          The requested invoice #{invoiceId} could not be retrieved.
        </p>
        <Link
          to={`/org/${orgSlug}/sales`}
          className="inline-flex items-center gap-2 px-4 py-2 text-sm font-medium text-white bg-blue-600 rounded-lg hover:bg-blue-700 transition"
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

  return (
    <div className="max-w-5xl mx-auto px-4 sm:px-6 py-6 pb-24 text-slate-900 dark:text-slate-100 print:p-0 print:m-0 print:max-w-none">
      {/* Print Specific CSS */}
      <style>{`
        @media print {
          body {
            background: white !important;
            color: black !important;
          }
          header, nav, aside, .no-print, button, a {
            display: none !important;
          }
          .print-clean {
            box-shadow: none !important;
            border: none !important;
            padding: 0 !important;
            margin: 0 !important;
            width: 100% !important;
          }
        }
      `}</style>

      {/* Top Navigation & Action Bar (Hidden on Print) */}
      <div className="no-print flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 mb-6">
        <div className="flex items-center gap-3">
          <Link
            to={`/org/${orgSlug}/sales`}
            className="p-2 text-slate-500 hover:text-slate-700 dark:text-slate-400 dark:hover:text-slate-200 rounded-lg hover:bg-slate-100 dark:hover:bg-slate-800 transition"
            title="Back to Invoices"
          >
            <ArrowLeft className="w-5 h-5" />
          </Link>
          <div className="flex items-center gap-3">
            <h1 className="text-2xl font-bold tracking-tight text-slate-900 dark:text-white">
              Invoice {invoice.no ? `#${invoice.no}` : `#${invoice.id}`}
            </h1>
            {getStatusBadge(invoice.status)}
          </div>
        </div>

        {/* Action Buttons */}
        <div className="flex items-center flex-wrap gap-2.5">
          {/* Print Button */}
          <button
            type="button"
            onClick={() => window.print()}
            className="inline-flex items-center gap-1.5 px-3.5 py-2 text-sm font-medium text-slate-700 dark:text-slate-300 bg-white dark:bg-slate-800 border border-slate-300 dark:border-slate-700 rounded-lg hover:bg-slate-50 dark:hover:bg-slate-700 shadow-sm transition"
          >
            <Printer className="w-4 h-4 text-slate-500" />
            Print / PDF
          </button>

          {/* Edit Button (if unposted draft or open with no payments) */}
          {invoice.status !== 'Void' && invoice.paidAmount === 0 && (
            <Link
              to={`/org/${orgSlug}/sales/${invoice.id}/edit`}
              className="inline-flex items-center gap-1.5 px-3.5 py-2 text-sm font-medium text-slate-700 dark:text-slate-300 bg-white dark:bg-slate-800 border border-slate-300 dark:border-slate-700 rounded-lg hover:bg-slate-50 dark:hover:bg-slate-700 shadow-sm transition"
            >
              <FileEdit className="w-4 h-4 text-slate-500" />
              Edit
            </Link>
          )}

          {/* Record Payment Button (if balance due and not draft/void) */}
          {invoice.status !== 'Draft' &&
            invoice.status !== 'Void' &&
            invoice.balanceDue > 0 && (
              <button
                type="button"
                onClick={() => {
                  setPaymentAmount(invoice.balanceDue.toFixed(2));
                  setPaymentError(null);
                  setIsPaymentModalOpen(true);
                }}
                className="inline-flex items-center gap-1.5 px-4 py-2 text-sm font-medium text-white bg-emerald-600 rounded-lg hover:bg-emerald-700 shadow-sm transition"
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
              className="inline-flex items-center gap-1.5 px-3.5 py-2 text-sm font-medium text-rose-600 dark:text-rose-400 hover:bg-rose-50 dark:hover:bg-rose-950/30 border border-transparent rounded-lg transition"
            >
              <Ban className="w-4 h-4" />
              Void
            </button>
          )}
        </div>
      </div>

      {/* Notifications */}
      {errorBanner && (
        <div className="no-print mb-6 p-4 bg-red-50 dark:bg-red-950/40 border border-red-200 dark:border-red-900/50 rounded-xl flex items-start gap-3">
          <AlertTriangle className="w-5 h-5 text-red-600 dark:text-red-400 shrink-0 mt-0.5" />
          <div className="flex-1 text-sm text-red-700 dark:text-red-300">{errorBanner}</div>
          <button
            onClick={() => setErrorBanner(null)}
            className="text-red-500 hover:text-red-700 dark:hover:text-red-300"
          >
            <X className="w-4 h-4" />
          </button>
        </div>
      )}

      {successBanner && (
        <div className="no-print mb-6 p-4 bg-emerald-50 dark:bg-emerald-950/40 border border-emerald-200 dark:border-emerald-900/50 rounded-xl flex items-center gap-3">
          <CheckCircle2 className="w-5 h-5 text-emerald-600 dark:text-emerald-400 shrink-0" />
          <div className="flex-1 text-sm text-emerald-700 dark:text-emerald-300">{successBanner}</div>
          <button
            onClick={() => setSuccessBanner(null)}
            className="text-emerald-500 hover:text-emerald-700 dark:hover:text-emerald-300 ml-auto"
          >
            <X className="w-4 h-4" />
          </button>
        </div>
      )}

      {/* Main Printable Invoice Sheet */}
      <div className="print-clean bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl shadow-sm p-8 sm:p-12 space-y-10">
        {/* Document Header */}
        <div className="flex flex-col sm:flex-row sm:items-start sm:justify-between gap-6 border-b border-slate-100 dark:border-slate-800 pb-8">
          <div>
            <div className="flex items-center gap-2.5 mb-2">
              <div className="w-10 h-10 rounded-xl bg-blue-600 text-white flex items-center justify-center font-bold text-lg shadow-sm">
                {currentOrg.name?.substring(0, 1) || 'A'}
              </div>
              <h2 className="text-2xl font-bold tracking-tight text-slate-900 dark:text-white">
                {currentOrg.name || invoice.companyName || 'AccountGo Organization'}
              </h2>
            </div>
            <p className="text-xs text-slate-500 dark:text-slate-400">
              Tax ID / CRA: {currentOrg.cra || 'N/A'}
            </p>
          </div>

          <div className="text-left sm:text-right">
            <h3 className="text-3xl font-extrabold tracking-tight text-slate-900 dark:text-white uppercase mb-1">
              INVOICE
            </h3>
            <p className="text-base font-semibold text-blue-600 dark:text-blue-400">
              #{invoice.no || invoice.id}
            </p>
            <div className="mt-2 flex sm:justify-end">{getStatusBadge(invoice.status)}</div>
          </div>
        </div>

        {/* Metadata & Billing Party Info */}
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-8 text-sm">
          {/* Bill To */}
          <div className="space-y-1.5">
            <span className="text-xs font-semibold uppercase tracking-wider text-slate-400 dark:text-slate-500 block mb-1">
              Billed To
            </span>
            <div className="font-bold text-slate-900 dark:text-white text-base">
              {invoice.customerName}
            </div>
            {invoice.customerEmail && (
              <div className="flex items-center gap-1.5 text-slate-600 dark:text-slate-400">
                <Mail className="w-3.5 h-3.5 text-slate-400" />
                {invoice.customerEmail}
              </div>
            )}
            {invoice.customerPhone && (
              <div className="flex items-center gap-1.5 text-slate-600 dark:text-slate-400">
                <Phone className="w-3.5 h-3.5 text-slate-400" />
                {invoice.customerPhone}
              </div>
            )}
            {invoice.customerAddress && (
              <div className="flex items-start gap-1.5 text-slate-600 dark:text-slate-400 pt-1">
                <MapPin className="w-3.5 h-3.5 text-slate-400 shrink-0 mt-0.5" />
                <span>{invoice.customerAddress}</span>
              </div>
            )}
          </div>

          {/* Invoice Dates & Reference */}
          <div className="space-y-2.5 sm:text-right">
            <div className="flex justify-between sm:justify-end gap-6">
              <span className="text-slate-500 dark:text-slate-400">Invoice Date:</span>
              <span className="font-semibold text-slate-900 dark:text-white">
                {new Date(invoice.invoiceDate).toLocaleDateString(undefined, {
                  year: 'numeric',
                  month: 'short',
                  day: 'numeric',
                })}
              </span>
            </div>

            {invoice.dueDate && (
              <div className="flex justify-between sm:justify-end gap-6">
                <span className="text-slate-500 dark:text-slate-400">Due Date:</span>
                <span
                  className={`font-semibold ${
                    isOverdue
                      ? 'text-rose-600 dark:text-rose-400'
                      : 'text-slate-900 dark:text-white'
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
                <span className="text-slate-500 dark:text-slate-400">Reference / PO:</span>
                <span className="font-medium text-slate-900 dark:text-white">
                  {invoice.referenceNo}
                </span>
              </div>
            )}
          </div>
        </div>

        {/* Line Items Table */}
        <div className="overflow-x-auto">
          <table className="w-full text-left border-collapse">
            <thead>
              <tr className="border-b-2 border-slate-200 dark:border-slate-700 text-xs font-semibold text-slate-600 dark:text-slate-300 uppercase tracking-wider">
                <th className="py-3 px-2 w-12 text-center">#</th>
                <th className="py-3 px-4">Description</th>
                <th className="py-3 px-3 text-right w-24">Qty</th>
                <th className="py-3 px-3 text-right w-28">Unit Price</th>
                <th className="py-3 px-3 text-right w-24">Disc %</th>
                <th className="py-3 px-4 text-right w-32">Total</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100 dark:divide-slate-800 text-sm">
              {invoice.salesInvoiceLines?.map((line, idx) => {
                const qty = line.quantity || 1;
                const price = line.amount || 0;
                const gross = qty * price;
                const disc = (gross * (line.discount || 0)) / 100;
                const net = line.lineTotal ?? Math.max(0, gross - disc);

                return (
                  <tr key={line.id || idx}>
                    <td className="py-3.5 px-2 text-center text-slate-400 text-xs">
                      {idx + 1}
                    </td>
                    <td className="py-3.5 px-4 font-medium text-slate-900 dark:text-slate-100">
                      {line.itemDescription || 'Service / Product'}
                    </td>
                    <td className="py-3.5 px-3 text-right text-slate-600 dark:text-slate-300">
                      {line.quantity}
                    </td>
                    <td className="py-3.5 px-3 text-right text-slate-600 dark:text-slate-300">
                      {formatCurrency(line.amount || 0)}
                    </td>
                    <td className="py-3.5 px-3 text-right text-slate-600 dark:text-slate-300">
                      {line.discount ? `${line.discount}%` : '—'}
                    </td>
                    <td className="py-3.5 px-4 text-right font-semibold text-slate-900 dark:text-white">
                      {formatCurrency(net)}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>

        {/* Totals Summary */}
        <div className="flex flex-col sm:flex-row justify-between gap-8 border-t border-slate-100 dark:border-slate-800 pt-6">
          {/* Notes & Payment Instructions */}
          <div className="sm:max-w-md space-y-4 text-xs text-slate-600 dark:text-slate-400">
            {invoice.notes && (
              <div>
                <span className="font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400 block mb-1">
                  Notes
                </span>
                <p className="whitespace-pre-line">{invoice.notes}</p>
              </div>
            )}

            {invoice.paymentInstructions && (
              <div>
                <span className="font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400 block mb-1">
                  Payment Instructions
                </span>
                <p className="whitespace-pre-line p-3 bg-slate-50 dark:bg-slate-800/60 rounded-xl border border-slate-200/60 dark:border-slate-700/60">
                  {invoice.paymentInstructions}
                </p>
              </div>
            )}
          </div>

          {/* Breakdown Box */}
          <div className="w-full sm:w-72 space-y-2.5 text-sm">
            <div className="flex justify-between text-slate-600 dark:text-slate-400">
              <span>Subtotal:</span>
              <span className="font-medium text-slate-900 dark:text-slate-200">
                {formatCurrency(invoice.amount || 0)}
              </span>
            </div>

            {(invoice.totalTax || 0) > 0 && (
              <div className="flex justify-between text-slate-600 dark:text-slate-400">
                <span>Tax:</span>
                <span className="font-medium text-slate-900 dark:text-slate-200">
                  {formatCurrency(invoice.totalTax || 0)}
                </span>
              </div>
            )}

            <div className="flex justify-between font-bold text-base text-slate-900 dark:text-white border-t border-slate-200 dark:border-slate-700 pt-2">
              <span>Total Amount:</span>
              <span>{formatCurrency(invoice.totalAmountAfterTax || invoice.amount)}</span>
            </div>

            {invoice.paidAmount > 0 && (
              <div className="flex justify-between text-emerald-600 dark:text-emerald-400 font-medium">
                <span>Amount Paid:</span>
                <span>-{formatCurrency(invoice.paidAmount)}</span>
              </div>
            )}

            <div className="flex justify-between font-extrabold text-lg text-blue-600 dark:text-blue-400 border-t-2 border-slate-300 dark:border-slate-700 pt-2">
              <span>Balance Due:</span>
              <span>{formatCurrency(invoice.balanceDue)}</span>
            </div>
          </div>
        </div>
      </div>

      {/* Payment History Section (Hidden on Print) */}
      <div className="no-print mt-8 bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl shadow-sm p-6 sm:p-8">
        <div className="flex items-center justify-between mb-4">
          <div className="flex items-center gap-2">
            <Receipt className="w-5 h-5 text-blue-600 dark:text-blue-400" />
            <h3 className="font-bold text-slate-900 dark:text-white text-base">
              Payment History
            </h3>
          </div>
          {invoice.paidAmount > 0 && (
            <span className="text-xs font-semibold text-emerald-600 dark:text-emerald-400 bg-emerald-50 dark:bg-emerald-950/40 px-2.5 py-1 rounded-full border border-emerald-200/50">
              Total Paid: {formatCurrency(invoice.paidAmount)}
            </span>
          )}
        </div>

        {invoice.payments && invoice.payments.length > 0 ? (
          <div className="overflow-x-auto">
            <table className="w-full text-left border-collapse">
              <thead>
                <tr className="bg-slate-50 dark:bg-slate-800/60 text-xs font-semibold text-slate-600 dark:text-slate-300 uppercase tracking-wider border-b border-slate-200 dark:border-slate-800">
                  <th className="py-3 px-4">Payment Date</th>
                  <th className="py-3 px-4">Method</th>
                  <th className="py-3 px-4">Reference #</th>
                  <th className="py-3 px-4">Notes</th>
                  <th className="py-3 px-4 text-right">Amount</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100 dark:divide-slate-800 text-sm">
                {invoice.payments.map((p) => (
                  <tr key={p.id}>
                    <td className="py-3 px-4 text-slate-800 dark:text-slate-200">
                      {new Date(p.date).toLocaleDateString(undefined, {
                        year: 'numeric',
                        month: 'short',
                        day: 'numeric',
                      })}
                    </td>
                    <td className="py-3 px-4 font-medium text-slate-700 dark:text-slate-300">
                      {p.paymentMethod || 'Payment'}
                    </td>
                    <td className="py-3 px-4 text-slate-500 dark:text-slate-400 text-xs">
                      {p.referenceNo || p.receiptNo || '—'}
                    </td>
                    <td className="py-3 px-4 text-slate-500 dark:text-slate-400 text-xs">
                      {p.notes || '—'}
                    </td>
                    <td className="py-3 px-4 text-right font-semibold text-emerald-600 dark:text-emerald-400">
                      {formatCurrency(p.amount)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <div className="text-center py-6 text-sm text-slate-500 dark:text-slate-400">
            No payments have been recorded for this invoice yet.
          </div>
        )}
      </div>

      {/* Record Payment Modal */}
      {isPaymentModalOpen && (
        <div className="no-print fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/50 backdrop-blur-sm animate-fade-in">
          <div className="bg-white dark:bg-slate-900 rounded-2xl max-w-md w-full border border-slate-200 dark:border-slate-800 shadow-2xl overflow-hidden">
            <div className="px-6 py-4 border-b border-slate-100 dark:border-slate-800 flex items-center justify-between">
              <div className="flex items-center gap-2">
                <CreditCard className="w-5 h-5 text-emerald-600 dark:text-emerald-400" />
                <h3 className="font-bold text-slate-900 dark:text-white text-base">
                  Record Payment
                </h3>
              </div>
              <button
                type="button"
                onClick={() => setIsPaymentModalOpen(false)}
                className="text-slate-400 hover:text-slate-600 dark:hover:text-slate-200"
              >
                <X className="w-5 h-5" />
              </button>
            </div>

            <form onSubmit={handleRecordPayment} className="p-6 space-y-4">
              {paymentError && (
                <div className="p-3 bg-red-50 dark:bg-red-950/40 border border-red-200 dark:border-red-900/50 rounded-xl text-xs text-red-600 dark:text-red-400">
                  {paymentError}
                </div>
              )}

              <div className="p-3.5 bg-slate-50 dark:bg-slate-800/60 rounded-xl border border-slate-200/60 dark:border-slate-700/60 text-xs flex justify-between items-center">
                <span className="text-slate-500 dark:text-slate-400">Balance Remaining:</span>
                <span className="font-bold text-blue-600 dark:text-blue-400 text-sm">
                  {formatCurrency(invoice.balanceDue)}
                </span>
              </div>

              <div>
                <label className="block text-xs font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400 mb-1">
                  Payment Amount ({currencySymbol}) <span className="text-red-500">*</span>
                </label>
                <input
                  type="number"
                  step="0.01"
                  min="0.01"
                  max={invoice.balanceDue}
                  required
                  value={paymentAmount}
                  onChange={(e) => setPaymentAmount(e.target.value)}
                  className="w-full px-3.5 py-2 text-sm bg-slate-50 dark:bg-slate-800 border border-slate-300 dark:border-slate-700 rounded-xl focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 text-slate-900 dark:text-slate-100 font-bold"
                />
              </div>

              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="block text-xs font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400 mb-1">
                    Payment Date <span className="text-red-500">*</span>
                  </label>
                  <input
                    type="date"
                    required
                    value={paymentDate}
                    onChange={(e) => setPaymentDate(e.target.value)}
                    className="w-full px-3 py-2 text-xs bg-slate-50 dark:bg-slate-800 border border-slate-300 dark:border-slate-700 rounded-xl focus:ring-2 focus:ring-emerald-500 text-slate-900 dark:text-slate-100"
                  />
                </div>

                <div>
                  <label className="block text-xs font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400 mb-1">
                    Payment Method
                  </label>
                  <select
                    value={paymentMethod}
                    onChange={(e) => setPaymentMethod(e.target.value)}
                    className="w-full px-3 py-2 text-xs bg-slate-50 dark:bg-slate-800 border border-slate-300 dark:border-slate-700 rounded-xl focus:ring-2 focus:ring-emerald-500 text-slate-900 dark:text-slate-100"
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
                <label className="block text-xs font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400 mb-1">
                  Reference / Transaction #
                </label>
                <input
                  type="text"
                  placeholder="e.g. Wire Ref #TX-98421"
                  value={paymentReference}
                  onChange={(e) => setPaymentReference(e.target.value)}
                  className="w-full px-3.5 py-2 text-sm bg-slate-50 dark:bg-slate-800 border border-slate-300 dark:border-slate-700 rounded-xl focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 text-slate-900 dark:text-slate-100"
                />
              </div>

              <div>
                <label className="block text-xs font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400 mb-1">
                  Memo / Notes
                </label>
                <input
                  type="text"
                  placeholder="Optional internal memo"
                  value={paymentNotes}
                  onChange={(e) => setPaymentNotes(e.target.value)}
                  className="w-full px-3.5 py-2 text-sm bg-slate-50 dark:bg-slate-800 border border-slate-300 dark:border-slate-700 rounded-xl focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 text-slate-900 dark:text-slate-100"
                />
              </div>

              <div className="pt-2 flex items-center justify-end gap-3">
                <button
                  type="button"
                  onClick={() => setIsPaymentModalOpen(false)}
                  className="px-4 py-2 text-sm font-medium text-slate-700 dark:text-slate-300 bg-slate-100 dark:bg-slate-800 rounded-xl hover:bg-slate-200 dark:hover:bg-slate-700 transition"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={isSubmittingPayment}
                  className="inline-flex items-center gap-2 px-5 py-2 text-sm font-medium text-white bg-emerald-600 rounded-xl hover:bg-emerald-700 transition disabled:opacity-50"
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
        <div className="no-print fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/50 backdrop-blur-sm animate-fade-in">
          <div className="bg-white dark:bg-slate-900 rounded-2xl max-w-md w-full border border-slate-200 dark:border-slate-800 shadow-2xl p-6 space-y-4">
            <div className="flex items-center gap-3 text-rose-600 dark:text-rose-400">
              <Ban className="w-6 h-6" />
              <h3 className="font-bold text-lg text-slate-900 dark:text-white">
                Void Invoice #{invoice.no || invoice.id}?
              </h3>
            </div>

            <p className="text-sm text-slate-600 dark:text-slate-400 leading-relaxed">
              Are you sure you want to void this invoice? In accordance with standard accounting
              controls, this will mark the invoice as Void and auto-post a reversing journal entry
              to cancel all receivables and revenues.
            </p>

            {voidError && (
              <div className="p-3 bg-red-50 dark:bg-red-950/40 border border-red-200 dark:border-red-900/50 rounded-xl text-xs text-red-600 dark:text-red-400">
                {voidError}
              </div>
            )}

            <div className="pt-2 flex items-center justify-end gap-3">
              <button
                type="button"
                onClick={() => setIsVoidModalOpen(false)}
                className="px-4 py-2 text-sm font-medium text-slate-700 dark:text-slate-300 bg-slate-100 dark:bg-slate-800 rounded-xl hover:bg-slate-200 dark:hover:bg-slate-700 transition"
              >
                Cancel
              </button>
              <button
                type="button"
                onClick={handleVoidInvoice}
                disabled={isSubmittingVoid}
                className="inline-flex items-center gap-2 px-5 py-2 text-sm font-medium text-white bg-rose-600 rounded-xl hover:bg-rose-700 transition disabled:opacity-50"
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
