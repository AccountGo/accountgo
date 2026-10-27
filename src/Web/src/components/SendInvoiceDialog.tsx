import React, { useEffect, useId, useRef, useState } from 'react';
import { AlertTriangle, Loader2, Mail, Paperclip, Send } from 'lucide-react';
import { ModalShell } from './ModalShell';

interface SendInvoiceDialogProps {
  /** 'issue' asks whether to email while issuing a draft; 'send' emails an invoice that is already issued. */
  mode: 'issue' | 'send';
  invoiceNumber: string;
  /** The draft has no number yet; `invoiceNumber` is the one it takes when issued. */
  numberOnIssue?: boolean;
  to: string;
  /** The signed-in user, who receives a blind copy. */
  bcc?: string;
  /** When the invoice was already emailed from AccountGo, so a second send is a follow-up. */
  lastSent?: { to: string; sentAt: string } | null;
  /**
   * Unpaid issued invoices can use the follow-up template. With a prior in-app email the follow-up
   * template is pre-selected (no toggle); without one, the user can mark that the first contact was
   * outside the platform. Sending still always requires typing the invoice number.
   */
  followUpEligible?: boolean;
  busy: boolean;
  error: string | null;
  onIssueOnly: () => void;
  onSend: (followUp: boolean, attachPdf: boolean) => void;
  onCancel: () => void;
}

/** Mirrors the server's attachment name (InvoiceEmailTemplate.PdfFileName). */
export const invoicePdfFileName = (invoiceNumber: string) =>
  `Invoice-${invoiceNumber.replace(/[^A-Za-z0-9_-]/g, '-')}.pdf`;

const normalise = (value: string) => value.trim().replace(/^#/, '').toLowerCase();

// Emailing a customer can't be taken back, so sending needs the invoice number typed in, not just a click.
export const SendInvoiceDialog: React.FC<SendInvoiceDialogProps> = ({
  mode,
  invoiceNumber,
  numberOnIssue = false,
  to,
  bcc,
  lastSent,
  followUpEligible = false,
  busy,
  error,
  onIssueOnly,
  onSend,
  onCancel,
}) => {
  const hasPriorEmail = !!lastSent;
  // Prior in-app email → follow-up template is locked in (still need invoice number to send).
  const followUpFromHistory = mode === 'send' && followUpEligible && hasPriorEmail;
  const showFollowUpToggle = mode === 'send' && followUpEligible && !hasPriorEmail;

  const [typed, setTyped] = useState('');
  const [markAsFollowUp, setMarkAsFollowUp] = useState(false);
  const [showAdvanced, setShowAdvanced] = useState(false);
  const [attachPdf, setAttachPdf] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);
  const titleId = useId();
  const inputId = useId();
  const followUpToggleId = useId();
  const advancedToggleId = useId();
  const attachPdfId = useId();
  const confirmed = normalise(typed) === normalise(invoiceNumber);
  const asFollowUp = followUpFromHistory || markAsFollowUp;

  useEffect(() => {
    inputRef.current?.focus();
  }, []);

  const handleSubmit = (event: React.FormEvent) => {
    event.preventDefault();
    if (confirmed && !busy) onSend(asFollowUp, attachPdf);
  };

  const title =
    mode === 'issue'
      ? `Issue invoice #${invoiceNumber}`
      : asFollowUp
        ? `Send follow-up #${invoiceNumber}`
        : `Email invoice #${invoiceNumber}`;

  const description =
    mode === 'issue'
      ? `${numberOnIssue ? `It takes the next invoice number, #${invoiceNumber}. ` : ''}Email it to the customer as you issue it?`
      : asFollowUp
        ? 'Send a polite reminder that this invoice is still unpaid.'
        : 'Send this invoice to the customer by email.';

  return (
    <ModalShell
      open
      onClose={() => {
        if (!busy) onCancel();
      }}
      closeOnEscape={!busy}
      closeOnBackdrop={!busy}
      bare
      aria-labelledby={titleId}
      overlayClassName="no-print"
      panelClassName="no-print w-full max-w-md max-h-[min(90vh,calc(100dvh-2rem))] overflow-y-auto rounded-2xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] shadow-2xl"
    >
        <form onSubmit={handleSubmit} className="p-6 space-y-5">
          <div className="flex items-start gap-3">
            <span className="flex items-center justify-center w-10 h-10 shrink-0 rounded-xl bg-[var(--brand-subtle)] text-[var(--brand-primary)]">
              <Mail className="w-5 h-5" />
            </span>
            <div className="min-w-0">
              <h2 id={titleId} className="text-base font-bold text-[var(--text-main)]">
                {title}
              </h2>
              <p className="mt-0.5 text-sm text-[var(--text-secondary)]">{description}</p>
            </div>
          </div>

          {lastSent && (
            <div className="flex items-start gap-2.5 p-3 rounded-xl bg-amber-500/10 border border-amber-500/20 text-xs text-amber-600 dark:text-amber-400">
              <AlertTriangle className="w-4 h-4 shrink-0 mt-px" />
              <span>
                Already emailed to <strong className="font-semibold">{lastSent.to}</strong> on{' '}
                {new Date(lastSent.sentAt).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })}.
                {followUpFromHistory
                  ? ' Confirm below to send a follow-up reminder.'
                  : ' Sending again gives the customer a second copy.'}
              </span>
            </div>
          )}

          {showFollowUpToggle && (
            <label
              htmlFor={followUpToggleId}
              className="flex items-start gap-3 p-3 rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-subtle)] cursor-pointer"
            >
              <input
                id={followUpToggleId}
                type="checkbox"
                checked={markAsFollowUp}
                disabled={busy}
                onChange={(event) => setMarkAsFollowUp(event.target.checked)}
                className="mt-0.5 h-4 w-4 rounded border-[var(--border-subtle)] text-[var(--brand-primary)] focus:ring-[var(--brand-primary)]"
              />
              <span className="min-w-0 text-sm text-[var(--text-secondary)]">
                <span className="font-medium text-[var(--text-main)]">This is a follow-up</span>
                <span className="block mt-0.5 text-xs text-[var(--text-muted)]">
                  Use when the customer already received this invoice outside AccountGo.
                </span>
              </span>
            </label>
          )}

          <dl className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-subtle)] divide-y divide-[var(--border-subtle)] text-sm">
            <div className="flex gap-3 px-4 py-2.5">
              <dt className="w-12 shrink-0 text-[var(--text-muted)]">To</dt>
              <dd className="min-w-0 break-all font-medium text-[var(--text-main)]">{to}</dd>
            </div>
            {bcc && (
              <div className="flex gap-3 px-4 py-2.5">
                <dt className="w-12 shrink-0 text-[var(--text-muted)]">Bcc</dt>
                <dd className="min-w-0 break-all text-[var(--text-secondary)]">
                  {bcc} <span className="text-[var(--text-muted)]">(your copy)</span>
                </dd>
              </div>
            )}
            <div className="flex items-start gap-3 px-4 py-2.5">
              <dt className="w-12 shrink-0 text-[var(--text-muted)] pt-0.5">PDF</dt>
              <dd className="min-w-0 text-[var(--text-secondary)]">
                {attachPdf ? (
                  <span className="inline-flex items-center gap-1.5 break-all">
                    <Paperclip className="w-3.5 h-3.5 shrink-0" aria-hidden />
                    Secure link + {invoicePdfFileName(invoiceNumber)}
                  </span>
                ) : (
                  <span>Secure link in the email (expires in 7 days)</span>
                )}
              </dd>
            </div>
          </dl>

          <div className="space-y-2">
            <button
              type="button"
              id={advancedToggleId}
              disabled={busy}
              aria-expanded={showAdvanced}
              onClick={() => setShowAdvanced((open) => !open)}
              className="text-xs font-medium text-[var(--text-muted)] hover:text-[var(--text-secondary)]"
            >
              {showAdvanced ? 'Hide advanced' : 'Advanced'}
            </button>
            {showAdvanced && (
              <label
                htmlFor={attachPdfId}
                className="flex items-start gap-3 p-3 rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-subtle)] cursor-pointer"
              >
                <input
                  id={attachPdfId}
                  type="checkbox"
                  checked={attachPdf}
                  disabled={busy}
                  onChange={(event) => setAttachPdf(event.target.checked)}
                  className="mt-0.5 h-4 w-4 rounded border-[var(--border-subtle)] text-[var(--brand-primary)] focus:ring-[var(--brand-primary)]"
                />
                <span className="min-w-0 text-sm text-[var(--text-secondary)]">
                  <span className="font-medium text-[var(--text-main)]">Also attach the PDF</span>
                  <span className="block mt-0.5 text-xs text-[var(--text-muted)]">
                    Some mail filters block attachments; prefer the secure link unless the customer needs a file.
                  </span>
                </span>
              </label>
            )}
          </div>

          <div className="space-y-1.5">
            <label htmlFor={inputId} className="block text-sm text-[var(--text-secondary)]">
              To send, type the invoice number <strong className="font-semibold text-[var(--text-main)]">{invoiceNumber}</strong>
            </label>
            <input
              ref={inputRef}
              id={inputId}
              value={typed}
              onChange={(event) => setTyped(event.target.value)}
              disabled={busy}
              autoComplete="off"
              spellCheck={false}
              placeholder={invoiceNumber}
              className="w-full px-3.5 py-2.5 text-sm rounded-xl bg-[var(--bg-subtle)] border border-[var(--border-subtle)] text-[var(--text-main)] placeholder:text-[var(--text-muted)]/60 focus:outline-none focus:ring-2 focus:ring-[var(--brand-primary)]"
            />
          </div>

          {error && (
            <div role="alert" className="p-3 bg-rose-500/10 border border-rose-500/20 rounded-xl text-xs text-rose-500">
              {error}
            </div>
          )}

          <div className="flex flex-col-reverse sm:flex-row sm:items-center sm:justify-end gap-2">
            <button
              type="button"
              disabled={busy}
              onClick={onCancel}
              className="h-10 px-4 rounded-xl text-sm font-medium text-[var(--text-secondary)] hover:bg-[var(--bg-surface-hover)] disabled:opacity-50"
            >
              Cancel
            </button>
            {mode === 'issue' && (
              <button
                type="button"
                disabled={busy}
                onClick={onIssueOnly}
                className="h-10 px-4 rounded-xl text-sm font-medium text-[var(--text-main)] border border-[var(--border-subtle)] bg-[var(--bg-surface)] hover:bg-[var(--bg-surface-hover)] disabled:opacity-50"
              >
                Issue only
              </button>
            )}
            <button
              type="submit"
              disabled={!confirmed || busy}
              className="inline-flex items-center justify-center gap-2 h-10 px-4 rounded-xl text-sm font-semibold text-[var(--brand-text)] bg-[var(--brand-primary)] hover:bg-[var(--brand-hover)] disabled:opacity-40 disabled:cursor-not-allowed"
            >
              {busy ? <Loader2 className="w-4 h-4 animate-spin" /> : <Send className="w-4 h-4" />}
              {mode === 'issue' ? 'Issue & send' : asFollowUp ? 'Send follow-up' : 'Send email'}
            </button>
          </div>
        </form>
    </ModalShell>
  );
};

export default SendInvoiceDialog;
