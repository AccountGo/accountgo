// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { DocumentCapturePanel } from '../src/components/document-capture/DocumentCapturePanel';
import {
  computeCaptureDiscrepancy,
  describeCaptureError,
  describeLinkedCaptureTarget,
  DocumentCaptureCapabilities,
  DocumentCaptureView,
  formatSignedCurrencyDifference,
  parseStrictDecimal,
  validateVendorFieldsForAccept,
} from '../src/components/document-capture/fields';
import { ENTITLEMENT_DENIED_EVENT } from '../src/lib/entitlementEvents';

const { authFetch } = vi.hoisted(() => ({
  authFetch: vi.fn(),
}));

vi.mock('../src/auth/AuthContext', () => ({
  useAuth: () => ({ authFetch }),
}));

const mockRevoke = vi.fn();
let urlCounter = 0;
const mockCreate = vi.fn(() => `blob:mock-capture-${++urlCounter}`);

beforeEach(() => {
  urlCounter = 0;
  global.URL.createObjectURL = mockCreate;
  global.URL.revokeObjectURL = mockRevoke;
});

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

const enabledCapabilities: DocumentCaptureCapabilities = {
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

function makeJsonResponse(body: unknown, status = 200, headers: Record<string, string> = {}) {
  const headerMap = new Headers({ 'content-type': 'application/json', ...headers });
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: headerMap,
    json: async () => body,
    clone: () => ({ json: async () => body }),
  };
}

/**
 * Exact wire shape returned by DocumentCaptureService.ToView with FakeDocumentFieldExtractor
 * (25 fields, zero client-invented properties).
 */
function makeFakeExtractorReadyCapture(overrides: Partial<DocumentCaptureView> = {}): DocumentCaptureView {
  return {
    id: 'cap_01testcapture00000000000001',
    kind: 'expense-receipt',
    schemaVersion: 1,
    status: 'ready',
    contentType: 'image/png',
    byteCount: 1024,
    width: 800,
    height: 600,
    pageCount: 1,
    fields: {
      vendorName: 'Acme Coffee',
      vendorInvoiceNo: 'RCP-42',
      date: '2026-11-10',
      currency: 'USD',
      lines: [
        { description: 'Espresso beans', quantity: null, unitAmount: '25.00', sourceLineTotal: '25.00' },
      ],
      sourceTotals: { subtotal: '25.00', taxTotal: '0.00', grandTotal: '25.00', priceBasis: 'exclusive' },
      paymentHints: { paymentStatus: 'paid', amountPaid: '25.00', paymentMethod: 'Card' },
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

describe('DocumentCapturePanel & fields.ts', () => {
  it('suppresses new-scan controls when capabilities are disabled', async () => {
    authFetch.mockResolvedValueOnce(
      makeJsonResponse({ ...enabledCapabilities, enabled: false, providerAvailable: false, kinds: [] })
    );

    const { container } = render(
      <DocumentCapturePanel kind="expense-receipt" orgSlug="acme" onAccepted={vi.fn()} />
    );

    await waitFor(() => expect(authFetch).toHaveBeenCalledWith('/api/document-captures/capabilities'));
    expect(container.textContent).toBe('');
    expect(screen.queryByLabelText('Upload image or PDF')).toBeNull();
  });

  it('uses canonical server-accepted response, never submits parent form, revokes object URLs, and renders PDF page count', async () => {
    const ready = makeFakeExtractorReadyCapture({
      contentType: 'application/pdf',
      width: null,
      height: null,
      pageCount: 3,
    });
    const canonicalAccepted: DocumentCaptureView = {
      ...ready,
      status: 'accepted',
      fields: null,
      acceptedFields: {
        vendorName: 'Acme Coffee (Canonical)',
        vendorInvoiceNo: 'RCP-42',
        date: '2026-11-10',
        currency: 'USD',
        lines: [{ description: 'Espresso beans', quantity: '1', unitAmount: '25.00', missingQuantityDefaulted: true }],
        sourceTotals: { subtotal: '25.00', taxTotal: '0.00', grandTotal: '25.00', priceBasis: 'exclusive' },
      },
      canAccept: false,
      canApply: true,
      acceptedAt: '2026-11-10T10:01:00Z',
    };

    authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (url === '/api/document-captures/capabilities') return makeJsonResponse(enabledCapabilities);
      if (url === '/api/document-captures' && init?.method === 'POST') return makeJsonResponse(ready);
      if (url.endsWith('/accept') && init?.method === 'POST') return makeJsonResponse(canonicalAccepted);
      return makeJsonResponse({}, 404);
    });

    const onParentSubmit = vi.fn((e: Event) => e.preventDefault());
    const onAccepted = vi.fn();

    const { unmount } = render(
      <form onSubmit={onParentSubmit as any}>
        <DocumentCapturePanel kind={['expense-receipt', 'purchase-invoice']} orgSlug="acme" onAccepted={onAccepted} />
      </form>
    );

    await waitFor(() => expect(screen.getByLabelText('Upload image or PDF')).toBeTruthy());

    const imgFile = new File(['img-bytes'], 'receipt.png', { type: 'image/png' });
    fireEvent.change(screen.getByLabelText('Upload image or PDF'), { target: { files: [imgFile] } });

    await waitFor(() => expect(screen.getByText('3 pages · PDF')).toBeTruthy());
    expect(screen.getByText('Defaulted to 1')).toBeTruthy();
    expect(mockCreate).toHaveBeenCalledTimes(1);

    fireEvent.click(screen.getByRole('button', { name: /Accept & apply to draft/i }));

    await waitFor(() => expect(onAccepted).toHaveBeenCalledWith(canonicalAccepted));
    expect(onParentSubmit).not.toHaveBeenCalled();
    expect(authFetch.mock.calls.some(([u]) => String(u).includes('/api/Purchasing/SaveBill'))).toBe(false);

    unmount();
    expect(mockRevoke).toHaveBeenCalledWith('blob:mock-capture-1');
  });

  it('leaves the date unresolved when AMBIGUOUS_DATE warning is present', async () => {
    const ambiguous = makeFakeExtractorReadyCapture({
      warnings: ['AMBIGUOUS_DATE'],
    });
    authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (url === '/api/document-captures/capabilities') return makeJsonResponse(enabledCapabilities);
      if (url === '/api/document-captures' && init?.method === 'POST') return makeJsonResponse(ambiguous);
      return makeJsonResponse({}, 404);
    });

    render(<DocumentCapturePanel kind="expense-receipt" orgSlug="acme" onAccepted={vi.fn()} />);
    await waitFor(() => expect(screen.getByLabelText('Upload image or PDF')).toBeTruthy());

    fireEvent.change(screen.getByLabelText('Upload image or PDF'), {
      target: { files: [new File(['x'], 'amb.png', { type: 'image/png' })] },
    });

    await waitFor(() => expect(screen.getByText(/Extracted date is ambiguous/i)).toBeTruthy());
    const dateInput = screen.getByLabelText(/Document date/i) as HTMLInputElement;
    expect(dateInput.value).toBe('');

    fireEvent.click(screen.getByRole('button', { name: /Accept & apply to draft/i }));
    await waitFor(() => expect(screen.getByText(/Enter a valid document date in YYYY-MM-DD/i)).toBeTruthy());
  });

  it('prevents application for unknown schema versions and refuses kinds without a typed handler', async () => {
    const unknownSchemaCapture = makeFakeExtractorReadyCapture({ schemaVersion: 99 });
    authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (url === '/api/document-captures/capabilities') return makeJsonResponse(enabledCapabilities);
      if (url === '/api/document-captures' && init?.method === 'POST') return makeJsonResponse(unknownSchemaCapture);
      return makeJsonResponse({}, 404);
    });

    const { unmount } = render(<DocumentCapturePanel kind="expense-receipt" orgSlug="acme" onAccepted={vi.fn()} />);
    await waitFor(() => expect(screen.getByLabelText('Upload image or PDF')).toBeTruthy());

    fireEvent.change(screen.getByLabelText('Upload image or PDF'), {
      target: { files: [new File(['abc'], 'r.png', { type: 'image/png' })] },
    });

    await waitFor(() => expect(screen.getByText(/Schema version 99 is not supported/i)).toBeTruthy());
    expect((screen.getByRole('button', { name: /Accept & apply to draft/i }) as HTMLButtonElement).disabled).toBe(true);
    unmount();

    // Unknown kind without a typed review schema handler disables Accept
    const unknownCapture = makeFakeExtractorReadyCapture({
      kind: 'unknown-kind',
      fields: { raw: 'value' },
    });
    authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (url === '/api/document-captures/capabilities') {
        return makeJsonResponse({ ...enabledCapabilities, kinds: ['unknown-kind'] });
      }
      if (url === '/api/document-captures' && init?.method === 'POST') return makeJsonResponse(unknownCapture);
      return makeJsonResponse({}, 404);
    });

    render(<DocumentCapturePanel kind={'unknown-kind' as unknown as 'bank'} orgSlug="acme" onAccepted={vi.fn()} />);
    await waitFor(() => expect(screen.getByLabelText('Upload image or PDF')).toBeTruthy());
    fireEvent.change(screen.getByLabelText('Upload image or PDF'), {
      target: { files: [new File(['b'], 'b.png', { type: 'image/png' })] },
    });
    await waitFor(() => expect(screen.getByRole('button', { name: /Accept & apply to draft/i })).toBeTruthy());
    expect((screen.getByRole('button', { name: /Accept & apply to draft/i }) as HTMLButtonElement).disabled).toBe(true);
  });

  it('routes already-linked duplicate captures to the saved record instead of treating as unused', async () => {
    const linked = makeFakeExtractorReadyCapture({
      status: 'accepted',
      fields: null,
      acceptedFields: { date: '2026-11-10' },
      canAccept: false,
      canApply: true,
      link: { targetType: 'bill', targetId: 'bill_01existing00000000000001', linkedAt: '2026-11-10T11:00:00Z' },
    });
    authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (url === '/api/document-captures/capabilities') return makeJsonResponse(enabledCapabilities);
      if (url === '/api/document-captures' && init?.method === 'POST') return makeJsonResponse(linked);
      return makeJsonResponse({}, 404);
    });

    const onOpenLinked = vi.fn();
    render(
      <DocumentCapturePanel
        kind="expense-receipt"
        orgSlug="acme"
        onAccepted={vi.fn()}
        onOpenLinkedRecord={onOpenLinked}
      />
    );
    await waitFor(() => expect(screen.getByLabelText('Upload image or PDF')).toBeTruthy());

    fireEvent.change(screen.getByLabelText('Upload image or PDF'), {
      target: { files: [new File(['x'], 'dup.png', { type: 'image/png' })] },
    });

    await waitFor(() => expect(screen.getByRole('button', { name: /Open linked record/i })).toBeTruthy());
    fireEvent.click(screen.getByRole('button', { name: /Open linked record/i }));
    expect(onOpenLinked).toHaveBeenCalledWith(linked.link);
  });

  it('dispatches entitlement denial to PlanLimitDialog', async () => {
    const events: CustomEvent[] = [];
    const listener = (e: Event) => events.push(e as CustomEvent);
    window.addEventListener(ENTITLEMENT_DENIED_EVENT, listener);

    authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (url === '/api/document-captures/capabilities') return makeJsonResponse(enabledCapabilities);
      if (url === '/api/document-captures' && init?.method === 'POST') {
        return makeJsonResponse({ code: 'FEATURE_NOT_ENTITLED', detail: 'Feature not included.' }, 403);
      }
      return makeJsonResponse({}, 404);
    });

    render(<DocumentCapturePanel kind="expense-receipt" orgSlug="acme" onAccepted={vi.fn()} />);
    await waitFor(() => expect(screen.getByLabelText('Upload image or PDF')).toBeTruthy());

    fireEvent.change(screen.getByLabelText('Upload image or PDF'), {
      target: { files: [new File(['a'], 'a.png', { type: 'image/png' })] },
    });
    await waitFor(() => expect(events.length).toBe(1));
    expect(events[0].detail.code).toBe('FEATURE_NOT_ENTITLED');

    window.removeEventListener(ENTITLEMENT_DENIED_EVENT, listener);
  });

  it('keeps 429 rate-limit and 504 timeout errors in panel without auto-retry', async () => {
    let postCalls = 0;
    authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (url === '/api/document-captures/capabilities') return makeJsonResponse(enabledCapabilities);
      if (url === '/api/document-captures' && init?.method === 'POST') {
        postCalls++;
        if (postCalls === 1) {
          return makeJsonResponse({ code: 'DOCUMENT_RATE_LIMITED' }, 429, { 'Retry-After': '60' });
        }
        return makeJsonResponse({ code: 'DOCUMENT_PROVIDER_TIMEOUT', captureId: 'cap_01timeout000000000000001' }, 504);
      }
      if (url === '/api/document-captures/cap_01timeout000000000000001') {
        return makeJsonResponse(makeFakeExtractorReadyCapture({ id: 'cap_01timeout000000000000001' }));
      }
      return makeJsonResponse({}, 404);
    });

    render(<DocumentCapturePanel kind="expense-receipt" orgSlug="acme" onAccepted={vi.fn()} />);
    await waitFor(() => expect(screen.getByLabelText('Upload image or PDF')).toBeTruthy());

    fireEvent.change(screen.getByLabelText('Upload image or PDF'), {
      target: { files: [new File(['b'], 'b.png', { type: 'image/png' })] },
    });
    await waitFor(() => expect(screen.getByRole('alert').textContent).toContain('60 seconds'));

    fireEvent.change(screen.getByLabelText('Upload image or PDF'), {
      target: { files: [new File(['c'], 'c.png', { type: 'image/png' })] },
    });
    await waitFor(() => expect(screen.getByRole('button', { name: /Check status/i })).toBeTruthy());
    expect(postCalls).toBe(2);

    fireEvent.click(screen.getByRole('button', { name: /Check status/i }));
    await waitFor(() => expect(screen.getByRole('button', { name: /Accept & apply to draft/i })).toBeTruthy());
    expect(postCalls).toBe(2);
  });

  it('offers Check status on a lost accept response without calling the provider again', async () => {
    let uploadPosts = 0;
    const ready = makeFakeExtractorReadyCapture();
    const accepted: DocumentCaptureView = {
      ...ready,
      status: 'accepted',
      fields: null,
      acceptedFields: ready.fields,
      canAccept: false,
      canApply: true,
    };

    authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (url === '/api/document-captures/capabilities') return makeJsonResponse(enabledCapabilities);
      if (url === '/api/document-captures' && init?.method === 'POST') {
        uploadPosts++;
        return makeJsonResponse(ready);
      }
      if (url.endsWith('/accept') && init?.method === 'POST') {
        throw new Error('network dropped');
      }
      if (url === `/api/document-captures/${ready.id}`) {
        return makeJsonResponse(accepted);
      }
      return makeJsonResponse({}, 404);
    });

    render(<DocumentCapturePanel kind="expense-receipt" orgSlug="acme" onAccepted={vi.fn()} />);
    await waitFor(() => expect(screen.getByLabelText('Upload image or PDF')).toBeTruthy());

    fireEvent.change(screen.getByLabelText('Upload image or PDF'), {
      target: { files: [new File(['x'], 'x.png', { type: 'image/png' })] },
    });
    await waitFor(() => expect(screen.getByRole('button', { name: /Accept & apply to draft/i })).toBeTruthy());

    fireEvent.click(screen.getByRole('button', { name: /Accept & apply to draft/i }));
    await waitFor(() => expect(screen.getByText(/accept response was lost/i)).toBeTruthy());
    expect(screen.getByRole('button', { name: /Check status/i })).toBeTruthy();

    fireEvent.click(screen.getByRole('button', { name: /Check status/i }));
    await waitFor(() => expect(screen.getByRole('button', { name: /Apply accepted fields/i })).toBeTruthy());
    expect(uploadPosts).toBe(1);
  });

  it('discards stale accept response after switching workspace', async () => {
    let resolveAccept!: (val: unknown) => void;
    const acceptPromise = new Promise((resolve) => {
      resolveAccept = resolve;
    });
    const ready = makeFakeExtractorReadyCapture();
    const accepted: DocumentCaptureView = {
      ...ready,
      status: 'accepted',
      fields: null,
      acceptedFields: ready.fields,
      canAccept: false,
      canApply: true,
    };

    authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (url === '/api/document-captures/capabilities') return makeJsonResponse(enabledCapabilities);
      if (url === '/api/document-captures' && init?.method === 'POST') return makeJsonResponse(ready);
      if (url.endsWith('/accept') && init?.method === 'POST') return acceptPromise;
      return makeJsonResponse({}, 404);
    });

    const onAccepted = vi.fn();
    const { rerender } = render(
      <DocumentCapturePanel kind="expense-receipt" orgSlug="org-a" scopeKey="bill-1" onAccepted={onAccepted} />
    );
    await waitFor(() => expect(screen.getByLabelText('Upload image or PDF')).toBeTruthy());

    fireEvent.change(screen.getByLabelText('Upload image or PDF'), {
      target: { files: [new File(['a'], 'a.png', { type: 'image/png' })] },
    });
    await waitFor(() => expect(screen.getByRole('button', { name: /Accept & apply to draft/i })).toBeTruthy());
    fireEvent.click(screen.getByRole('button', { name: /Accept & apply to draft/i }));

    // Switch workspace before accept resolves
    rerender(
      <DocumentCapturePanel kind="expense-receipt" orgSlug="org-b" scopeKey="bill-1" onAccepted={onAccepted} />
    );

    resolveAccept(makeJsonResponse(accepted));
    await waitFor(() => expect(authFetch).toHaveBeenCalledWith('/api/document-captures/capabilities'));
    expect(onAccepted).not.toHaveBeenCalled();
  });

  it('refuses localized decimals 1.234,56 and 1,234.56 rather than reinterpreting them', () => {
    expect(parseStrictDecimal('1.234,56').valid).toBe(false);
    expect(parseStrictDecimal('1,234.56').valid).toBe(false);
    expect(parseStrictDecimal('12,50').valid).toBe(false);
    expect(parseStrictDecimal('$10.00').valid).toBe(false);
    expect(parseStrictDecimal('1234.5678').valid).toBe(true);
    expect(parseStrictDecimal('1234.56789').valid).toBe(false);
  });

  it('evaluates rounding differences at exactly 0.005 (match) and at 0.01 (discrepancy)', () => {
    const atHalfCent = computeCaptureDiscrepancy({ grandTotal: '10.00' }, 10.005);
    // round2(10.005) = 10.01 -> diff 0.01 (discrepancy); exact 0.004 rounds to 10.00 -> match
    expect(atHalfCent.hasDiscrepancy).toBe(true);
    expect(atHalfCent.difference).toBe(0.01);

    const exactZeroDiff = computeCaptureDiscrepancy({ grandTotal: '10.00' }, 10.004);
    expect(exactZeroDiff.hasDiscrepancy).toBe(false);
    expect(exactZeroDiff.difference).toBe(0);

    const atOneCent = computeCaptureDiscrepancy({ grandTotal: '10.00' }, 10.01);
    expect(atOneCent.hasDiscrepancy).toBe(true);
    expect(atOneCent.difference).toBe(0.01);
  });

  it('refuses explicit zero quantity, flags blank description with amount, and rejects overlong vendor invoice # and reference', () => {
    const zeroQty = validateVendorFieldsForAccept(
      { vendorName: 'V', vendorInvoiceNo: 'INV-1', date: '2026-11-10', dueDate: '', referenceNo: '', currency: 'USD', notes: '' },
      [{ id: 1, description: 'Paper', quantity: '0', unitAmount: '10.00', sourceLineTotal: '', taxHint: '', discount: '', priceBasis: '', missingQuantityDefaulted: false }]
    );
    expect(zeroQty.valid).toBe(false);
    expect(zeroQty.errors.some((e) => e.includes('greater than zero'))).toBe(true);

    const blankDesc = validateVendorFieldsForAccept(
      { vendorName: 'V', vendorInvoiceNo: 'INV-1', date: '2026-11-10', dueDate: '', referenceNo: '', currency: 'USD', notes: '' },
      [{ id: 1, description: '', quantity: '1', unitAmount: '45.00', sourceLineTotal: '', taxHint: '', discount: '', priceBasis: '', missingQuantityDefaulted: false }]
    );
    expect(blankDesc.valid).toBe(false);
    expect(blankDesc.errors.some((e) => e.includes('no description'))).toBe(true);

    const overlong = validateVendorFieldsForAccept(
      { vendorName: 'V', vendorInvoiceNo: 'X'.repeat(51), date: '2026-11-10', dueDate: '', referenceNo: 'R'.repeat(101), currency: 'USD', notes: '' },
      []
    );
    expect(overlong.valid).toBe(false);
    expect(overlong.errors.some((e) => e.includes('50 characters'))).toBe(true);
    expect(overlong.errors.some((e) => e.includes('100 characters'))).toBe(true);

    expect(describeCaptureError('DOCUMENT_DECODE_BUSY', 429).canRetryUpload).toBe(true);
    expect(describeCaptureError('DOCUMENT_BYTES_UNAVAILABLE', 409).canRetryAccept).toBe(false);
    expect(describeCaptureError('DOCUMENT_REVIEW_REQUIRED', 409).code).toBe('DOCUMENT_REVIEW_REQUIRED');
  });

  it('formats negative reconciliation differences as -$10.00 (never $-10.00) (E1)', () => {
    const fmt = (val: number) =>
      `${val < 0 ? '-' : ''}$${Math.abs(val).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
    expect(formatSignedCurrencyDifference(-10, fmt)).toBe('-$10.00');
    expect(formatSignedCurrencyDifference(10, fmt)).toBe('+$10.00');
    expect(formatSignedCurrencyDifference(0, fmt)).toBe('$0.00');
  });

  it('shows a human-readable linked target label instead of the raw public id on duplicate notices (E2)', async () => {
    const onOpenLinkedRecord = vi.fn();
    const linked = makeFakeExtractorReadyCapture({
      status: 'accepted',
      fields: null,
      acceptedFields: {
        date: '2026-11-10',
        vendorName: 'Blue Bean Coffee Supply',
        vendorInvoiceNo: 'INV-42',
      },
      canAccept: false,
      canApply: true,
      link: { targetType: 'bill', targetId: 'bill_01m8secret00000000000001', linkedAt: '2026-11-10T11:00:00Z' },
    });
    expect(describeLinkedCaptureTarget(linked)).toBe('bill #INV-42 from Blue Bean Coffee Supply');

    authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (url === '/api/document-captures/capabilities') return makeJsonResponse(enabledCapabilities);
      if (url === '/api/document-captures' && init?.method === 'POST') return makeJsonResponse(linked);
      return makeJsonResponse({}, 404);
    });

    render(
      <DocumentCapturePanel
        kind="purchase-invoice"
        orgSlug="acme"
        onAccepted={vi.fn()}
        onOpenLinkedRecord={onOpenLinkedRecord}
      />
    );
    await waitFor(() => expect(screen.getByLabelText('Upload image or PDF')).toBeTruthy());
    fireEvent.change(screen.getByLabelText('Upload image or PDF'), {
      target: { files: [new File(['dup'], 'dup.png', { type: 'image/png' })] },
    });

    const notice = await screen.findByText(/linked to bill #INV-42 from Blue Bean Coffee Supply/i);
    expect(notice.textContent).not.toContain('bill_01m8secret');
    fireEvent.click(screen.getByRole('button', { name: /Open linked record/i }));
    expect(onOpenLinkedRecord).toHaveBeenCalledWith(linked.link);
  });
});
