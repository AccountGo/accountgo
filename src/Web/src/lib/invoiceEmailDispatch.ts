import { pendingEmailKey, prepareCustomEmailAttempt, readPendingEmail } from './customEmail';
import type { PendingEmailAttempt } from './customEmail';

type StorageAccess = Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>;
type AuthFetch = (url: string, init?: RequestInit) => Promise<Response>;

export interface InvoiceEmailRequest {
  companyId: string;
  invoiceId: string | number;
  followUp: boolean;
  attachPdf: boolean;
  additionalRecipients: string[];
  cc: string[];
  approvedSenderId?: string;
}

/** A negative status read never clears intent: atomic abandonment must also stop any late original request. */
export async function recoverPendingEmail(fetcher: AuthFetch, storage: StorageAccess, key: string,
  invoiceId: string | number, pending: PendingEmailAttempt): Promise<{ abandoned: boolean; message: string }> {
  const path = `/api/email/dispatches/${encodeURIComponent(pending.dispatchId)}?invoiceId=${encodeURIComponent(invoiceId)}&purpose=${pending.followUp ? 'FollowUp' : 'Invoice'}`;
  try {
    const status = await fetcher(path);
    if (!status.ok) return { abandoned: false, message: 'The server could not confirm this dispatch. Preserve its original intent.' };
    const disposition = await status.json();
    if (disposition.canAbandon !== true) return { abandoned: false, message: disposition.state === 'Accepted'
      ? 'The provider accepted this email. Retry the original request to refresh its result; do not send a replacement.'
      : disposition.state === 'Pending' ? 'This dispatch is still being processed. Preserve its original sender and recipients.'
        : 'Provider acceptance is uncertain. Preserve the original dispatch for retry or investigation.' };
    const closed = await fetcher(path.replace('?', '/abandon?'), { method: 'POST' });
    if (!closed.ok || (await closed.json()).state !== 'Abandoned')
      return { abandoned: false, message: 'The dispatch could not be safely closed. Its acceptance state may have changed; preserve the original intent.' };
    if (readPendingEmail(storage, key)?.dispatchId !== pending.dispatchId)
      return { abandoned: false, message: 'A newer attempt is being handled. Preserve its intent.' };
    storage.removeItem(key);
    return { abandoned: true, message: 'No email was accepted. You can change the sender or recipients.' };
  } catch { return { abandoned: false, message: 'The server could not confirm this dispatch. Preserve its original intent.' }; }
}

/** Shared by the invoice page and DOM tests: sends once, then reconciles failed custom requests with server certainty. */
export async function sendInvoiceEmailRequest(fetcher: AuthFetch, storage: StorageAccess, request: InvoiceEmailRequest,
  onPending: (pending: PendingEmailAttempt | null) => void): Promise<string> {
  const key = pendingEmailKey(request.companyId, request.invoiceId);
  if (!request.approvedSenderId && readPendingEmail(storage, key))
    throw new Error('An unresolved custom send must be retried with its original sender before starting another send.');
  const pending = request.approvedSenderId ? prepareCustomEmailAttempt(storage, key, {
    senderId: request.approvedSenderId, followUp: request.followUp, attachPdf: request.attachPdf,
    additionalRecipients: request.additionalRecipients, cc: request.cc,
  }) : null;
  if (pending) onPending(pending);
  const query = new URLSearchParams({ id: String(request.invoiceId) });
  if (request.attachPdf) query.set('attachPdf', 'true');
  for (const name of ['additionalRecipients', 'cc'] as const)
    for (const address of request[name]) if (address.trim()) query.append(name, address.trim());
  if (pending) {
    query.set('sendingMode', 'custom'); query.set('approvedSenderId', pending.senderId); query.set('dispatchId', pending.dispatchId);
  }
  try {
    const res = await fetcher(`/api/Sales/${request.followUp ? 'SendSalesInvoiceFollowUpEmail' : 'SendSalesInvoiceEmail'}?${query}`, { method: 'POST' });
    const data = await res.json().catch(() => null);
    if (!res.ok) throw new Error(data?.message || 'The email could not be sent.');
    if (pending && readPendingEmail(storage, key)?.dispatchId === pending.dispatchId) { storage.removeItem(key); onPending(null); }
    return data?.to || '';
  } catch (err) {
    if (pending) {
      const recovery = await recoverPendingEmail(fetcher, storage, key, request.invoiceId, pending);
      if (recovery.abandoned) onPending(null);
      throw new Error(recovery.abandoned ? `${err instanceof Error ? err.message : 'The send failed.'} ${recovery.message}` : recovery.message);
    }
    throw err;
  }
}
