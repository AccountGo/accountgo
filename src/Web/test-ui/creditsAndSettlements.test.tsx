import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { CreditsAndSettlements } from '../src/pages/CreditsAndSettlements';
import { InvoiceDetail } from '../src/pages/InvoiceDetail';

const { authFetch, hasPermission, outlet } = vi.hoisted(() => ({
  authFetch: vi.fn(),
  hasPermission: vi.fn(() => true),
  outlet: {
    currentOrg: { id: '1', name: 'Work', slug: 'work', entityType: 'SoleProprietor', currency: 'USD' },
    hasModule: () => true,
    canWrite: () => true,
  },
}));

vi.mock('../src/auth/AuthContext', () => ({ useAuth: () => ({ authFetch }) }));
vi.mock('../src/components/Gating', () => ({
  usePermissions: () => ({ hasPermission }),
  ReadOnlyModuleBanner: () => null,
}));
vi.mock('../src/lib/entitlements', () => ({
  useCapabilities: () => ({ canModule: () => true, canWrite: () => true, canCreate: () => true }),
}));
vi.mock('../src/lib/useEmailCapabilities', () => ({
  useEmailCapabilities: () => ({ canEmail: false, hasEmailConfig: false }),
}));
vi.mock('../src/components/RecordHistoryPanel', () => ({
  RecordHistoryButton: () => null,
  RecordHistoryPanel: () => null,
  useRecordHistory: () => ({ canShow: false, open: false, openPanel: () => {}, closePanel: () => {}, personal: false }),
}));
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return { ...actual, useOutletContext: () => outlet };
});

const mockMappings = {
  readyForSalesCredits: true,
  readyForSupplierCredits: true,
  readyForAdvances: true,
  readyForWriteOffs: true,
  readyForReturns: true,
  roles: [
    {
      roleKey: 'CustomerCreditLiability',
      roleLabel: 'Customer Credits Liability',
      configuredAccountId: 201,
      configuredAccountCode: '20410',
      configuredAccountName: 'Customer Credits Liability',
      suggestedAccountId: 201,
      suggestedAccountCode: '20410',
      suggestedAccountName: 'Customer Credits Liability',
      isArchived: false,
      isMissing: false,
    },
  ],
};

const mockCredits = [
  {
    id: 10,
    no: 'CCN-00001',
    customerId: 5,
    customerName: 'Acme Corp',
    date: '2026-11-10T00:00:00Z',
    totalAmount: 150,
    appliedAmount: 0,
    refundedAmount: 0,
    availableAmount: 150,
    settlementState: 'Available',
    status: 3,
    statusLabel: 'Posted',
    reason: 'Overpayment concession',
    version: 1,
  },
];

beforeEach(() => {
  authFetch.mockReset();
  authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    if (url === '/api/sales/credits') return new Response(JSON.stringify(mockCredits), { status: 200 });
    if (url === '/api/sales/credits/mappings/preview') return new Response(JSON.stringify(mockMappings), { status: 200 });
    if (url === '/api/sales/credits/mappings' && init?.method === 'PUT') return new Response(JSON.stringify(mockMappings), { status: 200 });
    if (url === '/api/Sales/SalesInvoices') {
      return new Response(JSON.stringify([{ id: 77, no: 'INV-077', customerId: 5, customerName: 'Acme Corp', balanceDue: 500, status: 'Open' }]), { status: 200 });
    }
    if (url === '/api/Sales/Customers') return new Response(JSON.stringify([{ id: 5, name: 'Acme Corp' }]), { status: 200 });
    if (url === '/api/Banking/Accounts/Picker') return new Response(JSON.stringify([{ id: 3, name: 'Main Checking' }]), { status: 200 });
    if (url === '/api/Financials/Accounts') return new Response(JSON.stringify([{ id: 201, accountCode: '20410', accountName: 'Customer Credits Liability' }]), { status: 200 });
    if (url === '/api/sales/credits/preview') return new Response(JSON.stringify({ subtotal: 100, taxAmount: 10, totalAmount: 110 }), { status: 200 });
    if (url === '/api/sales/credits/drafts') return new Response(JSON.stringify({ id: 11, version: 1 }), { status: 200 });
    if (url === '/api/settlements/refunds/drafts') return new Response(JSON.stringify({ id: 31, version: 1 }), { status: 200 });
    if (url.includes('/api/settlements/refunds/31/submit')) return new Response(JSON.stringify({ id: 31, version: 2 }), { status: 200 });
    if (url === '/api/settlements/refunds/31/post') return new Response(JSON.stringify({ id: 31, version: 3 }), { status: 200 });
    if (url === '/api/sales/write-offs/requests') return new Response(JSON.stringify({ id: 41, version: 1 }), { status: 200 });
    if (url === '/api/sales/write-offs/41/post') return new Response(JSON.stringify({ id: 41, version: 2 }), { status: 200 });
    if (url.startsWith('/api/Sales/SalesInvoice?id=99')) {
      return new Response(
        JSON.stringify({
          id: 99,
          no: 'INV-099',
          customerId: 5,
          customerName: 'Acme Corp',
          date: '2026-11-01T00:00:00Z',
          dueDate: '2026-11-30T00:00:00Z',
          totalAmountAfterTax: 1000,
          totalAllocatedAmount: 0,
          paidAmount: 0,
          creditedAmount: 300,
          writtenOffAmount: 700,
          balanceDue: 0,
          settlementSummary: 'Settled: cash 0.00, credits 300.00, written off 700.00',
          posted: true,
          status: 'Settled',
          salesInvoiceLines: [{ id: 1, itemDescription: 'Consulting', quantity: 1, amount: 1000, discount: 0, lineTotal: 1000 }],
          payments: [],
        }),
        { status: 200 }
      );
    }
    return new Response('{}', { status: 200 });
  });
});

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

describe('CreditsAndSettlements and InvoiceDetail settlement UX', () => {
  it('previews and saves a customer credit note using party and invoice selectors, and saves mappings', async () => {
    render(
      <MemoryRouter initialEntries={['/org/work/sales/credits']}>
        <Routes>
          <Route path="/org/:slug/sales/credits" element={<CreditsAndSettlements side="sales" />} />
        </Routes>
      </MemoryRouter>
    );

    expect(await screen.findByText('CCN-00001')).toBeTruthy();

    // Save mappings
    fireEvent.click(screen.getByRole('button', { name: /Save Mappings/i }));
    await waitFor(() => {
      expect(authFetch).toHaveBeenCalledWith('/api/sales/credits/mappings', expect.objectContaining({ method: 'PUT' }));
    });

    // Open New customer credit modal
    fireEvent.click(screen.getByRole('button', { name: /New customer credit/i }));
    expect(screen.getByLabelText('Customer')).toBeTruthy();
    fireEvent.change(screen.getByLabelText('Reason / Evidence'), { target: { value: 'Price adjustment' } });
    fireEvent.change(screen.getByLabelText('Unit Price / Cost'), { target: { value: '100' } });

    fireEvent.click(screen.getByRole('button', { name: 'Preview' }));
    expect(await screen.findByText(/Preview total: 110.00/i)).toBeTruthy();

    fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }));
    await waitFor(() => {
      expect(authFetch).toHaveBeenCalledWith('/api/sales/credits/drafts', expect.objectContaining({ method: 'POST' }));
    });
  });

  it('records an external refund with explicit already-sent/received wording and submits a write-off', async () => {
    render(
      <MemoryRouter initialEntries={['/org/work/sales/credits']}>
        <Routes>
          <Route path="/org/:slug/sales/credits" element={<CreditsAndSettlements side="sales" />} />
        </Routes>
      </MemoryRouter>
    );

    expect(await screen.findByText('CCN-00001')).toBeTruthy();

    fireEvent.click(screen.getByRole('button', { name: 'Record Refund' }));
    expect(await screen.findByRole('heading', { name: 'Record a refund already sent/received' })).toBeTruthy();
    const dialog = screen.getByRole('dialog');
    fireEvent.change(within(dialog).getByLabelText('External Bank Reference'), { target: { value: 'ACH-991' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Record Refund' }));

    await waitFor(() => {
      expect(authFetch).toHaveBeenCalledWith('/api/settlements/refunds/drafts', expect.objectContaining({ method: 'POST' }));
      expect(authFetch).toHaveBeenCalledWith('/api/settlements/refunds/31/post', expect.objectContaining({ method: 'POST' }));
    });

    fireEvent.click(screen.getByRole('button', { name: /Write off invoice/i }));
    fireEvent.change(screen.getByLabelText('Open Invoice'), { target: { value: '77' } });
    fireEvent.change(screen.getByLabelText('Write-Off Amount'), { target: { value: '250' } });
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: 'Insolvency' } });
    fireEvent.change(screen.getByLabelText('Collection Evidence'), { target: { value: 'Notice #4' } });
    fireEvent.click(screen.getByRole('button', { name: 'Submit Write-Off' }));

    await waitFor(() => {
      expect(authFetch).toHaveBeenCalledWith('/api/sales/write-offs/requests', expect.objectContaining({ method: 'POST' }));
      expect(authFetch).toHaveBeenCalledWith('/api/sales/write-offs/41/post', expect.objectContaining({ method: 'POST' }));
    });
  });

  it('displays Settled badge and settlement composition summary instead of Paid on written-off/credited invoice', async () => {
    render(
      <MemoryRouter initialEntries={['/org/work/sales/99']}>
        <Routes>
          <Route path="/org/:slug/sales/:invoiceId" element={<InvoiceDetail />} />
        </Routes>
      </MemoryRouter>
    );

    expect((await screen.findAllByText('Settled')).length).toBeGreaterThan(0);
    expect(screen.getByText(/Settled: cash 0\.00, credits 300\.00, written off 700\.00/)).toBeTruthy();
    expect(screen.queryByText('Paid in full')).toBeNull();
  });
});
