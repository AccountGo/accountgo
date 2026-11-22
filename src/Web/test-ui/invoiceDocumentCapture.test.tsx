// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { InvoiceEdit } from '../src/pages/InvoiceEdit';
import { InvoiceDetail } from '../src/pages/InvoiceDetail';
import {
  DocumentCaptureCapabilities,
  DocumentCaptureView,
  validateSalesFieldsForAccept,
} from '../src/components/document-capture/fields';

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
  useAuth: () => ({ authFetch, user: { email: 'owner@acme.test' } }),
}));

vi.mock('../src/components/Gating', () => ({
  usePermissions: () => ({ hasPermission: mockHasPermission }),
  ReadOnlyModuleBanner: () => null,
}));

vi.mock('../src/lib/entitlements', () => ({
  useCapabilities: () => ({ canCreate: () => true }),
  useOptionalCapabilities: () => ({ ready: true, canStatementImport: false }),
}));

vi.mock('../src/lib/useEmailCapabilities', () => ({
  useEmailCapabilities: () => ({ capabilities: null }),
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
const mockCreate = vi.fn(() => 'blob:invoice-capture-url');

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
  kinds: ['sales-invoice'],
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

function makeSalesCapture(overrides: Partial<DocumentCaptureView> = {}): DocumentCaptureView {
  return {
    id: 'cap_01sales00000000000000001',
    kind: 'sales-invoice',
    schemaVersion: 1,
    status: 'ready',
    contentType: 'image/png',
    byteCount: 512,
    width: 800,
    height: 600,
    pageCount: 1,
    fields: {
      customerName: 'northwind traders',
      sourceInvoiceNo: 'EXT-INV-900',
      date: '2026-11-10',
      currency: 'USD',
      lines: [{ description: 'Advisory services', quantity: '1', unitAmount: '110.00' }],
      sourceTotals: { subtotal: '100.00', taxTotal: '10.00', grandTotal: '110.00', priceBasis: 'inclusive' },
    },
    acceptedFields: null,
    warnings: [],
    errorCode: null,
    bytesAvailable: true,
    canAccept: true,
    canApply: false,
    providerName: 'fake',
    providerVersion: '1',
    sourceHash: 'a'.repeat(64),
    contentHash: 'b'.repeat(64),
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
      { path: '/org/:orgSlug/sales', element: <div>Invoices List</div> },
      { path: '/org/:orgSlug/sales/new', element: <InvoiceEdit /> },
      { path: '/org/:orgSlug/sales/:invoiceId/edit', element: <InvoiceEdit isEdit /> },
      { path: '/org/:orgSlug/sales/:invoiceId', element: <InvoiceDetail /> },
    ],
    { initialEntries: [initialEntry] }
  );
  return render(<RouterProvider router={router} />);
}

function setupInvoiceEditMocks(
  captureFactory: () => DocumentCaptureView,
  options?: {
    linkFailStatus?: number;
    existingDraft?: any;
    linkedCaptures?: DocumentCaptureView[];
    saveInvoiceConflictOnce?: any;
  }
) {
  let saveCalls = 0;
  let linkCalls = 0;
  let capFetches = 0;
  let lastSaveBody: any = null;

  authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    if (url === '/api/Sales/Customers') {
      return jsonRes([{ id: 'cus_01north00000000000000001', name: 'Northwind Traders', archived: false }]);
    }
    if (url.startsWith('/api/Sales/Client?id=')) {
      return jsonRes({ id: 'cus_01north00000000000000001', name: 'Northwind Traders', contacts: [] });
    }
    if (url === '/api/Common/PaymentTerms') {
      return jsonRes([{ id: 'term_30', description: 'Net 30', dueAfterDays: 30 }]);
    }
    if (url === '/api/Common/Items') return jsonRes([]);
    if (url === '/api/Tax/Taxes') {
      return jsonRes({ taxes: [{ id: 'tax_10', taxCode: 'VAT10', taxName: 'VAT 10%', rate: 10, isActive: true }] });
    }
    if (url === '/api/Sales/SalesAccounts') return jsonRes([]);
    if (url.startsWith('/api/Sales/NextSalesInvoiceNumber')) return jsonRes({ next: 'INV-0042' });
    if (url === '/api/organizations/current/sales-invoice-settings') return jsonRes({});
    if (url === '/api/document-captures/capabilities') {
      capFetches++;
      return jsonRes(capsEnabled);
    }
    if (url.startsWith('/api/Sales/SalesInvoice?id=') && options?.existingDraft) {
      return jsonRes(options.existingDraft);
    }
    if (url.startsWith('/api/document-captures/linked/invoice/')) {
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
    if (url === '/api/Sales/SaveSalesInvoice' && init?.method === 'POST') {
      saveCalls++;
      lastSaveBody = JSON.parse(String(init.body));
      if (options?.saveInvoiceConflictOnce && saveCalls === 1) {
        return jsonRes(options.saveInvoiceConflictOnce, 409);
      }
      return jsonRes({
        id: options?.existingDraft?.id ?? 'inv_01saved0000000000000001',
        no: lastSaveBody.status === 'Draft' ? null : 'INV-0042',
        status: lastSaveBody.status,
      });
    }
    if (url.endsWith('/links') && init?.method === 'POST') {
      linkCalls++;
      if (options?.linkFailStatus && linkCalls === 1) {
        return jsonRes({ code: 'DOCUMENT_PROVIDER_TIMEOUT' }, options.linkFailStatus);
      }
      return jsonRes({
        ...captureFactory(),
        link: { targetType: 'invoice', targetId: 'inv_01saved0000000000000001', linkedAt: '2026-11-10T12:00:00Z' },
      });
    }
    return jsonRes({}, 404);
  });

  return {
    getSaveCalls: () => saveCalls,
    getLinkCalls: () => linkCalls,
    getCapFetches: () => capFetches,
    getLastSaveBody: () => lastSaveBody,
  };
}

async function scanAndAccept(fileName = 'ref-invoice.png', type = 'image/png') {
  await waitFor(() => expect(screen.getByRole('button', { name: /Scan reference invoice/i })).toBeTruthy());
  fireEvent.click(screen.getByRole('button', { name: /Scan reference invoice/i }));
  await waitFor(() => expect(screen.getByLabelText('Upload image or PDF')).toBeTruthy());
  fireEvent.change(screen.getByLabelText('Upload image or PDF'), {
    target: { files: [new File(['bytes'], fileName, { type })] },
  });
  await waitFor(() => expect(screen.getByRole('button', { name: /Accept & apply to draft/i })).toBeTruthy());
  fireEvent.click(screen.getByRole('button', { name: /Accept & apply to draft/i }));
}

async function confirmUnrecordedSale() {
  await waitFor(() => expect(screen.getByRole('button', { name: /Confirm sale is not already recorded/i })).toBeTruthy());
  fireEvent.click(screen.getByRole('button', { name: /Confirm sale is not already recorded/i }));
}

describe('InvoiceEdit & InvoiceDetail document capture integration (PR 3)', () => {
  it('fetches capabilities only once across InvoiceEdit and DocumentCapturePanel', async () => {
    const counters = setupInvoiceEditMocks(() => makeSalesCapture());

    renderWithRouter('/org/acme/sales/new');
    await scanAndAccept();

    await waitFor(() => expect(screen.getByText(/Confirm new draft from reference document/i)).toBeTruthy());
    expect(counters.getCapFetches()).toBe(1);
    expect(counters.getSaveCalls()).toBe(0);
  });

  it('requires unrecorded-sale confirmation and warns against historical import before applying captured values', async () => {
    const counters = setupInvoiceEditMocks(() => makeSalesCapture());

    renderWithRouter('/org/acme/sales/new');
    await scanAndAccept();

    await waitFor(() => expect(screen.getByText(/not a historical import/i)).toBeTruthy());
    expect(screen.getByText(/duplicate revenue and receivables/i)).toBeTruthy();
    // Nothing applied to form lines until user explicitly confirms
    expect((screen.getByLabelText('Line 1 description') as HTMLInputElement).value).toBe('');
    expect(counters.getSaveCalls()).toBe(0);

    await confirmUnrecordedSale();
    await waitFor(() => expect((screen.getByLabelText('Line 1 description') as HTMLInputElement).value).toBe('Advisory services'));
    expect(counters.getSaveCalls()).toBe(0);
  });

  it('keeps source invoice number only in referenceNo and preserves AccountGo invoice numbering', async () => {
    setupInvoiceEditMocks(() => makeSalesCapture());

    renderWithRouter('/org/acme/sales/new');
    await scanAndAccept();
    await confirmUnrecordedSale();

    await waitFor(() => expect(screen.getByDisplayValue('EXT-INV-900')).toBeTruthy());
    // AccountGo invoice number preview remains intact and invoiceNo input is not overwritten with EXT-INV-900
    expect(screen.getAllByText(/Next number: #INV-0042/i).length).toBeGreaterThan(0);
    expect((screen.getByLabelText('Invoice #') as HTMLInputElement).value).toBe('');
    const refInput = screen.getByDisplayValue('EXT-INV-900') as HTMLInputElement;
    expect(refInput.value).toBe('EXT-INV-900');
  });

  it('suggests exact-match active customer requiring explicit confirmation and never creates a customer', async () => {
    setupInvoiceEditMocks(() => makeSalesCapture());

    renderWithRouter('/org/acme/sales/new');
    await scanAndAccept();
    await confirmUnrecordedSale();

    await waitFor(() => expect(screen.getByText(/Suggested client match from scan/i)).toBeTruthy());
    fireEvent.click(screen.getByRole('button', { name: /Confirm client/i }));
    await waitFor(() => expect(screen.queryByText(/Suggested client match from scan/i)).toBeNull());
    expect(authFetch.mock.calls.some(([u]) => String(u).includes('/api/Sales/SaveClient'))).toBe(false);
  });

  it('blocks foreign currency auto-application and keeps source currency visible after panel closes', async () => {
    setupInvoiceEditMocks(() =>
      makeSalesCapture({
        fields: {
          customerName: 'Northwind Traders',
          sourceInvoiceNo: 'EUR-55',
          date: '2026-11-10',
          currency: 'EUR',
          lines: [{ description: 'Euro consulting', quantity: '1', unitAmount: '500.00' }],
          sourceTotals: { grandTotal: '500.00', priceBasis: 'exclusive' },
        },
        warnings: ['FOREIGN_CURRENCY'],
      })
    );

    renderWithRouter('/org/acme/sales/new');
    await scanAndAccept();
    await confirmUnrecordedSale();

    await waitFor(() => expect(screen.getByText(/Monetary amounts cannot be applied automatically/i)).toBeTruthy());
    expect(screen.getByText('EUR')).toBeTruthy();
    expect((screen.getByLabelText('Line 1 unit price') as HTMLInputElement).value).toBe('0');
  });

  it('blocks credit notes and multiple documents from coercing into sales invoice lines', async () => {
    setupInvoiceEditMocks(() =>
      makeSalesCapture({
        contentType: 'application/pdf',
        width: null,
        height: null,
        pageCount: 2,
        fields: {
          customerName: 'Northwind Traders',
          sourceInvoiceNo: 'CN-77',
          date: '2026-11-10',
          currency: 'USD',
          lines: [{ description: 'Credit adjustment', quantity: '1', unitAmount: '40.00' }],
          sourceTotals: { grandTotal: '40.00', priceBasis: 'unknown' },
        },
        warnings: ['UNSUPPORTED_DOCUMENT_CLASS', 'MULTIPLE_DOCUMENTS'],
      })
    );

    renderWithRouter('/org/acme/sales/new');
    await scanAndAccept('credit.pdf', 'application/pdf');
    await confirmUnrecordedSale();

    await waitFor(() => expect(screen.getByText(/Multiple documents were detected/i)).toBeTruthy());
    expect((screen.getByLabelText('Line 1 unit price') as HTMLInputElement).value).toBe('0');
  });

  it('requires deliberate merge choice before replacing populated draft lines', async () => {
    setupInvoiceEditMocks(() => makeSalesCapture());

    renderWithRouter('/org/acme/sales/new');
    await waitFor(() => expect(screen.getByLabelText('Line 1 description')).toBeTruthy());
    fireEvent.change(screen.getByLabelText('Line 1 description'), { target: { value: 'Existing manual line' } });
    fireEvent.change(screen.getByLabelText('Line 1 unit price'), { target: { value: '75' } });

    await scanAndAccept();
    await confirmUnrecordedSale();

    await waitFor(() => expect(screen.getByText(/This draft already has line items/i)).toBeTruthy());
    expect((screen.getByLabelText('Line 1 description') as HTMLInputElement).value).toBe('Existing manual line');

    fireEvent.click(screen.getByRole('button', { name: /Replace existing lines/i }));
    expect((screen.getByLabelText('Line 1 description') as HTMLInputElement).value).toBe('Advisory services');
  });

  it('invalidates discrepancy acknowledgement whenever form amounts change', async () => {
    setupInvoiceEditMocks(() => makeSalesCapture());

    renderWithRouter('/org/acme/sales/new');
    await scanAndAccept();
    await confirmUnrecordedSale();

    await waitFor(() => expect(screen.getByLabelText('Line 1 tax')).toBeTruthy());
    fireEvent.change(screen.getByLabelText('Line 1 tax'), { target: { value: 'tax_10' } });
    await waitFor(() => expect(screen.getByLabelText(/Acknowledge difference/i)).toBeTruthy());

    const ackCheckbox = screen.getByLabelText(/Acknowledge difference/i) as HTMLInputElement;
    fireEvent.click(ackCheckbox);
    expect(ackCheckbox.checked).toBe(true);

    fireEvent.change(screen.getByLabelText('Line 1 unit price'), { target: { value: '120' } });
    expect((screen.getByLabelText(/Acknowledge difference/i) as HTMLInputElement).checked).toBe(false);
  });

  it('shows route-leave confirmation while an accepted capture link is pending', async () => {
    setupInvoiceEditMocks(() => makeSalesCapture(), { linkFailStatus: 504 });

    renderWithRouter('/org/acme/sales/new');
    await scanAndAccept();
    await confirmUnrecordedSale();
    await waitFor(() => expect(screen.getByRole('button', { name: /Confirm client/i })).toBeTruthy());
    fireEvent.click(screen.getByRole('button', { name: /Confirm client/i }));

    fireEvent.click(screen.getByRole('button', { name: /Save draft/i }));
    await waitFor(() => expect(screen.getByText(/attaching the source document failed/i)).toBeTruthy());

    fireEvent.click(screen.getByRole('link', { name: /Cancel/i }));
    await waitFor(() => expect(screen.getByText(/Leave without attaching source document/i)).toBeTruthy());
    fireEvent.click(screen.getByRole('button', { name: /^Cancel$/i }));
    await waitFor(() => expect(screen.queryByText(/Leave without attaching source document/i)).toBeNull());
  });

  it('retries only evidence linking after a 504 without repeating SaveSalesInvoice', async () => {
    const counters = setupInvoiceEditMocks(() => makeSalesCapture(), { linkFailStatus: 504 });

    renderWithRouter('/org/acme/sales/new');
    await scanAndAccept();
    await confirmUnrecordedSale();
    await waitFor(() => expect(screen.getByRole('button', { name: /Confirm client/i })).toBeTruthy());
    fireEvent.click(screen.getByRole('button', { name: /Confirm client/i }));

    fireEvent.click(screen.getByRole('button', { name: /Save draft/i }));
    await waitFor(() => expect(screen.getByText(/attaching the source document failed/i)).toBeTruthy());
    expect(counters.getSaveCalls()).toBe(1);
    expect(counters.getLinkCalls()).toBe(1);

    fireEvent.click(screen.getByRole('button', { name: /Retry attach evidence/i }));
    await waitFor(() => expect(counters.getLinkCalls()).toBe(2));
    expect(counters.getSaveCalls()).toBe(1);
  });

  it('in edit mode loads linked evidence, preserves input on 409 conflict, and issues after acknowledging server values', async () => {
    const linkedCap = makeSalesCapture({
      id: 'cap_01linkedinv0000000000001',
      status: 'accepted',
      fields: null,
      acceptedFields: {
        customerName: 'Northwind Traders',
        sourceInvoiceNo: 'EXT-1',
        date: '2026-11-10',
        currency: 'USD',
        sourceTotals: { grandTotal: '1.09', priceBasis: 'exclusive' },
      },
      canAccept: false,
      canApply: true,
      link: { targetType: 'invoice', targetId: 'inv_01draft0000000000000001', linkedAt: '2026-11-10T10:00:00Z' },
    });

    const counters = setupInvoiceEditMocks(() => linkedCap, {
      existingDraft: {
        id: 'inv_01draft0000000000000001',
        customerId: 'cus_01north00000000000000001',
        no: null,
        invoiceDate: '2026-11-10',
        dueDate: '2026-11-10',
        status: 'Draft',
        salesInvoiceLines: [{ id: 'il_1', itemDescription: 'Manual draft line', quantity: 1, amount: 1.09, discount: 0, taxId: '' }],
      },
      linkedCaptures: [linkedCap],
      saveInvoiceConflictOnce: {
        code: 'DOCUMENT_REVIEW_REQUIRED',
        detail: 'Server invoice total differs.',
        captureId: 'cap_01linkedinv0000000000001',
        sourceGrandTotal: 1.09,
        formTotal: 1.08,
        difference: -0.01,
      },
    });

    renderWithRouter('/org/acme/sales/inv_01draft0000000000000001/edit');
    await waitFor(() => expect(screen.getByText(/Captured source review \(cap_01linkedinv0000000000001\)/i)).toBeTruthy());

    fireEvent.click(screen.getByRole('button', { name: /Save & issue/i }));
    await waitFor(() => expect(screen.getByText(/Server invoice total differs/i)).toBeTruthy());
    expect((screen.getByLabelText('Line 1 description') as HTMLInputElement).value).toBe('Manual draft line');

    fireEvent.click(screen.getByLabelText(/Acknowledge difference for cap_01linkedinv0000000000001/i));
    fireEvent.click(screen.getByRole('button', { name: /Save & issue/i }));

    await waitFor(() => expect(counters.getSaveCalls()).toBe(2));
    expect(counters.getLastSaveBody().captureReviews).toEqual([
      {
        captureId: 'cap_01linkedinv0000000000001',
        sourceGrandTotal: 1.09,
        formTotal: 1.08,
        discrepancyAcknowledged: true,
      },
    ]);
  });

  it('hides capture controls on read-only InvoiceEdit', async () => {
    mockOutletContext.canWrite = () => false;
    setupInvoiceEditMocks(() => makeSalesCapture());

    renderWithRouter('/org/acme/sales/new');
    await waitFor(() => expect(screen.getByText('New invoice')).toBeTruthy());
    expect(screen.queryByRole('button', { name: /Scan reference invoice/i })).toBeNull();
  });

  it('shows linked evidence, source invoice #, hashes, provenance, and revokes preview URL on issued read-only InvoiceDetail', async () => {
    mockOutletContext.canWrite = () => false;
    const evidence = makeSalesCapture({
      id: 'cap_01invevid0000000000001',
      status: 'accepted',
      fields: null,
      acceptedFields: {
        customerName: 'Northwind Traders',
        sourceInvoiceNo: 'EXT-REF-55',
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
      if (url.startsWith('/api/Sales/SalesInvoice?id=')) {
        return jsonRes({
          id: 'inv_01issued00000000000001',
          no: 'INV-0042',
          customerId: 'cus_1',
          customerName: 'Northwind Traders',
          invoiceDate: '2026-11-10',
          dueDate: '2026-11-10',
          referenceNo: 'EXT-REF-55',
          salesInvoiceLines: [{ id: 'il_1', itemDescription: 'Advisory', quantity: 1, amount: 100, discount: 0, taxAmount: 0, lineTotal: 100 }],
          amount: 100,
          totalTax: 0,
          totalAmountAfterTax: 100,
          paidAmount: 0,
          balanceDue: 100,
          status: 'Open',
          payments: [],
          emails: [],
          seller: { name: 'Acme', addressLines: [] },
          display: { showCustomerEmail: true, showAttention: true, showNotes: true, showPaymentInstructions: true },
        });
      }
      if (url === '/api/document-captures/linked/invoice/inv_01issued00000000000001') return jsonRes([evidence]);
      if (url === '/api/document-captures/cap_01invevid0000000000001/image') return jsonRes({}, 200);
      return jsonRes({}, 404);
    });

    const rendered = renderWithRouter('/org/acme/sales/inv_01issued00000000000001');
    await waitFor(() => expect(screen.getByText(/Supporting source documents \(1\)/i)).toBeTruthy());
    expect(screen.getByTitle(evidence.sourceHash)).toBeTruthy();
    expect(screen.getByTitle(evidence.contentHash)).toBeTruthy();
    expect(screen.getByText(/fake v1 · image-normalization-v2/i)).toBeTruthy();

    fireEvent.click(screen.getByRole('button', { name: /View image/i }));
    await waitFor(() => expect(screen.getByAltText('Source evidence cap_01invevid0000000000001')).toBeTruthy());
    rendered.unmount();
    expect(mockRevoke).toHaveBeenCalledWith('blob:invoice-capture-url');
  });

  it('requires acknowledging all discrepant linked captures before issuing a draft on InvoiceDetail', async () => {
    const cap1 = makeSalesCapture({
      id: 'cap_01invmulti000000000001',
      status: 'accepted',
      fields: null,
      acceptedFields: { customerName: 'Northwind Traders', date: '2026-11-10', currency: 'USD', sourceTotals: { grandTotal: '100.00', priceBasis: 'exclusive' } },
      canAccept: false,
      canApply: false,
    });
    const cap2 = makeSalesCapture({
      id: 'cap_01invmulti000000000002',
      status: 'accepted',
      fields: null,
      acceptedFields: { customerName: 'Northwind Traders', date: '2026-11-10', currency: 'USD', sourceTotals: { grandTotal: '60.00', priceBasis: 'exclusive' } },
      canAccept: false,
      canApply: false,
    });

    let lastIssueBody: any = null;
    authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (url.startsWith('/api/Sales/SalesInvoice?id=')) {
        return jsonRes({
          id: 'inv_01draftdetail000000001',
          no: null,
          customerId: 'cus_1',
          customerName: 'Northwind Traders',
          invoiceDate: '2026-11-10',
          dueDate: '2026-11-10',
          salesInvoiceLines: [{ id: 'il_1', itemDescription: 'Advisory', quantity: 1, amount: 150, discount: 0, taxAmount: 0, lineTotal: 150 }],
          amount: 150,
          totalTax: 0,
          totalAmountAfterTax: 150,
          paidAmount: 0,
          balanceDue: 150,
          status: 'Draft',
          payments: [],
          emails: [],
          seller: { name: 'Acme', addressLines: [] },
          display: { showCustomerEmail: true, showAttention: true, showNotes: true, showPaymentInstructions: true },
        });
      }
      if (url.startsWith('/api/Sales/NextSalesInvoiceNumber')) return jsonRes({ next: 'INV-0042' });
      if (url === '/api/document-captures/linked/invoice/inv_01draftdetail000000001') return jsonRes([cap1, cap2]);
      if (url.startsWith('/api/Sales/IssueSalesInvoice') && init?.method === 'POST') {
        lastIssueBody = JSON.parse(String(init.body));
        const allAcked = lastIssueBody.captureReviews.every((r: any) => r.discrepancyAcknowledged);
        if (!allAcked) {
          return jsonRes({ code: 'DOCUMENT_REVIEW_REQUIRED', detail: 'Acknowledge all invoice captures.' }, 409);
        }
        return jsonRes({ success: true, invoiceId: 'inv_01draftdetail000000001', no: 'INV-0042', status: 'Open', warnings: [] });
      }
      return jsonRes({}, 404);
    });

    renderWithRouter('/org/acme/sales/inv_01draftdetail000000001');
    await waitFor(() => expect(screen.getByText(/Supporting source documents \(2\)/i)).toBeTruthy());

    // Acknowledge only first capture -> IssueSalesInvoice returns 409
    fireEvent.click(screen.getByLabelText(/Acknowledge difference for cap_01invmulti000000000001/i));
    fireEvent.click(screen.getByRole('button', { name: /Issue invoice/i }));
    await waitFor(() => expect(screen.getByText(/Acknowledge all invoice captures/i)).toBeTruthy());

    // Acknowledge second capture too -> IssueSalesInvoice succeeds
    fireEvent.click(screen.getByLabelText(/Acknowledge difference for cap_01invmulti000000000002/i));
    fireEvent.click(screen.getByRole('button', { name: /Issue invoice/i }));
    await waitFor(() => expect(screen.getByText(/Invoice #INV-0042 has been issued/i)).toBeTruthy());
    expect(lastIssueBody.captureReviews).toHaveLength(2);
  });

  it('validates sales-invoice fields strictly against DocumentCaptureFieldSchemas limits', () => {
    const invalid = validateSalesFieldsForAccept(
      {
        customerName: 'Northwind',
        sourceInvoiceNo: 'S'.repeat(201),
        date: '11/10/2026',
        dueDate: '',
        referenceNo: 'R'.repeat(201),
        currency: 'USD',
        notes: '',
      },
      [
        {
          id: 1,
          description: '',
          quantity: '0',
          unitAmount: '1,234.56',
          sourceLineTotal: '',
          taxHint: '',
          discount: '',
          priceBasis: '',
          missingQuantityDefaulted: false,
        },
      ]
    );

    expect(invalid.valid).toBe(false);
    expect(invalid.errors.some((e) => e.includes('YYYY-MM-DD'))).toBe(true);
    expect(invalid.errors.some((e) => e.includes('Source invoice # can be at most 200'))).toBe(true);
    expect(invalid.errors.some((e) => e.includes('Reference can be at most 200'))).toBe(true);
    expect(invalid.errors.some((e) => e.includes('no description'))).toBe(true);
    expect(invalid.errors.some((e) => e.includes('greater than zero'))).toBe(true);
    expect(invalid.errors.some((e) => e.includes('plain decimal number'))).toBe(true);
  });

  it('offers Create client pre-filled with the captured name when no exact client matches and never auto-creates (E4)', async () => {
    setupInvoiceEditMocks(() =>
      makeSalesCapture({
        fields: {
          customerName: 'New Horizon LLC',
          sourceInvoiceNo: 'NH-55',
          date: '2026-11-10',
          currency: 'USD',
          lines: [{ description: 'Advisory', quantity: '1', unitAmount: '100.00' }],
          sourceTotals: { subtotal: '100.00', grandTotal: '100.00', priceBasis: 'exclusive' },
        },
      })
    );

    renderWithRouter('/org/acme/sales/new');
    await scanAndAccept();
    await confirmUnrecordedSale();

    const createBtn = await screen.findByRole('button', { name: /Create client “New Horizon LLC”/i });
    expect(authFetch.mock.calls.some(([u]) => String(u).includes('Client') && String(u).includes('Save'))).toBe(false);

    fireEvent.click(createBtn);
    const nameInput = (await screen.findByDisplayValue('New Horizon LLC')) as HTMLInputElement;
    expect(nameInput).toBeTruthy();
    expect(screen.getByRole('heading', { name: /New client/i })).toBeTruthy();
  });
});
