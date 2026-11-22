// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { DocumentCapturePanel } from '../src/components/document-capture/DocumentCapturePanel';
import {
  describeCaptureError,
  DocumentCaptureCapabilities,
  DocumentCaptureView,
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
  const res = {
    ok: status >= 200 && status < 300,
    status,
    headers: headerMap,
    json: async () => body,
    clone: () => ({ json: async () => body }),
  };
  return res;
}

function makeReadyCapture(overrides: Partial<DocumentCaptureView> = {}): DocumentCaptureView {
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
    fieldConfidences: { vendorName: 0.62, date: null },
    fieldBoxes: { vendorName: { page: 2, x: 0.1, y: 0.1, width: 0.3, height: 0.05 }, date: null },
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

  it('uses canonical server-accepted response, never submits parent form, revokes object URLs, and marks low confidence + page metadata', async () => {
    const ready = makeReadyCapture({
      contentType: 'application/pdf',
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

    // First pick an image to verify object URL creation and revocation on replacement
    const imgFile = new File(['img-bytes'], 'receipt.png', { type: 'image/png' });
    fireEvent.change(screen.getByLabelText('Upload image or PDF'), { target: { files: [imgFile] } });

    await waitFor(() => expect(screen.getByText('Check this')).toBeTruthy());
    expect(screen.getByText('Page 2')).toBeTruthy();
    expect(screen.getByText('3 pages · PDF')).toBeTruthy();
    expect(screen.getByText('Defaulted to 1')).toBeTruthy();
    expect(mockCreate).toHaveBeenCalledTimes(1);

    fireEvent.click(screen.getByRole('button', { name: /Accept & apply to draft/i }));

    await waitFor(() => expect(onAccepted).toHaveBeenCalledWith(canonicalAccepted));
    expect(onParentSubmit).not.toHaveBeenCalled();
    // Never calls any business save endpoint
    expect(authFetch.mock.calls.some(([u]) => String(u).includes('/api/Purchasing/SaveBill'))).toBe(false);

    unmount();
    expect(mockRevoke).toHaveBeenCalledWith('blob:mock-capture-1');
  });

  it('prevents application for unknown schema versions and unavailable bytes', async () => {
    const unknownSchemaCapture = makeReadyCapture({ schemaVersion: 99 });
    authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (url === '/api/document-captures/capabilities') return makeJsonResponse(enabledCapabilities);
      if (url === '/api/document-captures' && init?.method === 'POST') return makeJsonResponse(unknownSchemaCapture);
      return makeJsonResponse({}, 404);
    });

    render(<DocumentCapturePanel kind="expense-receipt" orgSlug="acme" onAccepted={vi.fn()} />);
    await waitFor(() => expect(screen.getByLabelText('Upload image or PDF')).toBeTruthy());

    const file = new File(['abc'], 'r.png', { type: 'image/png' });
    fireEvent.change(screen.getByLabelText('Upload image or PDF'), { target: { files: [file] } });

    await waitFor(() => expect(screen.getByText(/Schema version 99 is not supported/i)).toBeTruthy());
    const acceptBtn = screen.getByRole('button', { name: /Accept & apply to draft/i }) as HTMLButtonElement;
    expect(acceptBtn.disabled).toBe(true);
  });

  it('routes already-linked duplicate captures to the saved record instead of treating as unused', async () => {
    const linked = makeReadyCapture({
      status: 'accepted',
      fields: null,
      acceptedFields: { date: '2026-11-10' },
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

  it('dispatches entitlement denial to PlanLimitDialog and keeps 429 / 504 errors in panel without auto-retry', async () => {
    const events: CustomEvent[] = [];
    const listener = (e: Event) => events.push(e as CustomEvent);
    window.addEventListener(ENTITLEMENT_DENIED_EVENT, listener);

    let callCount = 0;
    authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (url === '/api/document-captures/capabilities') return makeJsonResponse(enabledCapabilities);
      if (url === '/api/document-captures' && init?.method === 'POST') {
        callCount++;
        if (callCount === 1) {
          return makeJsonResponse({ code: 'FEATURE_NOT_ENTITLED', detail: 'Feature not included.' }, 403);
        }
        if (callCount === 2) {
          return makeJsonResponse({ code: 'DOCUMENT_RATE_LIMITED' }, 429, { 'Retry-After': '60' });
        }
        return makeJsonResponse({ code: 'DOCUMENT_PROVIDER_TIMEOUT', captureId: 'cap_01timeout000000000000001' }, 504);
      }
      if (url === '/api/document-captures/cap_01timeout000000000000001') {
        return makeJsonResponse(makeReadyCapture({ id: 'cap_01timeout000000000000001' }));
      }
      return makeJsonResponse({}, 404);
    });

    render(<DocumentCapturePanel kind="expense-receipt" orgSlug="acme" onAccepted={vi.fn()} />);
    await waitFor(() => expect(screen.getByLabelText('Upload image or PDF')).toBeTruthy());

    // 1. Entitlement 403 dispatches shared PlanLimitDialog event
    fireEvent.change(screen.getByLabelText('Upload image or PDF'), {
      target: { files: [new File(['a'], 'a.png', { type: 'image/png' })] },
    });
    await waitFor(() => expect(events.length).toBe(1));
    expect(events[0].detail.code).toBe('FEATURE_NOT_ENTITLED');

    // 2. 429 rate limit renders in panel
    fireEvent.change(screen.getByLabelText('Upload image or PDF'), {
      target: { files: [new File(['b'], 'b.png', { type: 'image/png' })] },
    });
    await waitFor(() => expect(screen.getByRole('alert').textContent).toContain('60 seconds'));

    // 3. 504 timeout does NOT auto-retry; offers manual Check status button
    fireEvent.change(screen.getByLabelText('Upload image or PDF'), {
      target: { files: [new File(['c'], 'c.png', { type: 'image/png' })] },
    });
    await waitFor(() => expect(screen.getByRole('button', { name: /Check status/i })).toBeTruthy());
    expect(callCount).toBe(3);

    fireEvent.click(screen.getByRole('button', { name: /Check status/i }));
    await waitFor(() => expect(screen.getByRole('button', { name: /Accept & apply to draft/i })).toBeTruthy());
    // Still only 3 POST calls (no automatic provider retry!)
    expect(callCount).toBe(3);

    window.removeEventListener(ENTITLEMENT_DENIED_EVENT, listener);
  });

  it('discards stale upload responses after workspace switch', async () => {
    let resolveUpload!: (val: unknown) => void;
    const uploadPromise = new Promise((resolve) => {
      resolveUpload = resolve;
    });

    authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (url === '/api/document-captures/capabilities') return makeJsonResponse(enabledCapabilities);
      if (url === '/api/document-captures' && init?.method === 'POST') return uploadPromise;
      return makeJsonResponse({}, 404);
    });

    const { rerender } = render(
      <DocumentCapturePanel kind="expense-receipt" orgSlug="org-a" scopeKey="bill-1" onAccepted={vi.fn()} />
    );
    await waitFor(() => expect(screen.getByLabelText('Upload image or PDF')).toBeTruthy());

    fireEvent.change(screen.getByLabelText('Upload image or PDF'), {
      target: { files: [new File(['a'], 'slow.png', { type: 'image/png' })] },
    });

    // Switch workspace before upload resolves
    rerender(
      <DocumentCapturePanel kind="expense-receipt" orgSlug="org-b" scopeKey="bill-1" onAccepted={vi.fn()} />
    );

    resolveUpload(makeJsonResponse(makeReadyCapture()));
    await waitFor(() => expect(authFetch).toHaveBeenCalledWith('/api/document-captures/capabilities'));
    // Late response from org-a must be discarded
    expect(screen.queryByRole('button', { name: /Accept & apply to draft/i })).toBeNull();
  });

  it('enforces strict decimal, date, quantity, blank-description, and domain length validation', () => {
    // Ambiguous / localized decimals rejected instead of silently reinterpreted
    expect(parseStrictDecimal('1,234.56').valid).toBe(false);
    expect(parseStrictDecimal('12,50').valid).toBe(false);
    expect(parseStrictDecimal('$10.00').valid).toBe(false);
    expect(parseStrictDecimal('1234.5678').valid).toBe(true);
    expect(parseStrictDecimal('1234.56789').valid).toBe(false);

    const invalid = validateVendorFieldsForAccept(
      {
        vendorName: 'Test',
        vendorInvoiceNo: 'X'.repeat(55), // > 50 bill domain limit
        date: '03/04/2026', // ambiguous date
        dueDate: '',
        referenceNo: '',
        currency: 'USD',
        notes: '',
      },
      [
        {
          id: 1,
          description: '',
          quantity: '0', // explicit zero refused
          unitAmount: '45.00', // amount-bearing blank description flagged
          sourceLineTotal: '',
          taxHint: '',
          discount: '',
          priceBasis: '',
          missingQuantityDefaulted: false,
        },
        {
          // completely blank line is safely removed
          id: 2,
          description: '',
          quantity: '',
          unitAmount: '',
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
    expect(invalid.errors.some((e) => e.includes('50 characters'))).toBe(true);
    expect(invalid.errors.some((e) => e.includes('no description'))).toBe(true);
    expect(invalid.errors.some((e) => e.includes('greater than zero'))).toBe(true);

    // Every §6 code maps cleanly
    expect(describeCaptureError('DOCUMENT_DECODE_BUSY', 429).canRetryUpload).toBe(true);
    expect(describeCaptureError('DOCUMENT_BYTES_UNAVAILABLE', 409).canRetryAccept).toBe(false);
    expect(describeCaptureError('DOCUMENT_REVIEW_REQUIRED', 409).code).toBe('DOCUMENT_REVIEW_REQUIRED');
  });
});
