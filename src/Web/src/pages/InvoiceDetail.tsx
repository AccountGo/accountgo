import React, { useState, useEffect, useCallback, useRef } from 'react';
import { useOutletContext, useParams, useLocation, useNavigate, Link } from 'react-router-dom';
import {
  ArrowLeft,
  Printer,
  CreditCard,
  FileEdit,
  Copy,
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
  Trash2,
} from 'lucide-react';
import {
  TenantContextType,
  Invoice,
  InvoiceStatus,
  RecordPaymentPayload,
} from '../types';
import { useAuth } from '../auth/AuthContext';
import { currencySymbol as getCurrencySymbol } from '../lib/currency';
import { SendInvoiceDialog } from '../components/SendInvoiceDialog';
import { ModalShell } from '../components/ModalShell';
import { VoidDialog } from '../components/VoidDialog';
import { invoiceTitle } from '../lib/invoiceNumber';
import { localToday } from '../lib/businessDays';
import { pickDefaultReceiptBankId } from '../lib/bankingForm';
import type { BankAccountPickerItem } from '../types/banking';
import { PageHeader, HeaderButton, HeaderMenu, StatusPill } from '../components/PageHeader';
import { ReadOnlyModuleBanner, usePermissions } from '../components/Gating';
import { useCapabilities } from '../lib/entitlements';

const formatDateTime = (iso: string) =>
  new Date(iso).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' });

export const InvoiceDetail: React.FC = () => {
  const { currentOrg, canWrite } = useOutletContext<TenantContextType>();
  const { hasPermission } = usePermissions();
  const { canCreate: canCreateResource } = useCapabilities();
  const salesWritable = canWrite('sales');
  const { authFetch, user } = useAuth();
  const { orgSlug, invoiceId } = useParams<{ orgSlug: string; invoiceId: string }>();
  const location = useLocation();
  const navigate = useNavigate();

  // State
  const [invoice, setInvoice] = useState<Invoice | null>(null);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [errorBanner, setErrorBanner] = useState<string | null>(null);
  const [successBanner, setSuccessBanner] = useState<string | null>(null);
  const [stockWarnings, setStockWarnings] = useState<string[]>([]);
  // Why an issued invoice couldn't be posted to the ledger, from the save or issue that just happened.
  const [postingError, setPostingError] = useState<string | null>(null);

  useEffect(() => {
    setStockWarnings([]);
    setPostingError(null);
  }, [invoiceId]);

  // Warnings and posting errors handed over by the invoice editor are shown once: clearing them from history keeps a
  // refresh from bringing them back.
  useEffect(() => {
    const warnings = location.state?.stockWarnings;
    const handedPostingError = location.state?.postingError;
    if (!Array.isArray(warnings) && typeof handedPostingError !== 'string') return;
    if (Array.isArray(warnings))
      setStockWarnings(warnings.filter((warning: unknown): warning is string => typeof warning === 'string'));
    if (typeof handedPostingError === 'string')
      setPostingError(handedPostingError);
    navigate(`${location.pathname}${location.search}`, { replace: true, state: null });
  }, [location.state, location.pathname, location.search, navigate]);

  // Payment Modal State
  const [isPaymentModalOpen, setIsPaymentModalOpen] = useState<boolean>(false);
  const [paymentAmount, setPaymentAmount] = useState<string>('');
  const [paymentDate, setPaymentDate] = useState<string>(localToday);
  const [paymentMethod, setPaymentMethod] = useState<string>('Bank Transfer');
  const [paymentReference, setPaymentReference] = useState<string>('');
  const [paymentNotes, setPaymentNotes] = useState<string>('');
  const [depositBanks, setDepositBanks] = useState<BankAccountPickerItem[] | null>(null);
  const [depositBankId, setDepositBankId] = useState<number | ''>('');
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
  const fetchInvoicePdf = async (id: number, no: string | null) => {
    const res = await authFetch(`/api/Sales/SalesInvoicePdf?id=${id}`);
    if (!res.ok) {
      throw new Error(`Could not generate the PDF (${res.status})`);
    }
    const blob = await res.blob();
    const fileName =
      res.headers.get('Content-Disposition')?.match(/filename="?([^";]+)"?/)?.[1] ||
      `Invoice-${no || 'Draft'}.pdf`;
    return { blob, fileName };
  };

  const handleDownloadPdf = async () => {
    if (!invoice) return;
    setIsDownloadingPdf(true);
    setErrorBanner(null);
    try {
      const { blob, fileName } = await fetchInvoicePdf(invoice.id, invoice.no);
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

  // Print sends the same PDF the customer receives to the printer, so what prints is exactly the download.
  // Desktop Chromium prints it from a hidden frame; other browsers can't print a PDF that way, so they open
  // it in a tab to print from the browser's PDF viewer.
  const printFrameRef = useRef<HTMLIFrameElement | null>(null);
  const [isPrinting, setIsPrinting] = useState<boolean>(false);
  const removePrintFrame = () => {
    const frame = printFrameRef.current;
    if (!frame) return;
    URL.revokeObjectURL(frame.src);
    frame.remove();
    printFrameRef.current = null;
  };
  useEffect(() => removePrintFrame, []);
  const handlePrint = async () => {
    if (!invoice || isPrinting) return;
    const ua = navigator.userAgent;
    const framePrint = /Chrome\//.test(ua) && !/Android|Mobile|CriOS/.test(ua);
    // Opened before the fetch, while the click still counts as a user gesture, so it isn't blocked as a popup.
    const tab = framePrint ? null : window.open('', '_blank');
    setIsPrinting(true);
    setErrorBanner(null);
    try {
      const { blob } = await fetchInvoicePdf(invoice.id, invoice.no);
      const url = URL.createObjectURL(blob);
      // Safari/Firefox: prefer a tab opened under the user gesture. If the popup was blocked
      // (tab is null), fall through to the iframe path and tell the user if that can't print either.
      if (tab) {
        tab.location.href = url;
        setTimeout(() => URL.revokeObjectURL(url), 60_000);
        return;
      }
      if (!framePrint) {
        // Popup was blocked; iframe PDF printing is unreliable outside Chromium.
        setErrorBanner('Allow pop-ups to print this invoice, or use Download PDF.');
      }
      removePrintFrame();
      const frame = document.createElement('iframe');
      frame.setAttribute('aria-hidden', 'true');
      frame.style.cssText = 'position:fixed;right:0;bottom:0;width:0;height:0;border:0;visibility:hidden';
      // Chrome fires onload before the PDF plugin is ready (blank page). Wait, then print once.
      frame.onload = () => {
        window.setTimeout(() => {
          try {
            frame.contentWindow?.focus();
            frame.contentWindow?.print();
          } catch {
            setErrorBanner('Could not open the print dialog. Try Download PDF, then print from your PDF viewer.');
          }
        }, 500);
      };
      frame.src = url;
      document.body.appendChild(frame);
      printFrameRef.current = frame;
    } catch (err: any) {
      tab?.close();
      setErrorBanner(err.message || 'Could not prepare the invoice for printing.');
    } finally {
      setIsPrinting(false);
    }
  };

  useEffect(() => {
    if (!isPaymentModalOpen) return;
    let cancelled = false;
    (async () => {
      const res = await authFetch('/api/Banking/Accounts/Picker');
      if (cancelled) return;
      const list: BankAccountPickerItem[] = res.ok ? await res.json() : [];
      setDepositBanks(list);
      const preferred = pickDefaultReceiptBankId(list);
      setDepositBankId(preferred ?? '');
    })();
    return () => {
      cancelled = true;
    };
  }, [isPaymentModalOpen, authFetch]);

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
        bankAccountId: depositBankId === '' ? undefined : Number(depositBankId),
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
  const handleVoidInvoice = async (date: string) => {
    if (!invoice) return;
    setIsSubmittingVoid(true);
    setVoidError(null);

    try {
      const res = await authFetch(`/api/Sales/VoidSalesInvoice?id=${invoice.id}&date=${encodeURIComponent(date)}`, {
        method: 'POST',
      });

      if (!res.ok) {
        const errorData = await res.json().catch(() => null);
        throw new Error(errorData?.message || 'Failed to void invoice.');
      }

      setSuccessBanner(`${invoiceTitle(invoice)} has been voided.`);
      setIsVoidModalOpen(false);
      await fetchInvoice();
    } catch (err: any) {
      setVoidError(err.message || 'An error occurred while voiding the invoice.');
    } finally {
      setIsSubmittingVoid(false);
    }
  };

  // Delete Draft State
  const [isDeleteModalOpen, setIsDeleteModalOpen] = useState<boolean>(false);
  const [isSubmittingDelete, setIsSubmittingDelete] = useState<boolean>(false);
  const [deleteError, setDeleteError] = useState<string | null>(null);

  const handleDeleteDraft = async () => {
    if (!invoice) return;
    setIsSubmittingDelete(true);
    setDeleteError(null);

    try {
      const res = await authFetch(`/api/Sales/DeleteSalesInvoice?id=${invoice.id}`, {
        method: 'DELETE',
      });

      if (!res.ok) {
        const errorData = await res.json().catch(() => null);
        throw new Error(errorData?.message || errorData || 'Failed to delete draft invoice.');
      }

      navigate(`/org/${orgSlug}/sales`);
    } catch (err: any) {
      setDeleteError(err.message || 'An error occurred while deleting the draft invoice.');
    } finally {
      setIsSubmittingDelete(false);
    }
  };

  // Issue Draft Invoice, optionally emailing it (and emailing issued invoices again later)
  const [isSubmittingIssue, setIsSubmittingIssue] = useState<boolean>(false);
  const [sendDialogMode, setSendDialogMode] = useState<'issue' | 'send' | null>(null);
  const [isSendingEmail, setIsSendingEmail] = useState<boolean>(false);
  const [sendError, setSendError] = useState<string | null>(null);
  // The number an unnumbered draft takes when issued, as shown in the issue dialog.
  const [issueNumber, setIssueNumber] = useState<string | null>(null);

  const fetchNextNumber = async (): Promise<string | null> => {
    const res = await authFetch('/api/Sales/NextSalesInvoiceNumber');
    if (!res.ok) return null;
    const data = await res.json().catch(() => null);
    return data?.next ?? null;
  };

  /** Issues the draft and returns the number it was given. */
  const issueInvoice = async (): Promise<string> => {
    if (!invoice) return '';
    // The dialog asked the user to confirm a number; the server refuses if another invoice took it meanwhile.
    const expected = sendDialogMode === 'issue' && !invoice.no && issueNumber ? `&expectedNo=${encodeURIComponent(issueNumber)}` : '';
    const res = await authFetch(`/api/Sales/IssueSalesInvoice?id=${invoice.id}${expected}`, {
      method: 'POST',
    });

    const data = await res.json().catch(() => null);
    if (!res.ok) {
      if (data?.code === 'number_changed' && data?.next) setIssueNumber(data.next);
      throw new Error(data?.message || 'Failed to issue invoice.');
    }
    setStockWarnings(Array.isArray(data?.warnings) ? data.warnings.filter((warning: unknown): warning is string => typeof warning === 'string') : []);
    setPostingError(typeof data?.postingError === 'string' ? data.postingError : null);
    return data?.no || invoice.no || '';
  };

  const emailInvoice = async (
    followUp = false,
    attachPdf = false,
    additionalRecipients: string[] = [],
    cc: string[] = [],
  ): Promise<string> => {
    if (!invoice) return '';
    const attach = attachPdf ? '&attachPdf=true' : '';
    const query = (name: string, values: string[]) =>
      values
        .filter((r) => r.trim().length > 0)
        .map((r) => `&${name}=${encodeURIComponent(r.trim())}`)
        .join('');
    const extras = query('additionalRecipients', additionalRecipients) + query('cc', cc);
    const path = followUp
      ? `/api/Sales/SendSalesInvoiceFollowUpEmail?id=${invoice.id}${attach}${extras}`
      : `/api/Sales/SendSalesInvoiceEmail?id=${invoice.id}${attach}${extras}`;
    const res = await authFetch(path, {
      method: 'POST',
    });
    const data = await res.json().catch(() => null);
    if (!res.ok) {
      throw new Error(data?.message || 'The email could not be sent.');
    }
    return data?.to || invoice.recipientEmail || '';
  };

  const handleIssueClick = async () => {
    if (!invoice) return;
    if (invoice.recipientEmail) {
      setSendError(null);
      const next = invoice.no ? null : await fetchNextNumber();
      if (!invoice.no && !next) {
        setErrorBanner("Couldn't get the next invoice number. Please try again.");
        return;
      }
      setIssueNumber(next);
      setSendDialogMode('issue');
      return;
    }
    void handleIssueOnly();
  };

  const handleIssueOnly = async () => {
    if (!invoice) return;
    setIsSubmittingIssue(true);
    setErrorBanner(null);

    try {
      const no = await issueInvoice();
      setSendDialogMode(null);
      setSuccessBanner(`Invoice #${no} has been issued.`);
      await fetchInvoice();
    } catch (err: any) {
      if (sendDialogMode) setSendError(err.message || 'An error occurred while issuing the invoice.');
      else setErrorBanner(err.message || 'An error occurred while issuing the invoice.');
    } finally {
      setIsSubmittingIssue(false);
    }
  };

  const handleSendEmail = async (
    followUp: boolean,
    attachPdf = false,
    additionalRecipients: string[] = [],
    cc: string[] = [],
  ) => {
    if (!invoice || !sendDialogMode) return;
    const issuing = sendDialogMode === 'issue';
    // Issuing always sends the first invoice email, never a follow-up.
    const sendAsFollowUp = !issuing && followUp;
    let label = invoiceTitle(invoice);
    setIsSendingEmail(true);
    setSendError(null);
    setErrorBanner(null);

    try {
      if (issuing) label = `Invoice #${await issueInvoice()}`;
    } catch (err: any) {
      // Nothing happened yet; keep the dialog open so the user can retry or cancel.
      setSendError(err.message || 'An error occurred while issuing the invoice.');
      setIsSendingEmail(false);
      return;
    }

    try {
      const to = await emailInvoice(sendAsFollowUp, attachPdf, additionalRecipients, cc);
      const others = additionalRecipients.length + cc.length;
      const recipientNotice =
        others > 0
          ? `${to} and ${others} other recipient${others > 1 ? 's' : ''}`
          : to;

      setSuccessBanner(
        issuing
          ? `${label} has been issued and emailed to ${recipientNotice}.`
          : sendAsFollowUp
            ? `Follow-up for ${label} has been emailed to ${recipientNotice}.`
            : `${label} has been emailed to ${recipientNotice}.`,
      );
      setSendDialogMode(null);
      if (!issuing) await fetchInvoice();
    } catch (err: any) {
      if (issuing) {
        // The invoice is issued either way; the email can be sent again from the invoice.
        setSendDialogMode(null);
        setErrorBanner(`${label} was issued, but the email was not sent: ${err.message} Use "Email invoice" to try again.`);
      } else {
        setSendError(err.message);
      }
    } finally {
      setIsSendingEmail(false);
      if (issuing) await fetchInvoice();
    }
  };

  // Status Badge Component
  const getStatusBadge = (status: InvoiceStatus) => {
    switch (status) {
      case 'Paid':
        return <StatusPill tone="green" icon={CheckCircle2}>Paid</StatusPill>;
      case 'Partially Paid':
        return <StatusPill tone="amber" icon={Clock}>Partially paid</StatusPill>;
      case 'Overdue':
        return <StatusPill tone="red" icon={AlertTriangle}>Overdue</StatusPill>;
      case 'Draft':
        return <StatusPill tone="slate" icon={FileEdit}>Draft</StatusPill>;
      case 'Void':
        return <StatusPill tone="slate" icon={Ban} strike>Void</StatusPill>;
      case 'Open':
      default:
        return <StatusPill tone="blue" icon={Clock}>Open</StatusPill>;
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
  const sellerContact = [invoice.seller?.phone, invoice.seller?.email].filter(Boolean).join(' · ');
  const showCustomerEmail = invoice.display?.showCustomerEmail ?? true;
  const showAttention = invoice.display?.showAttention ?? true;
  const showNotes = invoice.display?.showNotes ?? true;
  const showPaymentInstructions = invoice.display?.showPaymentInstructions ?? true;
  const lastEmail = invoice.emails?.[0] ?? null;
  // Edit and void are only offered before any payment; drafts are issued before taking payments or emails. Each
  // action also needs its permission: drafting, issuing (which voiding undoes), or recording receipts.
  // Duplicate also needs remaining quota; edit/issue/void/pay need canWrite(sales).
  const canCreate = hasPermission('sales.invoices.create') && salesWritable && canCreateResource('salesDocuments');
  const canIssue = hasPermission('sales.invoices.post') && salesWritable;
  const canEdit = hasPermission('sales.invoices.create') && salesWritable && invoice.status === 'Draft';
  const canDelete = hasPermission('sales.invoices.create') && salesWritable && invoice.status === 'Draft';
  const canVoid = canIssue && invoice.status !== 'Draft' && invoice.status !== 'Void' && invoice.status !== 'Paid' && invoice.paidAmount === 0 && invoice.balanceDue > 0;
  const canEmail = hasPermission('sales.invoices.create') && salesWritable && invoice.status !== 'Draft' && invoice.status !== 'Void' && invoice.status !== 'Paid' && invoice.balanceDue > 0 && !!invoice.recipientEmail;
  // Follow-up copy for unpaid issued invoices (Open / Overdue / Partially Paid). API enforces the same.
  const followUpEligible = canEmail && invoice.balanceDue > 0;
  const canRecordPayment =
    hasPermission('sales.receipts.create') && salesWritable && invoice.status !== 'Void' && invoice.status !== 'Draft' && invoice.balanceDue > 0;
  const dueOn = invoice.dueDate ? new Date(invoice.dueDate).toLocaleDateString(undefined, { dateStyle: 'medium' }) : null;
  const balanceSummary =
    invoice.status === 'Void'
      ? null
      : invoice.balanceDue > 0
        ? `${formatCurrency(invoice.balanceDue)} ${isOverdue && dueOn ? `overdue since ${dueOn}` : dueOn ? `due ${dueOn}` : 'due'}`
        : invoice.status === 'Paid'
          ? 'Paid in full'
          : null;

  // Like the PDF, the tax column shows only when a line has tax.
  const showTax = invoice.salesInvoiceLines?.some((line) => (line.taxAmount ?? 0) !== 0) ?? false;

  return (
    <div className="print-page max-w-5xl mx-auto px-4 sm:px-6 py-6 pb-24 text-[var(--text-main)]">
      <div className="no-print mb-4">
        <ReadOnlyModuleBanner module="sales" orgSlug={orgSlug} />
      </div>

      {/* Header & actions (hidden on print) */}
      <PageHeader
        className="no-print mb-6"
        back={{ to: `/org/${orgSlug}/sales`, label: 'Back to invoices' }}
        title={invoiceTitle(invoice)}
        badges={
          <>
            {getStatusBadge(invoice.status)}
            {lastEmail && (
              <StatusPill tone="sky" icon={Mail} title={`Emailed to ${lastEmail.to} on ${formatDateTime(lastEmail.sentAt)}`}>
                Emailed
              </StatusPill>
            )}
          </>
        }
        subtitle={[invoice.customerName, balanceSummary].filter(Boolean).join(' · ')}
        actions={
          <>
            {canEmail && (
              <HeaderButton
                label="Email"
                icon={Mail}
                compact
                onClick={() => {
                  setSendError(null);
                  setSendDialogMode('send');
                }}
              />
            )}
            <HeaderButton label="Download PDF" icon={FileDown} compact busy={isDownloadingPdf} onClick={handleDownloadPdf} />
            <HeaderMenu
              items={[
                // Opens a new invoice prefilled from this one; nothing is saved until the user saves it.
                canCreate && { label: 'Duplicate invoice', icon: Copy, to: `/org/${orgSlug}/sales/new?from=${invoice.id}` },
                canEdit && { label: 'Edit invoice', icon: FileEdit, to: `/org/${orgSlug}/sales/${invoice.id}/edit` },
                { label: isPrinting ? 'Preparing print…' : 'Print', icon: Printer, onSelect: () => void handlePrint() },
                canDelete && 'separator',
                canDelete && {
                  label: 'Delete draft',
                  icon: Trash2,
                  danger: true,
                  onSelect: () => {
                    setDeleteError(null);
                    setIsDeleteModalOpen(true);
                  },
                },
                canVoid && 'separator',
                canVoid && {
                  label: 'Void invoice',
                  icon: Ban,
                  danger: true,
                  onSelect: () => {
                    setVoidError(null);
                    setIsVoidModalOpen(true);
                  },
                },
              ]}
            />
            {invoice.status === 'Draft' ? (
              canIssue && <HeaderButton label="Issue invoice" icon={Send} variant="primary" busy={isSubmittingIssue} onClick={handleIssueClick} />
            ) : (
              canRecordPayment && (
                <HeaderButton
                  label="Record payment"
                  icon={CreditCard}
                  variant="primary"
                  onClick={() => {
                    setPaymentAmount(invoice.balanceDue.toFixed(2));
                    setPaymentError(null);
                    setDepositBanks(null);
                    setDepositBankId('');
                    setIsPaymentModalOpen(true);
                  }}
                />
              )
            )}
          </>
        }
      />

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

      {postingError && (
        <div role="alert" className="no-print mb-6 p-4 bg-rose-500/10 border border-rose-500/20 text-rose-600 dark:text-rose-400 rounded-xl flex items-start gap-3">
          <AlertTriangle className="w-5 h-5 shrink-0" />
          <div className="text-sm">
            <p className="font-semibold">Issued, but not posted to the ledger</p>
            <p>{postingError}</p>
          </div>
        </div>
      )}

      {stockWarnings.length > 0 && (
        <div role="status" className="no-print mb-6 p-4 bg-amber-500/10 border border-amber-500/20 text-amber-700 dark:text-amber-300 rounded-xl flex items-start gap-3">
          <AlertTriangle className="w-5 h-5 shrink-0" />
          <div className="text-sm">
            <p className="font-semibold">Stock warning</p>
            {stockWarnings.map((warning, index) => <p key={`${index}-${warning}`}>{warning}</p>)}
          </div>
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
            {invoice.no ? (
              <p className="text-base font-bold font-mono text-[var(--brand-primary)]">#{invoice.no}</p>
            ) : (
              <p className="text-xs text-[var(--text-muted)]">Numbered when issued</p>
            )}
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
            {/* Same order as the PDF: address, phone, email, then the attention line after a gap. */}
            {invoice.customerAddress && (
              <div className="flex items-start gap-1.5 text-[var(--text-secondary)]">
                <MapPin className="w-3.5 h-3.5 text-[var(--text-muted)] shrink-0 mt-0.5" />
                <span>{invoice.customerAddress}</span>
              </div>
            )}
            {invoice.customerPhone && (
              <div className="flex items-center gap-1.5 text-[var(--text-secondary)]">
                <Phone className="w-3.5 h-3.5 text-[var(--text-muted)]" />
                {invoice.customerPhone}
              </div>
            )}
            {showCustomerEmail && invoice.customerEmail && (
              <div className="flex items-center gap-1.5 text-[var(--text-secondary)]">
                <Mail className="w-3.5 h-3.5 text-[var(--text-muted)]" />
                {invoice.customerEmail}
              </div>
            )}
            {showAttention && invoice.customerAttention && (
              <div className="pt-3 text-[var(--text-secondary)]">{invoice.customerAttention}</div>
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
                {showTax && <th className="py-3 px-3 text-right w-24">Tax</th>}
                <th className="py-3 px-4 text-right w-32">Total</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-[var(--border-subtle)] text-sm">
              {invoice.salesInvoiceLines?.map((line, idx) => {
                const qty = line.quantity ?? 1;
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
                    {showTax && (
                      <td className="py-3.5 px-3 text-right font-mono text-[var(--text-secondary)]">
                        {line.taxAmount ? formatCurrency(line.taxAmount) : '—'}
                      </td>
                    )}
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

            {(invoice.shippingHandlingCharge ?? 0) > 0 && (
              <div className="flex justify-between text-[var(--text-secondary)]">
                <span>Shipping:</span>
                <span className="font-mono">{formatCurrency(invoice.shippingHandlingCharge ?? 0)}</span>
              </div>
            )}

            <div className="flex justify-between font-bold text-base text-[var(--text-main)] border-t border-[var(--border-subtle)] pt-2.5">
              <span>Total Amount:</span>
              <span className="font-mono">{formatCurrency(invoice.totalAmountAfterTax ?? invoice.amount)}</span>
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

      {/* Email History Section (Hidden on Print) */}
      {invoice.status !== 'Draft' && (
        <div className="no-print mt-6 bg-[var(--bg-surface)] border border-[var(--border-subtle)] rounded-2xl shadow-sm p-6 sm:p-8">
          <div className="flex items-center gap-2 mb-4">
            <Mail className="w-5 h-5 text-[var(--brand-primary)]" />
            <h3 className="font-bold text-[var(--text-main)] text-base">Email History</h3>
          </div>

          {invoice.emails && invoice.emails.length > 0 ? (
            <ol className="divide-y divide-[var(--border-subtle)] rounded-xl border border-[var(--border-subtle)]">
              {invoice.emails.map((email) => (
                <li key={email.id} className="flex flex-col sm:flex-row sm:items-center gap-1 sm:gap-4 px-4 py-3 text-sm">
                  <div className="flex items-center gap-2 min-w-0 flex-1">
                    <Send className="w-3.5 h-3.5 shrink-0 text-[var(--text-muted)]" />
                    <span className="text-[var(--text-secondary)]">
                      {email.kind === 'Invoice'
                        ? 'Invoice sent to'
                        : email.kind === 'FollowUp'
                          ? 'Follow-up sent to'
                          : `${email.kind} sent to`}
                    </span>
                    <span className="font-medium text-[var(--text-main)] truncate">{email.to}</span>
                  </div>
                  <div className="text-xs text-[var(--text-muted)] sm:text-right">
                    {formatDateTime(email.sentAt)}
                    {email.sentBy && <span> · by {email.sentBy}</span>}
                  </div>
                </li>
              ))}
            </ol>
          ) : (
            <div className="text-center py-6 text-sm text-[var(--text-muted)] bg-[var(--bg-subtle)]/50 rounded-xl border border-dashed border-[var(--border-subtle)]">
              {invoice.recipientEmail ? (
                'This invoice has not been emailed to the customer yet.'
              ) : (
                <>
                  <Link to={`/org/${orgSlug}/sales/clients/${invoice.customerId}`} className="font-medium text-[var(--brand-primary)] hover:underline">
                    Add an email address to the client
                  </Link>{' '}
                  to send invoices by email.
                </>
              )}
            </div>
          )}
        </div>
      )}

      {/* Record Payment Modal */}
      <ModalShell
        open={isPaymentModalOpen}
        onClose={() => {
          if (!isSubmittingPayment) setIsPaymentModalOpen(false);
        }}
        closeOnEscape={!isSubmittingPayment}
        closeOnBackdrop={!isSubmittingPayment}
        bare
        overlayClassName="no-print"
        panelClassName="bg-[var(--bg-surface)] rounded-2xl max-w-md w-full border border-[var(--border-subtle)] shadow-2xl overflow-hidden max-h-[min(90vh,calc(100dvh-2rem))] overflow-y-auto"
      >
          <div>
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
                disabled={isSubmittingPayment}
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

              {depositBanks && depositBanks.length > 0 && (
                <div>
                  <label className="block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1">
                    Deposit to <span className="text-rose-500">*</span>
                  </label>
                  <select
                    required
                    value={depositBankId}
                    onChange={(e) => setDepositBankId(e.target.value ? Number(e.target.value) : '')}
                    className="w-full px-3.5 py-2.5 text-sm bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-xl focus:ring-2 focus:ring-[var(--brand-ring)] focus:outline-none text-[var(--text-main)] transition"
                  >
                    {depositBanks.map((b) => (
                      <option key={b.id} value={b.id}>
                        {b.name}
                        {b.accountNumberMasked ? ` · ${b.accountNumberMasked}` : ''}
                        {b.isDefaultForReceipts ? ' (default)' : ''}
                      </option>
                    ))}
                  </select>
                </div>
              )}

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
      </ModalShell>

      {/* Issue / Email Invoice Modal */}
      {sendDialogMode && invoice.recipientEmail && (
        <SendInvoiceDialog
          mode={sendDialogMode}
          invoiceNumber={invoice.no || issueNumber || ''}
          numberOnIssue={sendDialogMode === 'issue' && !invoice.no}
          to={invoice.recipientEmail}
          cc={invoice.recipientCcEmail}
          bcc={
            user?.email &&
            user.email.toLowerCase() !== invoice.recipientEmail.toLowerCase() &&
            user.email.toLowerCase() !== invoice.recipientCcEmail?.toLowerCase()
              ? user.email
              : undefined
          }
          lastSent={sendDialogMode === 'send' ? lastEmail : null}
          followUpEligible={sendDialogMode === 'send' && followUpEligible}
          busy={isSendingEmail || isSubmittingIssue}
          error={sendError}
          onIssueOnly={handleIssueOnly}
          onSend={handleSendEmail}
          onCancel={() => setSendDialogMode(null)}
        />
      )}

      {/* Void Invoice Confirmation Modal */}
      {isVoidModalOpen && invoice && (
        <VoidDialog
          title={`Void invoice #${invoice.no}?`}
          message="Voiding marks the invoice as Void and auto-posts a reversing journal entry to cancel all receivables and revenues. Stock issued on this invoice is put back, and its cost of goods sold is reversed. This can't be undone."
          confirmLabel="Void invoice"
          minDate={invoice.invoiceDate ? invoice.invoiceDate.slice(0, 10) : undefined}
          error={voidError}
          busy={isSubmittingVoid}
          onConfirm={(date) => handleVoidInvoice(date)}
          onCancel={() => {
            setIsVoidModalOpen(false);
            setVoidError(null);
          }}
        />
      )}

      {/* Delete Draft Confirmation Modal */}
      <ModalShell
        open={isDeleteModalOpen}
        onClose={() => {
          if (!isSubmittingDelete) setIsDeleteModalOpen(false);
        }}
        closeOnEscape={!isSubmittingDelete}
        closeOnBackdrop={!isSubmittingDelete}
        bare
        overlayClassName="no-print"
        panelClassName="bg-[var(--bg-surface)] rounded-2xl max-w-md w-full border border-[var(--border-subtle)] shadow-2xl p-6 space-y-4 max-h-[min(90vh,calc(100dvh-2rem))] overflow-y-auto"
      >
        <div className="space-y-4">
          <div className="flex items-center gap-3 text-rose-500">
            <Trash2 className="w-6 h-6 shrink-0" />
            <h3 className="font-bold text-lg text-[var(--text-main)]">
              Delete draft invoice?
            </h3>
          </div>

          <p className="text-sm text-[var(--text-secondary)] leading-relaxed">
            Are you sure you want to permanently delete this draft invoice? This action cannot be undone.
          </p>

          {deleteError && (
            <div className="p-3 bg-rose-500/10 border border-rose-500/20 rounded-xl text-xs text-rose-500">
              {deleteError}
            </div>
          )}

          <div className="pt-2 flex items-center justify-end gap-3">
            <button
              type="button"
              onClick={() => setIsDeleteModalOpen(false)}
              disabled={isSubmittingDelete}
              className="px-4 py-2.5 text-sm font-medium text-[var(--text-secondary)] bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-xl hover:bg-[var(--bg-surface-hover)] hover:text-[var(--text-main)] transition"
            >
              Cancel
            </button>
            <button
              type="button"
              onClick={handleDeleteDraft}
              disabled={isSubmittingDelete}
              className="inline-flex items-center gap-2 px-5 py-2.5 text-sm font-semibold text-white bg-rose-600 rounded-xl hover:bg-rose-700 transition disabled:opacity-50 shadow-sm"
            >
              {isSubmittingDelete && <Loader2 className="w-4 h-4 animate-spin" />}
              Delete Draft
            </button>
          </div>
        </div>
      </ModalShell>
    </div>
  );
};
