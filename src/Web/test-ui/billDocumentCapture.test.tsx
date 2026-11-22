// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { BillEdit } from '../src/pages/BillEdit';
import { BillDetail } from '../src/pages/BillDetail';
import { DocumentCaptureCapabilities, DocumentCaptureView } from '../src/components/document-capture/fields';

const { authFetch, mockOutletContext, mockHasPermission } = vi.hoisted(() => ({
  authFetch: vi.fn(),
  mockOutletContext: {
    currentOrg: { id: 'ws_1', name: 'Acme', slug: 'acme', currency: 'USD' },
    hasModule: (): boolean => true,
    canWrite: (): boolean => true,
  },
  mockHasPermission: vi.fn(() => true),
}));

vi.mock('../src/auth/AuthContext', () => ({
  useAuth: () => ({ authFetch }),
}));

vi.mock('../src/components/Gating', () => ({
  usePermissions: () => ({ hasPermission: mockHasPermission }),
  ReadOnlyModuleBanner: () => null,
}));

vi.mock('../src/lib/entitlements', () => ({
  useCapabilities: () => ({ canCreate: () => true }),
  useOptionalCapabilities: () => ({ canStatementImport: false }),
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
    useOutletContext: () => mockOutletContext,
  };
});

const mockRevoke = vi.fn();
const mockCreate = vi.fn(() => 'blob:bill-capture-url');

beforeEach(() => {
  global.URL.createObjectURL = mockCreate;
  global.URL.revokeObjectURL = mockRevoke;
  mockOutletContext.canWrite = () => true;
  mockHasPermission.mockReturnValue(true);
});

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

const capsEnabled: DocumentCaptureCapabilities = {
  enabled: true,
  providerAvailable: true,
  kinds: ['expense-receipt', 'purchase-invoice'],
  schemaVersion: 1,
  maxUploadBytes: 8 * 1024 * 1024,
  maxPdfPages: 10,
  maxImageWidth: 8000,
  maxImageHeight: 8000,
  maxImagePixels: 24_000_000,
  allowedMimeTypes: ['image/jpeg', 'image/png', 'application/pdf'],
};

function jsonRes(body: unknown, status = 200) {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: new Headers({ 'content-type': 'application/json' }),
    json: async () => body,
    blob: async () => new Blob(['bytes'], { type: 'image/png' }),
    clone: () => ({ json: async () => body }),
  };
}

function makeCapture(overrides: Partial<DocumentCaptureView> = {}): DocumentCaptureView {
  return {
    id: 'cap_01receipt000000000000001',
    kind: 'expense-receipt',
    schemaVersion: 1,
    status: 'ready',
    contentType: 'image/png',
    byteCount: 512,
    width: 800,
    height: 600,
    pageCount: 1,
    fields: {
      vendorName: 'acme supply',
      vendorInvoiceNo: 'INV-900',
      date: '2026-11-10',
      currency: 'USD',
      lines: [{ description: 'Office supplies', quantity: '1', unitAmount: '110.00' }],
      sourceTotals: { subtotal: '100.00', taxTotal: '10.00', grandTotal: '110.00', priceBasis: 'inclusive' },
      paymentHints: { paymentStatus: 'paid', amountPaid: '110.00', paymentMethod: 'Card' },
    },
    acceptedFields: null,
    warnings: [],
    errorCode: null,
    bytesAvailable: true,
    canAccept: true,
    canApply: false,
    providerName: 'fake',
    providerVersion: '1',
    sourceHash: '1'.repeat(64),
    contentHash: '2'.repeat(64),
    normalizationVersion: 'image-normalization-v2',
    link: null,
    createdAt: '2026-11-10T10:00:00Z',
    acceptedAt: null,
    rejectedAt: null,
    ...overrides,
  };
}

function renderWithRouter(initialEntry: string) {
  const router = createMemoryRouter(
    [
      { path: '/org/:orgSlug/purchases', element: <div>Bills List</div> },
      { path: '/org/:orgSlug/purchases/new', element: <BillEdit /> },
      { path: '/org/:orgSlug/purchases/:billId/edit', element: <BillEdit isEdit /> },
      { path: '/org/:orgSlug/purchases/:billId', element: <BillDetail /> },
    ],
    { initialEntries: [initialEntry] }
  );
  return render(<RouterProvider router={router} />);
}

function setupBillEditMocks(
  captureFactory: () => DocumentCaptureView,
  options?: {
    linkFailStatus?: number;
    existingDraft?: any;
    linkedCaptures?: DocumentCaptureView[];
    saveBillConflictOnce?: any;
  }
) {
  let saveBillCalls = 0;
  let linkCalls = 0;
  let capFetches = 0;
  let lastSaveBody: any = null;

  authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    if (url === '/api/Purchasing/Vendors') {
      return jsonRes([{ id: 'ven_01acme000000000000000001', name: 'Acme Supply', archived: false }]);
    }
    if (url === '/api/Common/PaymentTerms') {
      return jsonRes([{ id: 'term_30', description: 'Net 30', dueAfterDays: 30 }]);
    }
    if (url === '/api/Purchasing/BillAccounts') {
      return jsonRes([{ id: 'acct_50200', code: '50200', name: 'Purchase A/C', group: 'Expense' }]);
    }
    if (url === '/api/Tax/Taxes') {
      return jsonRes({ taxes: [{ id: 'tax_10', taxCode: 'VAT10', taxName: 'VAT 10%', rate: 10, isActive: true }] });
    }
    if (url === '/api/Catalog/Items') return jsonRes([]);
    if (url === '/api/document-captures/capabilities') {
      capFetches++;
      return jsonRes(capsEnabled);
    }
    if (url.startsWith('/api/Purchasing/Bill?id=') && options?.existingDraft) {
      return jsonRes(options.existingDraft);
    }
    if (url.startsWith('/api/document-captures/linked/bill/')) {
      return jsonRes(options?.linkedCaptures ?? []);
    }
    if (url === '/api/document-captures' && init?.method === 'POST') {
      return jsonRes(captureFactory());
    }
    if (url.endsWith('/accept') && init?.method === 'POST') {
      const cap = captureFactory();
      const reqBody = JSON.parse(String(init.body));
      return jsonRes({
        ...cap,
        status: 'accepted',
        fields: null,
        acceptedFields: reqBody.fields,
        canAccept: false,
        canApply: true,
        acceptedAt: '2026-11-10T10:02:00Z',
      });
    }
    if (url === '/api/Purchasing/SaveBill' && init?.method === 'POST') {
      saveBillCalls++;
      lastSaveBody = JSON.parse(String(init.body));
      if (options?.saveBillConflictOnce && saveBillCalls === 1) {
        return jsonRes(options.saveBillConflictOnce, 409);
      }
      return jsonRes({ id: options?.existingDraft?.id ?? 'bill_01saved0000000000000001', status: lastSaveBody.status, total: 110 });
    }
    if (url.endsWith('/links') && init?.method === 'POST') {
      linkCalls++;
      if (options?.linkFailStatus && linkCalls === 1) {
        return jsonRes({ code: options.linkFailStatus === 504 ? 'DOCUMENT_PROVIDER_TIMEOUT' : 'DOCUMENT_BUSY' }, options.linkFailStatus);
      }
      return jsonRes({
        ...captureFactory(),
        link: { targetType: 'bill', targetId: 'bill_01saved0000000000000001', linkedAt: '2026-11-10T12:00:00Z' },
      });
    }
    return jsonRes({}, 404);
  });

  return {
    getSaveBillCalls: () => saveBillCalls,
    getLinkCalls: () => linkCalls,
    getCapFetches: () => capFetches,
    getLastSaveBody: () => lastSaveBody,
  };
}

async function scanAndAccept(fileName = 'receipt.png', type = 'image/png') {
  await waitFor(() => expect(screen.getByRole('button', { name: /Scan receipt \/ bill/i })).toBeTruthy());
  fireEvent.click(screen.getByRole('button', { name: /Scan receipt \/ bill/i }));
  await waitFor(() => expect(screen.getByLabelText('Upload image or PDF')).toBeTruthy());
  fireEvent.change(screen.getByLabelText('Upload image or PDF'), {
    target: { files: [new File(['bytes'], fileName, { type })] },
  });
  await waitFor(() => expect(screen.getByRole('button', { name: /Accept & apply to draft/i })).toBeTruthy());
  fireEvent.click(screen.getByRole('button', { name: /Accept & apply to draft/i }));
}

describe('BillEdit & BillDetail document capture integration', () => {
  it('fetches capabilities only once across BillEdit and DocumentCapturePanel (G10)', async () => {
    const counters = setupBillEditMocks(() => makeCapture());

    renderWithRouter('/org/acme/purchases/new');
    await scanAndAccept();

    await waitFor(() => expect(screen.getByText(/Captured source review/i)).toBeTruthy());
    expect(counters.getCapFetches()).toBe(1);
  });

  it('shows tax-inclusive guidance and requires explicit vendor confirmation', async () => {
    setupBillEditMocks(() => makeCapture());

    renderWithRouter('/org/acme/purchases/new');
    await scanAndAccept();

    await waitFor(() => expect(screen.getByText(/Suggested vendor match from scan/i)).toBeTruthy());
    expect(screen.getByText(/Source amounts include tax/i)).toBeTruthy();

    fireEvent.click(screen.getByRole('button', { name: /Confirm vendor/i }));
    await waitFor(() => expect(screen.queryByText(/Suggested vendor match from scan/i)).toBeNull());
    expect(screen.getByText('Acme Supply')).toBeTruthy();
  });

  it('shows paid-receipt non-settlement notice pointing to existing payment and banking workflows', async () => {
    setupBillEditMocks(() => makeCapture());

    renderWithRouter('/org/acme/purchases/new');
    await scanAndAccept();

    await waitFor(() => expect(screen.getByText(/does not record a payment or settle it/i)).toBeTruthy());
    expect(screen.getByRole('link', { name: /Banking/i })).toBeTruthy();
  });

  it('invalidates discrepancy acknowledgement whenever form amounts change', async () => {
    setupBillEditMocks(() => makeCapture());

    renderWithRouter('/org/acme/purchases/new');
    await scanAndAccept();

    await waitFor(() => expect(screen.getByLabelText('Line 1 tax')).toBeTruthy());
    fireEvent.change(screen.getByLabelText('Line 1 tax'), { target: { value: 'tax_10' } });
    await waitFor(() => expect(screen.getByLabelText(/Acknowledge difference/i)).toBeTruthy());

    const ackCheckbox = screen.getByLabelText(/Acknowledge difference/i) as HTMLInputElement;
    fireEvent.click(ackCheckbox);
    expect(ackCheckbox.checked).toBe(true);

    fireEvent.change(screen.getByLabelText('Line 1 unit cost'), { target: { value: '120' } });
    expect((screen.getByLabelText(/Acknowledge difference/i) as HTMLInputElement).checked).toBe(false);
  });

  it('shows route-leave confirmation while an accepted capture link is pending (G9)', async () => {
    setupBillEditMocks(() => makeCapture(), { linkFailStatus: 504 });

    renderWithRouter('/org/acme/purchases/new');
    await scanAndAccept();
    await waitFor(() => expect(screen.getByRole('button', { name: /Confirm vendor/i })).toBeTruthy());
    fireEvent.click(screen.getByRole('button', { name: /Confirm vendor/i }));

    fireEvent.click(screen.getByRole('button', { name: /Save draft/i }));
    await waitFor(() => expect(screen.getByText(/attaching the source document failed/i)).toBeTruthy());

    fireEvent.click(screen.getByRole('link', { name: /Cancel/i }));
    await waitFor(() => expect(screen.getByText(/Leave without attaching source document/i)).toBeTruthy());
    fireEvent.click(screen.getByRole('button', { name: /^Cancel$/i }));
    await waitFor(() => expect(screen.queryByText(/Leave without attaching source document/i)).toBeNull());
  });

  it('retries only evidence linking after a 504 without repeating SaveBill', async () => {
    const counters = setupBillEditMocks(() => makeCapture(), { linkFailStatus: 504 });

    renderWithRouter('/org/acme/purchases/new');
    await scanAndAccept();
    await waitFor(() => expect(screen.getByRole('button', { name: /Confirm vendor/i })).toBeTruthy());
    fireEvent.click(screen.getByRole('button', { name: /Confirm vendor/i }));

    fireEvent.click(screen.getByRole('button', { name: /Save draft/i }));
    await waitFor(() => expect(screen.getByText(/attaching the source document failed/i)).toBeTruthy());
    expect(counters.getSaveBillCalls()).toBe(1);
    expect(counters.getLinkCalls()).toBe(1);

    fireEvent.click(screen.getByRole('button', { name: /Retry attach evidence/i }));
    await waitFor(() => expect(counters.getLinkCalls()).toBe(2));
    expect(counters.getSaveBillCalls()).toBe(1);
  });

  it('handles tax-exclusive amounts, discount lines, freight/additional charges, and defaulted quantity marker on applied lines', async () => {
    const capture = makeCapture({
      kind: 'purchase-invoice',
      fields: {
        vendorName: 'Acme Supply',
        vendorInvoiceNo: 'SUP-55',
        date: '2026-11-10',
        currency: 'USD',
        lines: [
          { description: 'Printer toner', quantity: null, unitAmount: '100.00', discount: '10', missingQuantityDefaulted: true },
        ],
        sourceTotals: {
          subtotal: '90.00',
          taxTotal: '9.00',
          discountTotal: '10.00',
          additionalCharges: '15.00',
          grandTotal: '114.00',
          priceBasis: 'exclusive',
        },
        paymentHints: { paymentStatus: 'unpaid' },
      },
    });
    setupBillEditMocks(() => capture);

    renderWithRouter('/org/acme/purchases/new');
    await scanAndAccept();

    await waitFor(() => expect(screen.getByText(/Tax-exclusive/i)).toBeTruthy());
    expect(screen.getByText(/Discount: 10.00/i)).toBeTruthy();
    expect(screen.getByText(/Charges: 15.00/i)).toBeTruthy();
    expect(screen.getByText('Defaulted to 1')).toBeTruthy();
    expect((screen.getByLabelText('Line 1 discount percent') as HTMLInputElement).value).toBe('10');
  });

  it('shows unknown price basis and partial payment hint guidance', async () => {
    const capture = makeCapture({
      fields: {
        vendorName: 'Acme Supply',
        date: '2026-11-10',
        currency: 'USD',
        lines: [{ description: 'Catering deposit', quantity: '1', unitAmount: '200.00' }],
        sourceTotals: { grandTotal: '200.00', priceBasis: 'unknown' },
        paymentHints: { paymentStatus: 'partial', amountPaid: '50.00', paymentMethod: 'Card' },
      },
      warnings: ['PRICE_BASIS_UNKNOWN'],
    });
    setupBillEditMocks(() => capture);

    renderWithRouter('/org/acme/purchases/new');
    await scanAndAccept();

    await waitFor(() => expect(screen.getByText(/hint: partial, 50.00/i)).toBeTruthy());
    expect(screen.getByText('Unknown')).toBeTruthy();
  });

  it('blocks foreign currency auto-application and keeps source currency visible after panel closes', async () => {
    const foreignCapture = makeCapture({
      kind: 'purchase-invoice',
      fields: {
        vendorName: 'Euro Vendor',
        vendorInvoiceNo: 'EU-77',
        date: '2026-11-10',
        currency: 'EUR',
        lines: [{ description: 'Euro widget', quantity: '1', unitAmount: '500.00' }],
        sourceTotals: { grandTotal: '500.00', priceBasis: 'exclusive' },
      },
      warnings: ['FOREIGN_CURRENCY'],
    });
    setupBillEditMocks(() => foreignCapture);

    renderWithRouter('/org/acme/purchases/new');
    await scanAndAccept();

    await waitFor(() => expect(screen.getByText(/Monetary amounts cannot be applied automatically/i)).toBeTruthy());
    expect(screen.getByText('EUR')).toBeTruthy();
    expect((screen.getByLabelText('Line 1 unit cost') as HTMLInputElement).value).toBe('0');
  });

  it('blocks credit notes and multiple documents from coercing into bill lines', async () => {
    const creditNoteCapture = makeCapture({
      kind: 'purchase-invoice',
      contentType: 'application/pdf',
      width: null,
      height: null,
      pageCount: 2,
      fields: {
        vendorName: 'Acme Supply',
        vendorInvoiceNo: 'CN-55',
        date: '2026-11-10',
        currency: 'USD',
        lines: [{ description: 'Credit adjustment', quantity: '1', unitAmount: '40.00' }],
        sourceTotals: { grandTotal: '40.00', priceBasis: 'unknown' },
      },
      warnings: ['UNSUPPORTED_DOCUMENT_CLASS', 'MULTIPLE_DOCUMENTS'],
    });
    setupBillEditMocks(() => creditNoteCapture);

    renderWithRouter('/org/acme/purchases/new');
    await scanAndAccept('multi.pdf', 'application/pdf');

    await waitFor(() => expect(screen.getByText(/Go to Supplier Credits/i)).toBeTruthy());
    expect(screen.getByText(/Multiple documents were detected/i)).toBeTruthy();
    expect((screen.getByLabelText("Vendor's bill #") as HTMLInputElement).value).toBe('');
    expect((screen.getByLabelText('Line 1 unit cost') as HTMLInputElement).value).toBe('0');
  });

  it('requires deliberate merge choice before replacing populated draft lines', async () => {
    const capture = makeCapture({
      kind: 'purchase-invoice',
      fields: {
        vendorName: 'Acme Supply',
        date: '2026-11-10',
        currency: 'USD',
        lines: [{ description: 'Scanned line', quantity: '2', unitAmount: '30.00' }],
        sourceTotals: { grandTotal: '60.00', priceBasis: 'exclusive' },
      },
    });
    setupBillEditMocks(() => capture);

    renderWithRouter('/org/acme/purchases/new');
    await waitFor(() => expect(screen.getByLabelText('Line 1 description')).toBeTruthy());
    fireEvent.change(screen.getByLabelText('Line 1 description'), { target: { value: 'Existing manual line' } });
    fireEvent.change(screen.getByLabelText('Line 1 unit cost'), { target: { value: '75' } });

    await scanAndAccept();

    await waitFor(() => expect(screen.getByText(/This draft already has line items/i)).toBeTruthy());
    expect((screen.getByLabelText('Line 1 description') as HTMLInputElement).value).toBe('Existing manual line');

    fireEvent.click(screen.getByRole('button', { name: /Replace existing lines/i }));
    expect((screen.getByLabelText('Line 1 description') as HTMLInputElement).value).toBe('Scanned line');
  });

  it('in edit mode loads linked evidence, preserves form input on 409 conflict, and approves after acknowledging server values (G3, G4)', async () => {
    const linkedCapture = makeCapture({
      id: 'cap_01linked0000000000000001',
      status: 'accepted',
      fields: null,
      acceptedFields: {
        vendorName: 'Acme Supply',
        date: '2026-11-10',
        currency: 'USD',
        sourceTotals: { grandTotal: '1.09', priceBasis: 'exclusive' },
      },
      canAccept: false,
      canApply: true,
      link: { targetType: 'bill', targetId: 'bill_01draft0000000000000001', linkedAt: '2026-11-10T10:00:00Z' },
    });

    const counters = setupBillEditMocks(() => linkedCapture, {
      existingDraft: {
        id: 'bill_01draft0000000000000001',
        vendorId: 'ven_01acme000000000000000001',
        vendorInvoiceNo: 'DRF-1',
        date: '2026-11-10',
        dueDate: '2026-11-10',
        status: 'Draft',
        approved: false,
        lines: [{ id: 'bl_1', description: 'Manual edited line', quantity: 1, amount: 1.09, discount: 0, taxId: '' }],
      },
      linkedCaptures: [linkedCapture],
      saveBillConflictOnce: {
        code: 'DOCUMENT_REVIEW_REQUIRED',
        detail: 'Server total differs.',
        captureId: 'cap_01linked0000000000000001',
        sourceGrandTotal: 1.09,
        formTotal: 1.08,
        difference: -0.01,
      },
    });

    renderWithRouter('/org/acme/purchases/bill_01draft0000000000000001/edit');
    await waitFor(() => expect(screen.getByText(/Captured source review \(cap_01linked0000000000000001\)/i)).toBeTruthy());

    // Save & approve triggers 409 from server with formTotal 1.08 -> form input preserved, server values shown for ack
    fireEvent.click(screen.getByRole('button', { name: /Save & approve/i }));
    await waitFor(() => expect(screen.getByRole('alert').textContent).toContain('Server total differs.'));
    expect((screen.getByLabelText('Line 1 description') as HTMLInputElement).value).toBe('Manual edited line');

    // Acknowledge exact server values and approve again
    fireEvent.click(screen.getByLabelText(/Acknowledge difference for cap_01linked0000000000000001/i));
    fireEvent.click(screen.getByRole('button', { name: /Save & approve/i }));

    await waitFor(() => expect(counters.getSaveBillCalls()).toBe(2));
    expect(counters.getLastSaveBody().captureReviews).toEqual([
      {
        captureId: 'cap_01linked0000000000000001',
        sourceGrandTotal: 1.09,
        formTotal: 1.08,
        discrepancyAcknowledged: true,
      },
    ]);
  });

  it('hides capture controls on read-only bill edit', async () => {
    mockOutletContext.canWrite = () => false;
    setupBillEditMocks(() => makeCapture());

    renderWithRouter('/org/acme/purchases/new');
    await waitFor(() => expect(screen.getByText('New bill')).toBeTruthy());
    expect(screen.queryByRole('button', { name: /Scan receipt \/ bill/i })).toBeNull();
  });

  it('shows linked evidence, hashes, provenance, and revokes preview URL on approved read-only BillDetail', async () => {
    mockOutletContext.canWrite = () => false;

    const evidence = makeCapture({
      id: 'cap_01evid000000000000000001',
      status: 'accepted',
      fields: null,
      acceptedFields: {
        vendorName: 'Acme Supply',
        vendorInvoiceNo: 'RCP-77',
        date: '2026-11-10',
        currency: 'USD',
        sourceTotals: { subtotal: '90.00', taxTotal: '10.00', grandTotal: '100.00', priceBasis: 'exclusive' },
      },
      canAccept: false,
      canApply: false,
      sourceHash: 'abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890',
      contentHash: '1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef',
    });

    authFetch.mockImplementation(async (url: string) => {
      if (url.startsWith('/api/Purchasing/Bill?id=')) {
        return jsonRes({
          id: 'bill_01approved000000000001',
          no: 'B-101',
          vendorId: 'ven_1',
          vendorName: 'Acme Supply',
          vendorInvoiceNo: 'RCP-77',
          date: '2026-11-10',
          dueDate: '2026-11-10',
          lines: [{ id: 'bl_1', description: 'Supplies', quantity: 1, amount: 100, taxAmount: 0, subtotal: 100 }],
          subtotal: 100,
          totalTax: 0,
          total: 100,
          amountPaid: 0,
          balanceDue: 100,
          status: 'Open',
          approved: true,
          payments: [],
        });
      }
      if (url === '/api/Common/PaymentTerms') return jsonRes([]);
      if (url === '/api/document-captures/linked/bill/bill_01approved000000000001') return jsonRes([evidence]);
      if (url === '/api/document-captures/cap_01evid000000000000000001/image') return jsonRes({}, 200);
      return jsonRes({}, 404);
    });

    const rendered = renderWithRouter('/org/acme/purchases/bill_01approved000000000001');
    await waitFor(() => expect(screen.getByText(/Supporting source documents \(1\)/i)).toBeTruthy());
    expect(screen.getByTitle(evidence.sourceHash)).toBeTruthy();
    expect(screen.getByTitle(evidence.contentHash)).toBeTruthy();
    expect(screen.getByText(/fake v1 · image-normalization-v2/i)).toBeTruthy();

    fireEvent.click(screen.getByRole('button', { name: /View image/i }));
    await waitFor(() => expect(screen.getByAltText('Source evidence cap_01evid000000000000000001')).toBeTruthy());
    rendered.unmount();
    expect(mockRevoke).toHaveBeenCalledWith('blob:bill-capture-url');
  });

  it('requires acknowledging all discrepant linked captures before approving a draft on BillDetail (G2)', async () => {
    const cap1 = makeCapture({
      id: 'cap_01multi00000000000000001',
      status: 'accepted',
      fields: null,
      acceptedFields: {
        vendorName: 'Acme Supply',
        date: '2026-11-10',
        currency: 'USD',
        sourceTotals: { grandTotal: '100.00', priceBasis: 'exclusive' },
      },
      canAccept: false,
      canApply: false,
    });
    const cap2 = makeCapture({
      id: 'cap_01multi00000000000000002',
      status: 'accepted',
      fields: null,
      acceptedFields: {
        vendorName: 'Acme Supply',
        date: '2026-11-10',
        currency: 'USD',
        sourceTotals: { grandTotal: '60.00', priceBasis: 'exclusive' },
      },
      canAccept: false,
      canApply: false,
    });

    let lastApproveBody: any = null;
    authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (url.startsWith('/api/Purchasing/Bill?id=')) {
        return jsonRes({
          id: 'bill_01detail00000000000001',
          no: null,
          vendorId: 'ven_1',
          vendorName: 'Acme Supply',
          vendorInvoiceNo: 'RCP-77',
          date: '2026-11-10',
          dueDate: '2026-11-10',
          lines: [{ id: 'bl_1', description: 'Supplies', quantity: 1, amount: 150, taxAmount: 0, subtotal: 150 }],
          subtotal: 150,
          totalTax: 0,
          total: 150,
          amountPaid: 0,
          balanceDue: 150,
          status: 'Draft',
          approved: false,
          payments: [],
        });
      }
      if (url === '/api/Common/PaymentTerms') return jsonRes([]);
      if (url === '/api/document-captures/linked/bill/bill_01detail00000000000001') return jsonRes([cap1, cap2]);
      if (url.startsWith('/api/Purchasing/ApproveBill') && init?.method === 'POST') {
        lastApproveBody = JSON.parse(String(init.body));
        const allAcked = lastApproveBody.captureReviews.every((r: any) => r.discrepancyAcknowledged);
        if (!allAcked) {
          return jsonRes({ code: 'DOCUMENT_REVIEW_REQUIRED', detail: 'Acknowledge all captures.' }, 409);
        }
        return jsonRes({
          id: 'bill_01detail00000000000001',
          no: 'B-101',
          vendorId: 'ven_1',
          vendorName: 'Acme Supply',
          date: '2026-11-10',
          lines: [],
          subtotal: 150,
          totalTax: 0,
          total: 150,
          amountPaid: 0,
          balanceDue: 150,
          status: 'Open',
          approved: true,
          payments: [],
        });
      }
      return jsonRes({}, 404);
    });

    renderWithRouter('/org/acme/purchases/bill_01detail00000000000001');
    await waitFor(() => expect(screen.getByText(/Supporting source documents \(2\)/i)).toBeTruthy());

    // Acknowledge only first capture -> ApproveBill returns 409
    fireEvent.click(screen.getByLabelText(/Acknowledge difference for cap_01multi00000000000000001/i));
    fireEvent.click(screen.getByRole('button', { name: /^Approve$/i }));
    await waitFor(() => expect(screen.getByRole('alert').textContent).toContain('Acknowledge all captures.'));

    // Acknowledge second capture too -> ApproveBill succeeds
    fireEvent.click(screen.getByLabelText(/Acknowledge difference for cap_01multi00000000000000002/i));
    fireEvent.click(screen.getByRole('button', { name: /^Approve$/i }));
    await waitFor(() => expect(screen.getByText(/is approved and counted as owed/i)).toBeTruthy());
    expect(lastApproveBody.captureReviews).toHaveLength(2);
  });

  it('offers Create vendor pre-filled with the captured name when no exact vendor matches and never auto-creates (E4)', async () => {
    setupBillEditMocks(() =>
      makeCapture({
        fields: {
          vendorName: 'Blue Bean Coffee Supply',
          vendorInvoiceNo: 'BB-100',
          date: '2026-11-10',
          currency: 'USD',
          lines: [{ description: 'Coffee beans', quantity: '1', unitAmount: '50.00' }],
          sourceTotals: { subtotal: '50.00', grandTotal: '50.00', priceBasis: 'exclusive' },
        },
      })
    );

    renderWithRouter('/org/acme/purchases/new');
    fireEvent.click(await screen.findByRole('button', { name: /Scan receipt \/ bill/i }));
    fireEvent.change(screen.getByLabelText('Upload image or PDF'), {
      target: { files: [new File(['png'], 'bb.png', { type: 'image/png' })] },
    });
    fireEvent.click(await screen.findByRole('button', { name: /Accept & apply to draft/i }));

    const createBtn = await screen.findByRole('button', { name: /Create vendor “Blue Bean Coffee Supply”/i });
    expect(authFetch.mock.calls.some(([u]) => String(u).includes('Vendor') && String(u).includes('Save'))).toBe(false);

    fireEvent.click(createBtn);
    const nameInput = (await screen.findByDisplayValue('Blue Bean Coffee Supply')) as HTMLInputElement;
    expect(nameInput).toBeTruthy();
    expect(screen.getByRole('heading', { name: /New vendor/i })).toBeTruthy();
  });

  it('hides the reconciliation card and acknowledgement while a pending line merge choice is unresolved (E5)', async () => {
    setupBillEditMocks(() =>
      makeCapture({
        fields: {
          vendorName: 'Acme Supply',
          vendorInvoiceNo: 'INV-99',
          date: '2026-11-10',
          currency: 'USD',
          lines: [{ description: 'Scanned item', quantity: '1', unitAmount: '300.00' }],
          sourceTotals: { subtotal: '300.00', grandTotal: '300.00', priceBasis: 'exclusive' },
        },
      })
    );

    renderWithRouter('/org/acme/purchases/new');
    await waitFor(() => expect(screen.getByLabelText('Line 1 description')).toBeTruthy());
    fireEvent.change(screen.getByLabelText('Line 1 description'), { target: { value: 'Existing line' } });
    fireEvent.change(screen.getByLabelText('Line 1 unit cost'), { target: { value: '75' } });

    await scanAndAccept();

    await screen.findByRole('button', { name: /Replace existing lines/i });
    expect(screen.queryByText(/Captured source review/i)).toBeNull();
    expect(screen.queryByLabelText(/Acknowledge difference for/i)).toBeNull();

    fireEvent.click(screen.getByRole('button', { name: /Keep existing lines/i }));
    expect(await screen.findByText(/Captured source review/i)).toBeTruthy();
  });
});
