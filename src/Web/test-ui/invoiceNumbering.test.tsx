import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { formatInvoiceNumberPreview, InvoiceNumberingSection, type InvoiceNumberingSettings } from '../src/components/InvoiceNumberingSection';
import { InvoiceDetail } from '../src/pages/InvoiceDetail';
import { InvoiceEdit } from '../src/pages/InvoiceEdit';

const { authFetch, hasPermission, outlet } = vi.hoisted(() => ({
  authFetch: vi.fn(),
  hasPermission: vi.fn((_p: string) => true),
  outlet: {
    currentOrg: { id: '1', slug: 'acme', name: 'Acme Corp', currency: 'USD', plan: 'invoicing' },
    subscription: { planId: 'invoicing' },
    hasModule: () => true,
    canWrite: () => true,
  },
}));
vi.mock('../src/auth/AuthContext', () => ({ useAuth: () => ({ authFetch, user: { email: 'owner@acme.test' } }) }));
vi.mock('../src/components/Gating', () => ({
  usePermissions: () => ({ hasPermission }),
  ReadOnlyModuleBanner: () => null,
}));
vi.mock('../src/lib/entitlements', () => ({
  useCapabilities: () => ({ canCreate: () => true }),
  useOptionalCapabilities: () => ({ ready: true }),
}));
vi.mock('../src/lib/useEmailCapabilities', () => ({
  useEmailCapabilities: () => ({ capabilities: { approvedSenders: [] } }),
}));
vi.mock('../src/components/RecordHistoryPanel', () => ({
  useRecordHistory: () => ({ canShow: false, open: false, openPanel: vi.fn(), closePanel: vi.fn(), personal: false }),
  RecordHistoryButton: () => null,
  RecordHistoryPanel: () => null,
}));
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return {
    ...actual,
    useOutletContext: () => outlet,
  };
});

afterEach(() => {
  cleanup();
  authFetch.mockReset();
  hasPermission.mockImplementation(() => true);
});

const numberingSettings = (overrides: Partial<InvoiceNumberingSettings> = {}): InvoiceNumberingSettings => ({
  format: null,
  nextNumber: 1,
  minNextNumber: 1,
  currentFiscalYearCode: '2026',
  nextFiscalYearCode: '2027',
  sampleCurrentYear: '1',
  sampleNextYear: '1',
  hasIssuedInCurrentFiscalYear: false,
  ...overrides,
});

function jsonResponse(body: unknown, status = 200) {
  return { ok: status >= 200 && status < 300, status, json: async () => body };
}

describe('invoice numbering preview helper', () => {
  it('formats continuous, fiscal-year, and validates tokens and bounds', () => {
    expect(formatInvoiceNumberPreview('', '2026', 12)).toEqual({ valid: true, preview: '12', error: null });
    expect(formatInvoiceNumberPreview('INV-{FY}-{####}', '2026-27', 5)).toEqual({
      valid: true,
      preview: 'INV-2026-27-0005',
      error: null,
    });
    expect(formatInvoiceNumberPreview('INV-{DATE}-{####}', '2026', 1).valid).toBe(false);
    expect(formatInvoiceNumberPreview('INV-{FY}', '2026', 1).valid).toBe(false);
    expect(formatInvoiceNumberPreview('-INV-{####}', '2026', 1).valid).toBe(false);
    expect(formatInvoiceNumberPreview('VERY-LONG-PREFIX-1234567890-{####}', '2026', 1).valid).toBe(false);
  });
});

describe('InvoiceNumberingSection', () => {
  it('renders live samples, validates forward-only next number, warns on format change, and saves', async () => {
    const initial = numberingSettings({
      format: 'INV-{FY}-{####}',
      nextNumber: 4,
      minNextNumber: 4,
      sampleCurrentYear: 'INV-2026-0004',
      sampleNextYear: 'INV-2027-0001',
      hasIssuedInCurrentFiscalYear: true,
    });
    const updated = numberingSettings({
      format: 'SI-{FY}-{###}',
      nextNumber: 5,
      minNextNumber: 5,
      sampleCurrentYear: 'SI-2026-005',
      sampleNextYear: 'SI-2027-001',
      hasIssuedInCurrentFiscalYear: true,
    });

    authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (url.endsWith('/sales-invoice-numbering') && init?.method === 'PUT') {
        return jsonResponse(updated);
      }
      return jsonResponse(initial);
    });

    render(<InvoiceNumberingSection canEdit />);

    const formatInput = (await screen.findByLabelText(/Format pattern/)) as HTMLInputElement;
    const nextInput = screen.getByLabelText(/Next number/) as HTMLInputElement;
    expect(formatInput.value).toBe('INV-{FY}-{####}');
    expect(screen.getByTestId('numbering-preview-current').textContent).toBe('INV-2026-0004');
    expect(screen.getByTestId('numbering-preview-next').textContent).toBe('INV-2027-0001');

    // Change format -> triggers D6 warning and updates live previews
    fireEvent.change(formatInput, { target: { value: 'SI-{FY}-{###}' } });
    expect(screen.getByRole('status').textContent).toContain('already has issued invoices');
    expect(screen.getByTestId('numbering-preview-current').textContent).toBe('SI-2026-004');
    expect(screen.getByTestId('numbering-preview-next').textContent).toBe('SI-2027-001');

    // Moving nextNumber backwards shows forward-only error and disables Save
    fireEvent.change(nextInput, { target: { value: '3' } });
    expect(screen.getByRole('alert').textContent).toContain('cannot be lower than 4');
    const saveBtn = screen.getByRole('button', { name: /Save Numbering/ }) as HTMLButtonElement;
    expect(saveBtn.disabled).toBe(true);

    // Valid nextNumber -> Save succeeds
    fireEvent.change(nextInput, { target: { value: '5' } });
    expect(saveBtn.disabled).toBe(false);
    fireEvent.click(saveBtn);

    await waitFor(() => {
      expect(screen.getByRole('status').textContent).toContain('Invoice numbering settings saved.');
    });
    expect(authFetch).toHaveBeenCalledWith(
      '/api/organizations/current/sales-invoice-numbering',
      expect.objectContaining({
        method: 'PUT',
        body: JSON.stringify({ format: 'SI-{FY}-{###}', nextNumber: 5 }),
      }),
    );
  });

  it('rejects invalid tokens before saving', async () => {
    authFetch.mockResolvedValue(jsonResponse(numberingSettings()));
    render(<InvoiceNumberingSection canEdit />);

    const formatInput = await screen.findByLabelText(/Format pattern/);
    fireEvent.change(formatInput, { target: { value: 'INV-{BAD}-{####}' } });
    expect(screen.getByRole('alert').textContent).toContain('Unknown token {BAD}');
    expect((screen.getByRole('button', { name: /Save Numbering/ }) as HTMLButtonElement).disabled).toBe(true);
  });
});

describe('Draft invoice next-number preview', () => {
  it('previews the next invoice number immediately when opening an unnumbered draft in InvoiceDetail', async () => {
    authFetch.mockImplementation(async (url: string) => {
      if (url.startsWith('/api/Sales/SalesInvoice?id=77')) {
        return jsonResponse({
          id: 77,
          no: null,
          status: 'Draft',
          customerId: 10,
          customerName: 'Northwind Studio',
          invoiceDate: '2026-11-15',
          dueDate: '2026-11-30',
          amount: 500,
          totalTax: 0,
          totalAmountAfterTax: 500,
          paidAmount: 0,
          balanceDue: 500,
          salesInvoiceLines: [
            { id: 1, itemDescription: 'Retainer', quantity: 1, amount: 500, discount: 0, lineTotal: 500 },
          ],
        });
      }
      if (url.startsWith('/api/Sales/NextSalesInvoiceNumber')) {
        return jsonResponse({ next: 'INV-2026-0008' });
      }
      return jsonResponse({});
    });

    render(
      <MemoryRouter initialEntries={['/org/acme/sales/77']}>
        <Routes>
          <Route path="/org/:orgSlug/sales/:invoiceId" element={<InvoiceDetail />} />
        </Routes>
      </MemoryRouter>,
    );

    const preview = await screen.findByTestId('draft-next-invoice-number');
    expect(preview.textContent).toContain('Next: #INV-2026-0008 (assigned on issue)');
    expect(screen.getByText(/Next number: #INV-2026-0008/)).toBeTruthy();
    expect(authFetch).toHaveBeenCalledWith('/api/Sales/NextSalesInvoiceNumber?invoiceDate=2026-11-15', expect.objectContaining({ signal: expect.any(AbortSignal) }));
  });

  it('previews the next invoice number in the hint and header subtitle when editing a draft in InvoiceEdit', async () => {
    authFetch.mockImplementation(async (url: string) => {
      if (url.startsWith('/api/Sales/SalesInvoice?id=77')) {
        return jsonResponse({
          id: 77,
          no: null,
          status: 'Draft',
          customerId: 10,
          customerName: 'Northwind Studio',
          invoiceDate: '2026-11-15',
          dueDate: '2026-11-30',
          salesInvoiceLines: [
            { id: 1, itemDescription: 'Retainer', quantity: 1, amount: 500, discount: 0 },
          ],
        });
      }
      if (url.startsWith('/api/Sales/NextSalesInvoiceNumber')) {
        return jsonResponse({ next: 'INV-2026-0008' });
      }
      if (url.includes('/api/Tax/Taxes')) {
        return jsonResponse({ taxes: [] });
      }
      return jsonResponse([]);
    });

    render(
      <MemoryRouter initialEntries={['/org/acme/sales/77/edit']}>
        <Routes>
          <Route path="/org/:orgSlug/sales/:invoiceId/edit" element={<InvoiceEdit isEdit />} />
        </Routes>
      </MemoryRouter>,
    );

    await waitFor(() => {
      expect(document.getElementById('invoice-number-hint')?.textContent).toContain('Next number: #INV-2026-0008');
    });
    expect(screen.getByRole('banner').textContent).toContain('Next number: #INV-2026-0008');
  });
});
