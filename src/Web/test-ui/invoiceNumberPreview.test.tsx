import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { MemoryRouter, Outlet, Route, Routes } from 'react-router-dom';
import { InvoiceDetail } from '../src/pages/InvoiceDetail';
import { InvoiceEdit } from '../src/pages/InvoiceEdit';
import type { Invoice } from '../src/types';

const { authFetch } = vi.hoisted(() => ({ authFetch: vi.fn() }));
vi.mock('../src/auth/AuthContext', () => ({ useAuth: () => ({ authFetch, user: null }) }));
vi.mock('../src/lib/entitlements', async importOriginal => ({
  ...await importOriginal<typeof import('../src/lib/entitlements')>(),
  useCapabilities: () => ({ canCreate: () => true }),
}));

const draft: Invoice = {
  id: 42, no: null, customerId: 1, customerName: 'Example client', invoiceDate: '2027-02-01',
  amount: 100, totalAllocatedAmount: 0, paidAmount: 0, balanceDue: 100,
  posted: false, status: 'Draft', salesInvoiceLines: [],
};
let invoice: Invoice;
let canCreate: boolean;
let issued: boolean;
let taken: boolean;
const reply = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });

beforeEach(() => {
  invoice = { ...draft };
  canCreate = true;
  issued = false;
  taken = false;
  authFetch.mockReset().mockImplementation(async (url: string, init?: RequestInit) => {
    if (url.includes('NextSalesInvoiceNumber')) return reply({ next: '8' });
    if (url.includes('IssueSalesInvoice')) {
      expect(init?.method).toBe('POST');
      const expected = new URL(url, 'https://example.org').searchParams.get('expectedNo');
      if (taken && expected !== '9') return reply({ code: 'number_changed', next: '9', message: 'Another invoice took #8.' }, 409);
      // Simulate the real endpoint: an omitted expectedNo silently takes the next number.
      issued = true;
      invoice = { ...invoice, no: taken ? '9' : '8', status: 'Open' };
      return reply({ no: invoice.no });
    }
    if (url.includes('SalesInvoice?id=')) return reply(invoice);
    if (url === '/api/email/capabilities') return reply({ companyId: '1', approvedSenders: [] });
    return reply([]);
  });
});
afterEach(cleanup);

function openInvoice(edit = false) {
  const context = {
    currentOrg: { id: '1', name: 'Example business', currency: 'USD' },
    canWrite: () => true, hasModule: () => true,
    hasPermission: (permission: string) => permission === 'sales.invoices.post' || (canCreate && permission === 'sales.invoices.create'),
    userPermissions: [], canAccess: () => true,
  };
  return render(<MemoryRouter initialEntries={['/org/example/sales/42']}>
    <Routes><Route element={<Outlet context={context} />}>
      <Route path="/org/:orgSlug/sales/:invoiceId" element={edit ? <InvoiceEdit isEdit /> : <InvoiceDetail />} />
    </Route></Routes>
  </MemoryRouter>);
}

it('previews the number for the draft date and sends it when issuing without an email', async () => {
  openInvoice();
  await screen.findByText(/Next number: #8/);
  expect(authFetch.mock.calls.filter(([url]) => String(url).includes('NextSalesInvoiceNumber')).map(([url]) => url))
    .toEqual(['/api/Sales/NextSalesInvoiceNumber?invoiceDate=2027-02-01']);
  fireEvent.click(screen.getByRole('button', { name: 'Issue invoice' }));
  await waitFor(() => expect(issued).toBe(true));
  expect(authFetch).toHaveBeenCalledWith('/api/Sales/IssueSalesInvoice?id=42&expectedNo=8', { method: 'POST' });
});

it('requires another explicit issue action after the previewed number is taken', async () => {
  openInvoice();
  await screen.findByText(/Next number: #8/);
  taken = true;
  fireEvent.click(screen.getByRole('button', { name: 'Issue invoice' }));
  await screen.findByText(/Next number: #9/);
  expect(issued).toBe(false);
  expect(screen.getByText(/Click Issue invoice again to confirm #9/)).toBeTruthy();
  fireEvent.click(screen.getByRole('button', { name: 'Issue invoice' }));
  await waitFor(() => expect(issued).toBe(true));
  expect(authFetch).toHaveBeenCalledWith('/api/Sales/IssueSalesInvoice?id=42&expectedNo=9', { method: 'POST' });
});

it('does not request or show an automatic number for a draft with a typed number', async () => {
  invoice.no = 'CUSTOM-42';
  openInvoice();
  await screen.findByRole('button', { name: 'Issue invoice' });
  expect(screen.queryByText(/Next number:/)).toBeNull();
  expect(authFetch.mock.calls.some(([url]) => String(url).includes('NextSalesInvoiceNumber'))).toBe(false);
});

it('does not request or show a number without invoice create permission', async () => {
  canCreate = false;
  openInvoice();
  await screen.findByRole('button', { name: 'Issue invoice' });
  expect(screen.queryByText(/Next number:/)).toBeNull();
  expect(authFetch.mock.calls.some(([url]) => String(url).includes('NextSalesInvoiceNumber'))).toBe(false);
});

it('shows an estimate on the edit page and uses the typed number in its header', async () => {
  openInvoice(true);
  await waitFor(() => expect(screen.getByRole('banner').textContent).toContain('Next number: #8'));
  const requests = authFetch.mock.calls.length;
  fireEvent.change(screen.getByLabelText('Invoice #'), { target: { value: 'CUSTOM-42' } });
  expect(screen.getByRole('banner').textContent).toContain('Invoice number: #CUSTOM-42');
  expect(screen.getByRole('banner').textContent).not.toContain('Next number:');
  expect(authFetch.mock.calls).toHaveLength(requests);
});

it('does not show draft numbering hints while an issued invoice is loading for edit', async () => {
  invoice = { ...draft, status: 'Open', no: 'INV-42' };
  const fetcher = authFetch.getMockImplementation()!;
  let finishLoading!: () => void;
  authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    if (url.includes('SalesInvoice?id=')) await new Promise<void>(resolve => { finishLoading = resolve; });
    return fetcher(url, init);
  });
  const page = openInvoice(true);
  await screen.findByPlaceholderText('Auto · 8');
  expect(screen.getByRole('banner').textContent).not.toContain('Next number:');
  expect(screen.queryByText(/Blank: numbered when issued/)).toBeNull();
  page.unmount();
  finishLoading();
});
