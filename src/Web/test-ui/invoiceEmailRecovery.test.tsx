import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { useState } from 'react';
import { SendInvoiceDialog } from '../src/components/SendInvoiceDialog';
import type { PendingEmailAttempt } from '../src/lib/customEmail';
import { pendingEmailKey, readPendingEmail } from '../src/lib/customEmail';
import { sendInvoiceEmailRequest } from '../src/lib/invoiceEmailDispatch';

afterEach(() => { cleanup(); sessionStorage.clear(); });
const reply = (status: number, body: unknown) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

function Dialog({ fetcher }: { fetcher: typeof fetch }) {
  const [pending, setPending] = useState<PendingEmailAttempt | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  return <SendInvoiceDialog key={pending?.dispatchId ?? 'new'} mode="send" invoiceNumber="INV-42" to="customer@example.org"
    approvedSenders={[{ id: 'sender-1', address: 'billing@invoices.example.com', displayName: 'Acme', domainId: 'domain-1', ready: true }]}
    pendingCustomSend={pending} busy={busy} error={error} onCancel={() => {}} onIssueOnly={() => {}}
    onSend={async (followUp, attachPdf, additionalRecipients = [], cc = [], approvedSenderId) => {
      setBusy(true); setError(null);
      try { await sendInvoiceEmailRequest(fetcher, sessionStorage, { companyId: '1', invoiceId: 42, followUp, attachPdf,
        additionalRecipients, cc, approvedSenderId }, setPending); }
      catch (err) { setError((err as Error).message); }
      finally { setBusy(false); }
    }} />;
}

const confirm = () => fireEvent.change(screen.getByLabelText(/To send, type the invoice number/), { target: { value: 'INV-42' } });
const from = () => screen.getByLabelText('From') as HTMLSelectElement;

describe('invoice dialog denial recovery with actual HTTP orchestration', () => {
  it.each([
    ['EMAIL_DOMAIN_NOT_READY', 'NeverAdmitted'], ['EMAIL_SENDER_NOT_APPROVED', 'NeverAdmitted'],
    ['EMAIL_DISPATCH_LIMIT', 'NeverAdmitted'], ['INVALID_RECIPIENT', 'NeverAdmitted'],
    ['PROVIDER_REJECTED', 'DefinitivelyRejected'], ['ADMITTED_BUT_NOT_SUBMITTED', 'Pending'],
  ])('%s releases controls only after server abandonment and permits an explicit platform send', async (code, state) => {
      const fetcher = vi.fn<typeof fetch>(async (url, init) => {
        if (String(url).includes('/abandon')) return reply(200, { state: 'Abandoned', canAbandon: true });
        if (String(url).includes('/api/email/dispatches/')) return reply(200, { state, canAbandon: true });
        if (String(url).includes('sendingMode=custom')) return reply(403, { code, message: 'Custom send denied' });
        expect(init?.method).toBe('POST');
        return reply(200, { to: 'customer@example.org' });
      });
      render(<Dialog fetcher={fetcher} />);
      fireEvent.change(from(), { target: { value: 'sender-1' } }); confirm();
      fireEvent.click(screen.getByRole('button', { name: 'Send email' }));
      await screen.findByRole('alert');
      await waitFor(() => expect(from().matches(':disabled')).toBe(false));
      expect(readPendingEmail(sessionStorage, pendingEmailKey('1', 42))).toBeNull();
      expect(from().value).toBe('');
      confirm(); fireEvent.click(screen.getByRole('button', { name: 'Send email' }));
      await waitFor(() => expect(fetcher.mock.calls.filter(([url]) => String(url).includes('/api/Sales/'))).toHaveLength(2));
      expect(String(fetcher.mock.calls.at(-1)![0])).not.toContain('sendingMode=custom');
    });

  it.each(['Ambiguous', 'Pending', 'Accepted'])('%s preserves intent and locks sender/recipient changes', async state => {
    const fetcher = vi.fn<typeof fetch>(async url => String(url).includes('/api/email/dispatches/')
      ? reply(200, { state, canAbandon: false }) : reply(502, { message: 'Outcome unconfirmed' }));
    render(<Dialog fetcher={fetcher} />);
    fireEvent.change(from(), { target: { value: 'sender-1' } }); confirm();
    fireEvent.click(screen.getByRole('button', { name: 'Send email' }));
    await screen.findByRole('alert');
    expect(from().matches(':disabled')).toBe(true);
    expect(screen.getByRole('button', { name: 'Add recipient' }).matches(':disabled')).toBe(true);
    expect(readPendingEmail(sessionStorage, pendingEmailKey('1', 42))?.senderId).toBe('sender-1');
    expect(fetcher.mock.calls.some(([url]) => String(url).includes('/abandon'))).toBe(false);
    if (state === 'Accepted') expect(screen.getByRole('alert').textContent).toContain('provider accepted');
  });

  it('a safe status read followed by an abandonment race does not unlock the dialog', async () => {
    const fetcher = vi.fn<typeof fetch>(async url => String(url).includes('/abandon')
      ? reply(409, { code: 'EMAIL_DISPATCH_ACCEPTANCE_RISK' }) : String(url).includes('/api/email/dispatches/')
        ? reply(200, { state: 'NeverAdmitted', canAbandon: true }) : reply(503, { message: 'Request interrupted' }));
    render(<Dialog fetcher={fetcher} />);
    fireEvent.change(from(), { target: { value: 'sender-1' } }); confirm();
    fireEvent.click(screen.getByRole('button', { name: 'Send email' }));
    await screen.findByRole('alert');
    expect(from().matches(':disabled')).toBe(true);
    expect(readPendingEmail(sessionStorage, pendingEmailKey('1', 42))).not.toBeNull();
  });
});
