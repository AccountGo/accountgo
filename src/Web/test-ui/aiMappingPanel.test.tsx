// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { DocumentCapturePanel } from '../src/components/document-capture/DocumentCapturePanel';
import { DocumentCaptureCapabilities, DocumentCaptureView } from '../src/components/document-capture/fields';

const { authFetch } = vi.hoisted(() => ({ authFetch: vi.fn() }));

vi.mock('../src/auth/AuthContext', () => ({
  useAuth: () => ({ authFetch }),
}));

beforeEach(() => {
  global.URL.createObjectURL = vi.fn(() => 'blob:ai-mapping');
  global.URL.revokeObjectURL = vi.fn();
});

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
  authFetch.mockReset();
});

const capabilities: DocumentCaptureCapabilities = {
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

const json = (body: unknown, status = 200) => ({
  ok: status >= 200 && status < 300,
  status,
  headers: new Headers({ 'content-type': 'application/json' }),
  json: async () => body,
  clone: () => ({ json: async () => body }),
});

const ID = 'cap_01aimapping0000000000001';

function view(overrides: Partial<DocumentCaptureView> = {}): DocumentCaptureView {
  return {
    id: ID,
    kind: 'purchase-invoice',
    schemaVersion: 1,
    status: 'ready',
    contentType: 'image/png',
    byteCount: 1024,
    width: 800,
    height: 600,
    pageCount: 1,
    fields: {
      vendorName: 'Acme Coffee',
      date: '2026-11-10',
      currency: 'USD',
      lines: [{ description: 'Espresso beans', quantity: '1', unitAmount: '25.00' }],
      sourceTotals: { subtotal: '25.00', taxTotal: '0.00', grandTotal: '25.00', priceBasis: 'exclusive' },
    },
    acceptedFields: null,
    warnings: [],
    errorCode: null,
    bytesAvailable: true,
    canAccept: true,
    canApply: false,
    providerName: 'local-ocr',
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

const improved = (status: 'done' | 'failed' | 'skipped' = 'done') =>
  view({
    fields:
      status === 'done'
        ? {
            vendorName: 'Acme Coffee Roasters Ltd',
            vendorInvoiceNo: 'AC-2045',
            date: '2026-11-10',
            currency: 'USD',
            lines: [{ description: 'Espresso beans, 1 kg', quantity: '1', unitAmount: '25.00' }],
            sourceTotals: { subtotal: '25.00', taxTotal: '0.00', grandTotal: '25.00', priceBasis: 'exclusive' },
            fieldSources: { vendorName: 'ai', vendorInvoiceNo: 'ai', 'lines[0].description': 'ai' },
          }
        : view().fields,
    aiMapping: {
      status,
      ruleHints: status === 'done' ? { vendorName: 'Acme Coffee', 'lines[0].description': 'Espresso beans' } : {},
    },
  });

function mock(uploaded: DocumentCaptureView, polled: () => DocumentCaptureView) {
  let polls = 0;
  authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    if (url.startsWith('/api/document-captures/capabilities')) return json(capabilities);
    if (url === '/api/document-captures' && init?.method === 'POST') return json(uploaded);
    if (url === `/api/document-captures/${ID}` && !init?.method) {
      polls++;
      return json(polled());
    }
    return json({}, 404);
  });
  return { polls: () => polls };
}

async function upload(scopeKey = 'new') {
  const rendered = render(<DocumentCapturePanel kind={['expense-receipt', 'purchase-invoice']} orgSlug="acme" scopeKey={scopeKey} onAccepted={vi.fn()} />);
  await selectFile();
  return rendered;
}

async function selectFile() {
  await waitFor(() => expect(screen.getByLabelText('Upload image or PDF')).toBeTruthy());
  fireEvent.change(screen.getByLabelText('Upload image or PDF'), { target: { files: [new File(['x'], 'bill.png', { type: 'image/png' })] } });
  await waitFor(() => expect(screen.getByLabelText(/^Vendor name/)).toBeTruthy());
}

const pendingView = () => view({ aiMapping: { status: 'pending', ruleHints: {} } });

describe('AI-assisted review updates', () => {
  it('shows the rule result at once with a quiet pending line, then improves only the fields the user has not touched', async () => {
    mock(pendingView(), () => improved('done'));
    await upload();

    // The rule result is already on screen.
    expect((screen.getByLabelText(/^Vendor name/) as HTMLInputElement).value).toBe('Acme Coffee');
    expect(screen.getByRole('status').textContent).toContain('Improving with AccountGo Assistant');

    // The user types a bill number before the mapping finishes.
    fireEvent.change(screen.getByLabelText(/Vendor's bill #/i), { target: { value: 'MY-1' } });

    await waitFor(() => expect((screen.getByLabelText(/^Vendor name/) as HTMLInputElement).value).toBe('Acme Coffee Roasters Ltd'), { timeout: 4000 });
    expect((screen.getByLabelText(/Vendor's bill #/i) as HTMLInputElement).value).toBe('MY-1');
    expect((screen.getByLabelText('Captured line 1 description') as HTMLInputElement).value).toBe('Espresso beans, 1 kg');
    expect(screen.queryByText(/Improving with AccountGo Assistant/)).toBeNull();
  });

  it('badges the AI-assisted fields with text and offers the rules value with a one-click use', async () => {
    mock(pendingView(), () => improved('done'));
    await upload();

    await waitFor(() => expect(screen.getAllByText('AI-assisted').length).toBeGreaterThan(0), { timeout: 4000 });
    // vendorName, vendorInvoiceNo and the line description are marked; the date is not.
    expect(screen.getAllByText('AI-assisted').length).toBeGreaterThanOrEqual(3);
    // One hint for the vendor name and one for the line description.
    await waitFor(() => expect(screen.getAllByText(/rules found:/i)).toHaveLength(2));
    const hints = screen.getAllByText(/rules found:/i).map((el) => el.textContent);
    expect(hints.some((text) => text?.includes('Acme Coffee'))).toBe(true);

    fireEvent.click(screen.getByRole('button', { name: /Use the rules' value for vendorName/i }));
    expect((screen.getByLabelText(/^Vendor name/) as HTMLInputElement).value).toBe('Acme Coffee');
    expect(screen.queryByRole('button', { name: /Use the rules' value for vendorName/i })).toBeNull();
  });

  it.each(['failed', 'skipped'] as const)('changes nothing when the mapping %s: no line, no badge, the rules fields stand', async (status) => {
    mock(pendingView(), () => improved(status));
    await upload();

    await waitFor(() => expect(screen.queryByText(/Improving with AccountGo Assistant/)).toBeNull(), { timeout: 4000 });
    expect((screen.getByLabelText(/^Vendor name/) as HTMLInputElement).value).toBe('Acme Coffee');
    expect(screen.queryByText('AI-assisted')).toBeNull();
    expect(screen.queryByText(/rules found:/i)).toBeNull();
  });

  it('does not poll when AI mapping is not on for the capture', async () => {
    const counters = mock(view(), () => view());
    await upload();
    await new Promise((resolve) => setTimeout(resolve, 1500));

    expect(counters.polls()).toBe(0);
    expect(screen.queryByRole('status')).toBeNull();
  });

  it('stops polling when the panel unmounts', async () => {
    const counters = mock(pendingView(), () => pendingView());
    await upload();
    const { unmount } = { unmount: () => cleanup() };
    await waitFor(() => expect(counters.polls()).toBeGreaterThan(0), { timeout: 3000 });
    unmount();
    const after = counters.polls();
    await new Promise((resolve) => setTimeout(resolve, 2200));

    expect(counters.polls()).toBe(after);
  });

  it('merges again after the same capture is re-extracted and its mapping finishes a second time', async () => {
    let round = 1;
    mock(pendingView(), () =>
      round === 1
        ? improved('done')
        : view({
            fields: {
              ...(view().fields as object),
              vendorName: 'Acme Coffee Roasters Limited',
              fieldSources: { vendorName: 'ai' },
            } as DocumentCaptureView['fields'],
            aiMapping: { status: 'done', ruleHints: { vendorName: 'Acme Coffee' } },
          })
    );
    const { rerender } = await upload('doc-1');
    await waitFor(() => expect((screen.getByLabelText(/^Vendor name/) as HTMLInputElement).value).toBe('Acme Coffee Roasters Ltd'), { timeout: 4000 });

    // The same capture is read again (a new extraction) in a fresh review scope; its mapping finishes with a different result.
    round = 2;
    rerender(<DocumentCapturePanel kind={['expense-receipt', 'purchase-invoice']} orgSlug="acme" scopeKey="doc-2" onAccepted={vi.fn()} />);
    await selectFile();
    expect((screen.getByLabelText(/^Vendor name/) as HTMLInputElement).value).toBe('Acme Coffee');

    await waitFor(() => expect((screen.getByLabelText(/^Vendor name/) as HTMLInputElement).value).toBe('Acme Coffee Roasters Limited'), { timeout: 4000 });
  });
});
