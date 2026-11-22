// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
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

function setupBillEditMocks(captureFactory: () => DocumentCaptureView, options?: { linkFailsFirst?: boolean; existingDraft?: any }) {
  let saveBillCalls = 0;
  let linkCalls = 0;

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
    if (url === '/api/document-captures/capabilities') return jsonRes(capsEnabled);
    if (url.startsWith('/api/Purchasing/Bill?id=') && options?.existingDraft) {
      return jsonRes(options.existingDraft);
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
      });
    }
    if (url === '/api/Purchasing/SaveBill' && init?.method === 'POST') {
      saveBillCalls++;
      return jsonRes({ id: 'bill_01saved0000000000000001', status: 'Draft', total: 110 });
    }
    if (url.endsWith('/links') && init?.method === 'POST') {
      linkCalls++;
      if (options?.linkFailsFirst && linkCalls === 1) {
        return jsonRes({ code: 'DOCUMENT_BUSY' }, 503);
      }
      return jsonRes({ ...captureFactory(), link: { targetType: 'bill', targetId: 'bill_01saved0000000000000001', linkedAt: '2026-11-10T12:00:00Z' } });
    }
    return jsonRes({}, 404);
  });

  return { getSaveBillCalls: () => saveBillCalls, getLinkCalls: () => linkCalls };
}

describe('BillEdit & BillDetail document capture integration', () => {
  it('shows tax-inclusive warning, paid-receipt notice, vendor confirmation, invalidates discrepancy ack on edit, and retries link without repeating SaveBill', async () => {
    const capture: DocumentCaptureView = {
      id: 'cap_01receipt000000000000001',
      kind: 'expense-receipt',
      schemaVersion: 1,
      status: 'ready',
      contentType: 'image/png',
      byteCount: 512,
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
    };

    const counters = setupBillEditMocks(() => capture, { linkFailsFirst: true });

    render(
      <MemoryRouter initialEntries={['/org/acme/purchases/new']}>
        <Routes>
          <Route path="/org/:orgSlug/purchases/new" element={<BillEdit />} />
          <Route path="/org/:orgSlug/purchases/:billId" element={<div>Saved Bill Page</div>} />
        </Routes>
      </MemoryRouter>
    );

    await waitFor(() => expect(screen.getByRole('button', { name: /Scan receipt \/ bill/i })).toBeTruthy());
    fireEvent.click(screen.getByRole('button', { name: /Scan receipt \/ bill/i }));

    await waitFor(() => expect(screen.getByLabelText('Upload image or PDF')).toBeTruthy());
    fireEvent.change(screen.getByLabelText('Upload image or PDF'), {
      target: { files: [new File(['png'], 'receipt.png', { type: 'image/png' })] },
    });

    await waitFor(() => expect(screen.getByRole('button', { name: /Accept & apply to draft/i })).toBeTruthy());
    fireEvent.click(screen.getByRole('button', { name: /Accept & apply to draft/i }));

    // Vendor suggestion requires explicit confirmation
    await waitFor(() => expect(screen.getByText(/Suggested vendor match from scan/i)).toBeTruthy());
    expect(screen.getByText(/Source amounts include tax/i)).toBeTruthy();
    expect(screen.getByText(/does not record a payment or settle it/i)).toBeTruthy();

    fireEvent.click(screen.getByRole('button', { name: /Confirm vendor/i }));

    // Add 10% tax on 110 -> form total becomes 121 (discrepancy of +11 vs source 110)
    fireEvent.change(screen.getByLabelText('Line 1 tax'), { target: { value: 'tax_10' } });
    await waitFor(() => expect(screen.getByRole('checkbox')).toBeTruthy());

    // Check acknowledgement, then edit amount -> acknowledgement must reset!
    const ackCheckbox = screen.getByRole('checkbox') as HTMLInputElement;
    fireEvent.click(ackCheckbox);
    expect(ackCheckbox.checked).toBe(true);

    fireEvent.change(screen.getByLabelText('Line 1 unit cost'), { target: { value: '120' } });
    expect((screen.getByRole('checkbox') as HTMLInputElement).checked).toBe(false);

    // Draft save succeeds even with unresolved discrepancy; link fails once and retries ONLY link
    fireEvent.click(screen.getByRole('button', { name: /Save draft/i }));

    await waitFor(() => expect(screen.getByText(/attaching the source document failed/i)).toBeTruthy());
    expect(counters.getSaveBillCalls()).toBe(1);
    expect(counters.getLinkCalls()).toBe(1);

    fireEvent.click(screen.getByRole('button', { name: /Retry attach evidence/i }));
    await waitFor(() => expect(screen.getByText('Saved Bill Page')).toBeTruthy());
    // SaveBill was NEVER repeated!
    expect(counters.getSaveBillCalls()).toBe(1);
    expect(counters.getLinkCalls()).toBe(2);
  });

  it('blocks foreign currency auto-application, blocks unsupported credit-note class, and prompts merge choice on populated draft', async () => {
    const foreignCapture: DocumentCaptureView = {
      id: 'cap_01foreign000000000000001',
      kind: 'purchase-invoice',
      schemaVersion: 1,
      status: 'ready',
      contentType: 'image/png',
      byteCount: 512,
      pageCount: 1,
      fields: {
        vendorName: 'Euro Vendor',
        vendorInvoiceNo: 'EU-77',
        date: '2026-11-10',
        currency: 'EUR',
        lines: [{ description: 'Euro widget', quantity: '1', unitAmount: '500.00' }],
        sourceTotals: { grandTotal: '500.00', priceBasis: 'exclusive' },
      },
      acceptedFields: null,
      warnings: ['FOREIGN_CURRENCY'],
      errorCode: null,
      bytesAvailable: true,
      canAccept: true,
      canApply: false,
      providerName: 'fake',
      providerVersion: '1',
      sourceHash: '3'.repeat(64),
      contentHash: '4'.repeat(64),
      normalizationVersion: 'image-normalization-v2',
      link: null,
      createdAt: '2026-11-10T10:00:00Z',
    };

    setupBillEditMocks(() => foreignCapture);

    render(
      <MemoryRouter initialEntries={['/org/acme/purchases/new']}>
        <Routes>
          <Route path="/org/:orgSlug/purchases/new" element={<BillEdit />} />
        </Routes>
      </MemoryRouter>
    );

    await waitFor(() => expect(screen.getByRole('button', { name: /Scan receipt \/ bill/i })).toBeTruthy());
    fireEvent.click(screen.getByRole('button', { name: /Scan receipt \/ bill/i }));
    await waitFor(() => expect(screen.getByLabelText('Upload image or PDF')).toBeTruthy());
    fireEvent.change(screen.getByLabelText('Upload image or PDF'), {
      target: { files: [new File(['eu'], 'eu.png', { type: 'image/png' })] },
    });
    await waitFor(() => expect(screen.getByRole('button', { name: /Accept & apply to draft/i })).toBeTruthy());
    fireEvent.click(screen.getByRole('button', { name: /Accept & apply to draft/i }));

    await waitFor(() => expect(screen.getByText(/Monetary amounts cannot be applied automatically/i)).toBeTruthy());
    // Source currency remains visible after panel closes, and unit cost stays 0 (not 500)
    expect(screen.getByText('EUR')).toBeTruthy();
    expect((screen.getByLabelText('Line 1 unit cost') as HTMLInputElement).value).toBe('0');
  });

  it('requires deliberate merge choice before replacing populated draft lines', async () => {
    const capture: DocumentCaptureView = {
      id: 'cap_01merge00000000000000001',
      kind: 'purchase-invoice',
      schemaVersion: 1,
      status: 'ready',
      contentType: 'image/png',
      byteCount: 200,
      pageCount: 1,
      fields: {
        vendorName: 'Acme Supply',
        date: '2026-11-10',
        currency: 'USD',
        lines: [{ description: 'Scanned line', quantity: '2', unitAmount: '30.00' }],
        sourceTotals: { grandTotal: '60.00', priceBasis: 'exclusive' },
      },
      acceptedFields: null,
      warnings: [],
      errorCode: null,
      bytesAvailable: true,
      canAccept: true,
      canApply: false,
      providerName: 'fake',
      providerVersion: '1',
      sourceHash: '5'.repeat(64),
      contentHash: '6'.repeat(64),
      normalizationVersion: 'image-normalization-v2',
      link: null,
      createdAt: '2026-11-10T10:00:00Z',
    };

    setupBillEditMocks(() => capture);

    render(
      <MemoryRouter initialEntries={['/org/acme/purchases/new']}>
        <Routes>
          <Route path="/org/:orgSlug/purchases/new" element={<BillEdit />} />
        </Routes>
      </MemoryRouter>
    );

    await waitFor(() => expect(screen.getByLabelText('Line 1 description')).toBeTruthy());
    // Populate existing line first
    fireEvent.change(screen.getByLabelText('Line 1 description'), { target: { value: 'Existing manual line' } });
    fireEvent.change(screen.getByLabelText('Line 1 unit cost'), { target: { value: '75' } });

    fireEvent.click(screen.getByRole('button', { name: /Scan receipt \/ bill/i }));
    await waitFor(() => expect(screen.getByLabelText('Upload image or PDF')).toBeTruthy());
    fireEvent.change(screen.getByLabelText('Upload image or PDF'), {
      target: { files: [new File(['m'], 'm.png', { type: 'image/png' })] },
    });
    await waitFor(() => expect(screen.getByRole('button', { name: /Accept & apply to draft/i })).toBeTruthy());
    fireEvent.click(screen.getByRole('button', { name: /Accept & apply to draft/i }));

    // Existing line is NOT overwritten until user chooses
    await waitFor(() => expect(screen.getByText(/This draft already has line items/i)).toBeTruthy());
    expect((screen.getByLabelText('Line 1 description') as HTMLInputElement).value).toBe('Existing manual line');

    fireEvent.click(screen.getByRole('button', { name: /Replace existing lines/i }));
    expect((screen.getByLabelText('Line 1 description') as HTMLInputElement).value).toBe('Scanned line');
  });

  it('displays linked supporting evidence, hashes, provenance, canonical fields, and source media on BillDetail even when write access is disabled', async () => {
    mockOutletContext.canWrite = () => false;

    const linkedEvidence: DocumentCaptureView = {
      id: 'cap_01evid000000000000000001',
      kind: 'expense-receipt',
      schemaVersion: 1,
      status: 'accepted',
      contentType: 'image/png',
      byteCount: 1024,
      pageCount: 1,
      fields: null,
      acceptedFields: {
        vendorName: 'Acme Supply',
        vendorInvoiceNo: 'RCP-77',
        date: '2026-11-10',
        currency: 'USD',
        sourceTotals: { subtotal: '100.00', taxTotal: '10.00', grandTotal: '110.00', priceBasis: 'exclusive' },
        paymentHints: { paymentStatus: 'paid', amountPaid: '110.00' },
      },
      warnings: [],
      errorCode: null,
      bytesAvailable: true,
      canAccept: false,
      canApply: false,
      providerName: 'fake',
      providerVersion: '1',
      sourceHash: 'abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890',
      contentHash: '1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef',
      normalizationVersion: 'image-normalization-v2',
      link: { targetType: 'bill', targetId: 'bill_01detail00000000000001', linkedAt: '2026-11-10T10:05:00Z' },
      createdAt: '2026-11-10T10:00:00Z',
    };

    authFetch.mockImplementation(async (url: string) => {
      if (url.startsWith('/api/Purchasing/Bill?id=')) {
        return jsonRes({
          id: 'bill_01detail00000000000001',
          no: 'B-101',
          vendorId: 'ven_1',
          vendorName: 'Acme Supply',
          vendorInvoiceNo: 'RCP-77',
          date: '2026-11-10',
          dueDate: '2026-11-10',
          lines: [{ id: 'bl_1', description: 'Supplies', quantity: 1, amount: 100, taxAmount: 10, subtotal: 100 }],
          subtotal: 100,
          totalTax: 10,
          total: 110,
          amountPaid: 0,
          balanceDue: 110,
          status: 'Open',
          approved: true,
          payments: [],
        });
      }
      if (url === '/api/Common/PaymentTerms') return jsonRes([]);
      if (url === '/api/document-captures/linked/bill/bill_01detail00000000000001') {
        return jsonRes([linkedEvidence]);
      }
      if (url === '/api/document-captures/cap_01evid000000000000000001/image') {
        return jsonRes({}, 200);
      }
      return jsonRes({}, 404);
    });

    const { unmount } = render(
      <MemoryRouter initialEntries={['/org/acme/purchases/bill_01detail00000000000001']}>
        <Routes>
          <Route path="/org/:orgSlug/purchases/:billId" element={<BillDetail />} />
        </Routes>
      </MemoryRouter>
    );

    await waitFor(() => expect(screen.getByText(/Supporting source documents \(1\)/i)).toBeTruthy());
    expect(screen.getByText('cap_01evid000000000000000001')).toBeTruthy();
    expect(screen.getByText(/fake v1 · image-normalization-v2/i)).toBeTruthy();
    expect(screen.getByTitle(linkedEvidence.sourceHash)).toBeTruthy();
    expect(screen.getByTitle(linkedEvidence.contentHash)).toBeTruthy();

    fireEvent.click(screen.getByRole('button', { name: /View image/i }));
    await waitFor(() => expect(screen.getByAltText('Source evidence cap_01evid000000000000000001')).toBeTruthy());

    unmount();
    expect(mockRevoke).toHaveBeenCalledWith('blob:bill-capture-url');
  });

  it('blocks credit notes and multiple documents from coercing into bill lines, and keeps payment terms intact until user confirms', async () => {
    const creditNoteCapture: DocumentCaptureView = {
      id: 'cap_01credit0000000000000001',
      kind: 'purchase-invoice',
      schemaVersion: 1,
      status: 'ready',
      contentType: 'application/pdf',
      byteCount: 400,
      pageCount: 2,
      detectedKind: 'credit-note',
      detectedDocumentCount: 2,
      fields: {
        vendorName: 'Acme Supply',
        vendorInvoiceNo: 'CN-55',
        date: '2026-11-10',
        currency: 'USD',
        lines: [{ description: 'Credit adjustment', quantity: '1', unitAmount: '40.00' }],
        sourceTotals: { grandTotal: '40.00', priceBasis: 'unknown' },
      },
      acceptedFields: null,
      warnings: ['UNSUPPORTED_DOCUMENT_CLASS', 'MULTIPLE_DOCUMENTS', 'PRICE_BASIS_UNKNOWN'],
      errorCode: null,
      bytesAvailable: true,
      canAccept: true,
      canApply: false,
      providerName: 'fake',
      providerVersion: '1',
      sourceHash: '7'.repeat(64),
      contentHash: '8'.repeat(64),
      normalizationVersion: 'pdf-structure-v1',
      link: null,
      createdAt: '2026-11-10T10:00:00Z',
    };

    setupBillEditMocks(() => creditNoteCapture);

    render(
      <MemoryRouter initialEntries={['/org/acme/purchases/new']}>
        <Routes>
          <Route path="/org/:orgSlug/purchases/new" element={<BillEdit />} />
        </Routes>
      </MemoryRouter>
    );

    await waitFor(() => expect(screen.getByRole('button', { name: /Scan receipt \/ bill/i })).toBeTruthy());
    fireEvent.click(screen.getByRole('button', { name: /Scan receipt \/ bill/i }));
    await waitFor(() => expect(screen.getByLabelText('Upload image or PDF')).toBeTruthy());
    fireEvent.change(screen.getByLabelText('Upload image or PDF'), {
      target: { files: [new File(['%PDF-1.4'], 'multi.pdf', { type: 'application/pdf' })] },
    });
    await waitFor(() => expect(screen.getByRole('button', { name: /Accept & apply to draft/i })).toBeTruthy());
    fireEvent.click(screen.getByRole('button', { name: /Accept & apply to draft/i }));

    await waitFor(() => expect(screen.getByText(/Go to Supplier Credits/i)).toBeTruthy());
    expect(screen.getByText(/Multiple documents were detected/i)).toBeTruthy();
    // Neither vendorInvoiceNo nor lines were coerced into the bill
    expect((screen.getByLabelText("Vendor's bill #") as HTMLInputElement).value).toBe('');
    expect((screen.getByLabelText('Line 1 unit cost') as HTMLInputElement).value).toBe('0');
  });
});
