import React, { useEffect, useId, useRef, useState } from 'react';
import { Loader2, Mail, Paperclip, Send } from 'lucide-react';

interface SendInvoiceDialogProps {
  /** 'issue' asks whether to email while issuing a draft; 'send' emails an invoice that is already issued. */
  mode: 'issue' | 'send';
  invoiceNumber: string;
  to: string;
  /** The signed-in user, who receives a blind copy. */
  bcc?: string;
  busy: boolean;
  error: string | null;
  onIssueOnly: () => void;
  onSend: () => void;
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
  to,
  bcc,
  busy,
  error,
  onIssueOnly,
  onSend,
  onCancel,
}) => {
  const [typed, setTyped] = useState('');
  const dialogRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const titleId = useId();
  const inputId = useId();
  const confirmed = normalise(typed) === normalise(invoiceNumber);

  useEffect(() => {
    const trigger = document.activeElement as HTMLElement | null;
    inputRef.current?.focus();
    return () => trigger?.focus();
  }, []);

  const handleKeyDown = (event: React.KeyboardEvent) => {
    if (event.key === 'Escape' && !busy) {
      event.preventDefault();
      onCancel();
      return;
    }
    if (event.key !== 'Tab') return;
    const focusable = dialogRef.current?.querySelectorAll<HTMLElement>('input:not(:disabled), button:not(:disabled)');
    if (!focusable?.length) return;
    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  };

  const handleSubmit = (event: React.FormEvent) => {
    event.preventDefault();
    if (confirmed && !busy) onSend();
  };

  return (
    <div className="no-print fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm p-4">
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        onKeyDown={handleKeyDown}
        className="w-full max-w-md rounded-2xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] shadow-2xl"
      >
        <form onSubmit={handleSubmit} className="p-6 space-y-5">
          <div className="flex items-start gap-3">
            <span className="flex items-center justify-center w-10 h-10 shrink-0 rounded-xl bg-[var(--brand-subtle)] text-[var(--brand-primary)]">
              <Mail className="w-5 h-5" />
            </span>
            <div className="min-w-0">
              <h2 id={titleId} className="text-base font-bold text-[var(--text-main)]">
                {mode === 'issue' ? `Issue invoice #${invoiceNumber}` : `Email invoice #${invoiceNumber}`}
              </h2>
              <p className="mt-0.5 text-sm text-[var(--text-secondary)]">
                {mode === 'issue'
                  ? 'Email it to the customer as you issue it?'
                  : 'Send this invoice to the customer by email.'}
              </p>
            </div>
          </div>

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
            <div className="flex items-center gap-3 px-4 py-2.5">
              <dt className="w-12 shrink-0 text-[var(--text-muted)]">
                <Paperclip className="w-4 h-4" aria-label="Attachment" />
              </dt>
              <dd className="min-w-0 break-all text-[var(--text-secondary)]">{invoicePdfFileName(invoiceNumber)}</dd>
            </div>
          </dl>

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
              {mode === 'issue' ? 'Issue & send' : 'Send email'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};

export default SendInvoiceDialog;
