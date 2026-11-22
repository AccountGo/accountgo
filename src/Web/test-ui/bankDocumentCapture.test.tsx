// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { BankAccountFormDialog } from '../src/components/BankAccountFormDialog';
import { DocumentCapturePanel } from '../src/components/document-capture/DocumentCapturePanel';
import {
  canAccessBankCapture,
  DocumentCaptureCapabilities,
  DocumentCaptureView,
  validateBankFieldsForAccept,
} from '../src/components/document-capture/fields';
import type { BankAccount } from '../src/types/banking';

const authFetch = vi.fn();

vi.mock('../src/auth/AuthContext', () => ({
  useAuth: () => ({ authFetch }),
}));

function jsonResponse(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: new Headers({ 'Content-Type': 'application/json' }),
    json: async () => body,
    text: async () => JSON.stringify(body),
    clone() {
      return jsonResponse(body, status);
    },
  } as unknown as Response;
}

const bankCaps: DocumentCaptureCapabilities = {
  enabled: true,
  providerAvailable: true,
  kinds: ['bank'],
  schemaVersion: 1,
  maxUploadBytes: 8 * 1024 * 1024,
  maxPdfPages: 10,
  maxImageWidth: 4096,
  maxImageHeight: 4096,
  maxImagePixels: 16_000_000,
  allowedMimeTypes: ['image/jpeg', 'image/png', 'application/pdf'],
};

function makeBankCapture(overrides: Partial<DocumentCaptureView> = {}): DocumentCaptureView {
  return {
    id: 'cap_bank001',
    kind: 'bank',
    schemaVersion: 1,
    status: 'ready',
    contentType: 'image/png',
    byteCount: 256,
    width: 600,
    height: 400,
    pageCount: 1,
    fields: {
      name: 'Operating Checking',
      bankName: 'First National Bank',
      accountNumber: '0011223344',
      routingNumber: '021000021',
      iban: 'DE89370400440532013000',
      swift: 'COBADEFFXXX',
    },
    acceptedFields: null,
    warnings: [],
    errorCode: null,
    bytesAvailable: true,
    canAccept: true,
    canApply: true,
    providerName: 'fake',
    providerVersion: '1',
    sourceHash: 'a'.repeat(64),
    contentHash: 'b'.repeat(64),
    normalizationVersion: 'image-normalization-v2',
    link: null,
    createdAt: '2026-05-01T10:00:00Z',
    acceptedAt: null,
    rejectedAt: null,
    ...overrides,
  };
}

const existingAccount: BankAccount = {
  id: 'ba_existing1',
  name: 'Existing Treasury',
  type: 1,
  bankName: 'Old Bank',
  accountNumber: '99887766',
  accountNumberMasked: '••••7766',
  routingNumber: '111000025',
  iban: null,
  swift: null,
  currencyCode: 'USD',
  isDefaultForReceipts: true,
  isDefaultForPayments: false,
  openingBalance: 5000,
  openingBalanceDate: '2026-01-01',
  accountId: 'acct_1',
  accountCode: '1010',
  accountName: 'Cash',
  bookBalance: 5000,
  reconciledThrough: null,
  archivedAt: null,
};

let originalCreateObjectURL: typeof URL.createObjectURL;
let originalRevokeObjectURL: typeof URL.revokeObjectURL;

beforeEach(() => {
  vi.clearAllMocks();
  originalCreateObjectURL = URL.createObjectURL;
  originalRevokeObjectURL = URL.revokeObjectURL;
  URL.createObjectURL = vi.fn(() => 'blob:bank-preview-1');
  URL.revokeObjectURL = vi.fn();
});

afterEach(() => {
  cleanup();
  URL.createObjectURL = originalCreateObjectURL;
  URL.revokeObjectURL = originalRevokeObjectURL;
});

describe('BankAccountFormDialog document capture integration (PR 4)', () => {
  it('enforces banking.accounts.manage permission plus sales/purchases/financials module OR gate', () => {
    expect(canAccessBankCapture((p) => p === 'banking.accounts.manage', (m) => m === 'sales')).toBe(true);
    expect(canAccessBankCapture((p) => p === 'banking.accounts.manage', (m) => m === 'purchases')).toBe(true);
    expect(canAccessBankCapture((p) => p === 'banking.accounts.manage', (m) => m === 'financials')).toBe(true);
    expect(canAccessBankCapture((p) => p === 'banking.accounts.manage', () => false)).toBe(false);
    expect(canAccessBankCapture((p) => p === 'banking.accounts.view', (m) => m === 'financials')).toBe(false);
  });

  it('hides scan controls and skips capabilities fetch when bank permission/module gate is false', () => {
    render(
      <BankAccountFormDialog
        hasFinancials={false}
        orgSlug="acme"
        canScanBankDetails={false}
        onSaved={vi.fn()}
        onClose={vi.fn()}
      />
    );

    expect(screen.queryByRole('button', { name: /Scan bank details/i })).toBeNull();
    expect(authFetch).not.toHaveBeenCalledWith('/api/document-captures/capabilities');
  });

  it('hides scan controls when capabilities deny or disable bank capture', async () => {
    authFetch.mockResolvedValueOnce(jsonResponse({ ...bankCaps, kinds: ['expense-receipt'] }));

    render(
      <BankAccountFormDialog
        hasFinancials={false}
        orgSlug="acme"
        canScanBankDetails={true}
        onSaved={vi.fn()}
        onClose={vi.fn()}
      />
    );

    await waitFor(() => expect(authFetch).toHaveBeenCalledWith('/api/document-captures/capabilities'));
    expect(screen.queryByRole('button', { name: /Scan bank details/i })).toBeNull();
  });

  it('fetches capabilities only once across BankAccountFormDialog and DocumentCapturePanel', async () => {
    authFetch.mockResolvedValue(jsonResponse(bankCaps));

    render(
      <BankAccountFormDialog
        hasFinancials={false}
        orgSlug="acme"
        canScanBankDetails={true}
        onSaved={vi.fn()}
        onClose={vi.fn()}
      />
    );

    fireEvent.click(await screen.findByRole('button', { name: /Scan bank details/i }));
    await screen.findByRole('button', { name: /Choose image or PDF/i });

    const capCalls = authFetch.mock.calls.filter(([url]) => url === '/api/document-captures/capabilities');
    expect(capCalls).toHaveLength(1);
  });

  it('accepts bank capture and populates only the six bank detail fields without saving automatically', async () => {
    const onSaved = vi.fn();
    const ready = makeBankCapture();
    const accepted = makeBankCapture({
      status: 'accepted',
      acceptedFields: {
        name: 'Operating Checking',
        bankName: 'First National Bank',
        accountNumber: '0011223344',
        routingNumber: '021000021',
        iban: 'DE89370400440532013000',
        swift: 'COBADEFFXXX',
      },
      acceptedAt: '2026-05-01T10:01:00Z',
    });

    authFetch.mockImplementation(async (url: string) => {
      if (url === '/api/document-captures/capabilities') return jsonResponse(bankCaps);
      if (url === '/api/document-captures') return jsonResponse(ready);
      if (url === '/api/document-captures/cap_bank001/accept') return jsonResponse(accepted);
      return jsonResponse({});
    });

    render(
      <BankAccountFormDialog
        hasFinancials={false}
        orgSlug="acme"
        canScanBankDetails={true}
        onSaved={onSaved}
        onClose={vi.fn()}
      />
    );

    fireEvent.click(await screen.findByRole('button', { name: /Scan bank details/i }));
    const file = new File(['png'], 'voided-check.png', { type: 'image/png' });
    fireEvent.change(screen.getByLabelText('Upload image or PDF'), { target: { files: [file] } });

    fireEvent.click(await screen.findByRole('button', { name: /Accept & apply to draft/i }));

    await waitFor(() => {
      expect((screen.getByLabelText(/Display name/i) as HTMLInputElement).value).toBe('Operating Checking');
    });
    expect((screen.getByLabelText(/Bank \/ institution/i) as HTMLInputElement).value).toBe('First National Bank');
    expect((screen.getByLabelText(/Account number/i) as HTMLInputElement).value).toBe('0011223344');
    expect((screen.getByLabelText(/Routing \/ sort/i) as HTMLInputElement).value).toBe('021000021');
    expect((screen.getByLabelText(/^IBAN$/i) as HTMLInputElement).value).toBe('DE89370400440532013000');
    expect((screen.getByLabelText(/^SWIFT$/i) as HTMLInputElement).value).toBe('COBADEFFXXX');
    expect(onSaved).not.toHaveBeenCalled();
    expect(authFetch.mock.calls.some(([url]) => String(url).startsWith('/api/Banking/Accounts'))).toBe(false);
  });

  it('never alters opening balance or opening-balance date when applying bank capture fields', async () => {
    const ready = makeBankCapture();
    const accepted = makeBankCapture({
      status: 'accepted',
      acceptedFields: {
        name: 'Treasury Checking',
        bankName: 'First National Bank',
        accountNumber: '0011223344',
        openingBalance: '99999.00',
        openingBalanceDate: '2020-01-01',
      } as unknown as Record<string, unknown>,
      acceptedAt: '2026-05-01T10:01:00Z',
    });

    authFetch.mockImplementation(async (url: string) => {
      if (url === '/api/document-captures/capabilities') return jsonResponse(bankCaps);
      if (url === '/api/document-captures') return jsonResponse(ready);
      if (url === '/api/document-captures/cap_bank001/accept') return jsonResponse(accepted);
      return jsonResponse({});
    });

    render(
      <BankAccountFormDialog
        hasFinancials={false}
        orgSlug="acme"
        canScanBankDetails={true}
        onSaved={vi.fn()}
        onClose={vi.fn()}
      />
    );

    const obInput = screen.getByLabelText(/Opening balance/i) as HTMLInputElement;
    const obDateInput = screen.getByLabelText(/As of/i) as HTMLInputElement;
    fireEvent.change(obInput, { target: { value: '1250.50' } });
    fireEvent.change(obDateInput, { target: { value: '2026-03-15' } });

    fireEvent.click(await screen.findByRole('button', { name: /Scan bank details/i }));
    const file = new File(['png'], 'bank-letter.png', { type: 'image/png' });
    fireEvent.change(screen.getByLabelText('Upload image or PDF'), { target: { files: [file] } });
    fireEvent.click(await screen.findByRole('button', { name: /Accept & apply to draft/i }));

    await waitFor(() => {
      expect((screen.getByLabelText(/Display name/i) as HTMLInputElement).value).toBe('Treasury Checking');
    });
    expect(obInput.value).toBe('1250.50');
    expect(obDateInput.value).toBe('2026-03-15');
  });

  it('validates bank field bounds mirroring DocumentCaptureFieldSchemas before POST /accept', () => {
    const overlong = validateBankFieldsForAccept({
      name: 'Checking',
      bankName: 'Bank',
      accountNumber: '1'.repeat(65),
      routingNumber: '2'.repeat(33),
      iban: '3'.repeat(65),
      swift: '4'.repeat(33),
    });

    expect(overlong.valid).toBe(false);
    expect(overlong.errors).toHaveLength(4);
  });

  it('prompts before overwriting populated fields when editing and preserves them on Keep existing', async () => {
    const ready = makeBankCapture();
    const accepted = makeBankCapture({
      status: 'accepted',
      acceptedFields: {
        name: 'Scanned Name',
        bankName: 'Scanned Bank',
        accountNumber: '0011223344',
        routingNumber: '021000021',
        iban: 'DE89370400440532013000',
        swift: 'COBADEFFXXX',
      },
      acceptedAt: '2026-05-01T10:01:00Z',
    });

    authFetch.mockImplementation(async (url: string) => {
      if (url === '/api/document-captures/capabilities') return jsonResponse(bankCaps);
      if (url === '/api/document-captures') return jsonResponse(ready);
      if (url === '/api/document-captures/cap_bank001/accept') return jsonResponse(accepted);
      return jsonResponse({});
    });

    render(
      <BankAccountFormDialog
        account={existingAccount}
        hasFinancials={false}
        orgSlug="acme"
        canScanBankDetails={true}
        onSaved={vi.fn()}
        onClose={vi.fn()}
      />
    );

    fireEvent.click(await screen.findByRole('button', { name: /Scan bank details/i }));
    const file = new File(['png'], 'bank.png', { type: 'image/png' });
    fireEvent.change(screen.getByLabelText('Upload image or PDF'), { target: { files: [file] } });
    fireEvent.click(await screen.findByRole('button', { name: /Accept & apply to draft/i }));

    await screen.findByRole('button', { name: /Keep existing, fill blanks only/i });
    expect((screen.getByLabelText(/Display name/i) as HTMLInputElement).value).toBe('Existing Treasury');

    fireEvent.click(screen.getByRole('button', { name: /Keep existing, fill blanks only/i }));

    expect((screen.getByLabelText(/Display name/i) as HTMLInputElement).value).toBe('Existing Treasury');
    expect((screen.getByLabelText(/Bank \/ institution/i) as HTMLInputElement).value).toBe('Old Bank');
    expect((screen.getByLabelText(/Account number/i) as HTMLInputElement).value).toBe('99887766');
    expect((screen.getByLabelText(/^IBAN$/i) as HTMLInputElement).value).toBe('DE89370400440532013000');
    expect((screen.getByLabelText(/^SWIFT$/i) as HTMLInputElement).value).toBe('COBADEFFXXX');
    expect((screen.getByLabelText(/Opening balance/i) as HTMLInputElement).value).toBe('5000');
    expect((screen.getByLabelText(/As of/i) as HTMLInputElement).value).toBe('2026-01-01');
  });

  it('replaces populated bank details on explicit Replace existing values while preserving opening balance', async () => {
    const ready = makeBankCapture();
    const accepted = makeBankCapture({
      status: 'accepted',
      acceptedFields: {
        name: 'Scanned Name',
        bankName: 'Scanned Bank',
        accountNumber: '0011223344',
        routingNumber: '021000021',
        iban: 'DE89370400440532013000',
        swift: 'COBADEFFXXX',
      },
      acceptedAt: '2026-05-01T10:01:00Z',
    });

    authFetch.mockImplementation(async (url: string) => {
      if (url === '/api/document-captures/capabilities') return jsonResponse(bankCaps);
      if (url === '/api/document-captures') return jsonResponse(ready);
      if (url === '/api/document-captures/cap_bank001/accept') return jsonResponse(accepted);
      return jsonResponse({});
    });

    render(
      <BankAccountFormDialog
        account={existingAccount}
        hasFinancials={false}
        orgSlug="acme"
        canScanBankDetails={true}
        onSaved={vi.fn()}
        onClose={vi.fn()}
      />
    );

    fireEvent.click(await screen.findByRole('button', { name: /Scan bank details/i }));
    const file = new File(['png'], 'bank.png', { type: 'image/png' });
    fireEvent.change(screen.getByLabelText('Upload image or PDF'), { target: { files: [file] } });
    fireEvent.click(await screen.findByRole('button', { name: /Accept & apply to draft/i }));

    fireEvent.click(await screen.findByRole('button', { name: /Replace existing values/i }));

    expect((screen.getByLabelText(/Display name/i) as HTMLInputElement).value).toBe('Scanned Name');
    expect((screen.getByLabelText(/Bank \/ institution/i) as HTMLInputElement).value).toBe('Scanned Bank');
    expect((screen.getByLabelText(/Account number/i) as HTMLInputElement).value).toBe('0011223344');
    expect((screen.getByLabelText(/Opening balance/i) as HTMLInputElement).value).toBe('5000');
    expect((screen.getByLabelText(/As of/i) as HTMLInputElement).value).toBe('2026-01-01');
  });

  it('rejects a bank capture, calls /reject, removes preview, and revokes the object URL', async () => {
    const ready = makeBankCapture();
    const rejected = makeBankCapture({
      status: 'rejected',
      bytesAvailable: false,
      canAccept: false,
      canApply: false,
      rejectedAt: '2026-05-01T10:02:00Z',
    });

    authFetch.mockImplementation(async (url: string) => {
      if (url === '/api/document-captures/capabilities') return jsonResponse(bankCaps);
      if (url === '/api/document-captures') return jsonResponse(ready);
      if (url === '/api/document-captures/cap_bank001/reject') return jsonResponse(rejected);
      return jsonResponse({});
    });

    render(
      <BankAccountFormDialog
        hasFinancials={false}
        orgSlug="acme"
        canScanBankDetails={true}
        onSaved={vi.fn()}
        onClose={vi.fn()}
      />
    );

    fireEvent.click(await screen.findByRole('button', { name: /Scan bank details/i }));
    const file = new File(['png'], 'voided-check.png', { type: 'image/png' });
    fireEvent.change(screen.getByLabelText('Upload image or PDF'), { target: { files: [file] } });

    expect(await screen.findByAltText('Captured document preview')).toBeTruthy();

    fireEvent.click(screen.getByRole('button', { name: /^Reject$/i }));

    await screen.findByText(/This capture was rejected/i);
    expect(screen.queryByAltText('Captured document preview')).toBeNull();
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:bank-preview-1');
    expect(authFetch).toHaveBeenCalledWith('/api/document-captures/cap_bank001/reject', { method: 'POST' });
  });

  it('shows no sensitive bank field values when GET /api/document-captures/{id} returns 403', async () => {
    const ready = makeBankCapture();

    authFetch.mockImplementation(async (url: string) => {
      if (url === '/api/document-captures') return jsonResponse(ready);
      if (url === '/api/document-captures/cap_bank001/accept') {
        return jsonResponse({ code: 'DOCUMENT_PROVIDER_TIMEOUT' }, 504);
      }
      if (url === '/api/document-captures/cap_bank001') {
        return jsonResponse({ code: 'FORBIDDEN' }, 403);
      }
      return jsonResponse({});
    });

    render(
      <DocumentCapturePanel
        kind="bank"
        orgSlug="acme"
        scopeKey="bank-403"
        capabilities={bankCaps}
        onAccepted={vi.fn()}
      />
    );

    const file = new File(['png'], 'bank.png', { type: 'image/png' });
    fireEvent.change(screen.getByLabelText('Upload image or PDF'), { target: { files: [file] } });
    fireEvent.click(await screen.findByRole('button', { name: /Accept & apply to draft/i }));

    fireEvent.click(await screen.findByRole('button', { name: /Check status/i }));

    await waitFor(() => expect(authFetch).toHaveBeenCalledWith('/api/document-captures/cap_bank001'));
    expect(screen.queryByDisplayValue('0011223344')).toBeNull();
    expect(screen.queryByDisplayValue('021000021')).toBeNull();
    expect(screen.queryByDisplayValue('DE89370400440532013000')).toBeNull();
    expect(screen.queryByAltText('Captured document preview')).toBeNull();
  });

  it('discards late in-flight bank capture responses when switching workspace orgSlug', async () => {
    let resolveUpload!: (res: Response) => void;
    authFetch.mockImplementation((url: string) => {
      if (url === '/api/document-captures/capabilities') return Promise.resolve(jsonResponse(bankCaps));
      if (url === '/api/document-captures') {
        return new Promise<Response>((resolve) => {
          resolveUpload = resolve;
        });
      }
      return Promise.resolve(jsonResponse({}));
    });

    const { rerender } = render(
      <BankAccountFormDialog
        hasFinancials={false}
        orgSlug="org-a"
        canScanBankDetails={true}
        onSaved={vi.fn()}
        onClose={vi.fn()}
      />
    );

    fireEvent.click(await screen.findByRole('button', { name: /Scan bank details/i }));
    const file = new File(['png'], 'bank.png', { type: 'image/png' });
    fireEvent.change(screen.getByLabelText('Upload image or PDF'), { target: { files: [file] } });

    // Switch workspace before upload resolves
    rerender(
      <BankAccountFormDialog
        hasFinancials={false}
        orgSlug="org-b"
        canScanBankDetails={true}
        onSaved={vi.fn()}
        onClose={vi.fn()}
      />
    );

    resolveUpload(jsonResponse(makeBankCapture()));
    await new Promise((r) => setTimeout(r, 20));

    expect(screen.queryByDisplayValue('0011223344')).toBeNull();
    expect((screen.getByLabelText(/Account number/i) as HTMLInputElement).value).toBe('');
  });

  it('discards late in-flight bank capture responses when the dialog closes', async () => {
    let resolveUpload!: (res: Response) => void;
    const onAccepted = vi.fn();
    authFetch.mockImplementation((url: string) => {
      if (url === '/api/document-captures/capabilities') return Promise.resolve(jsonResponse(bankCaps));
      if (url === '/api/document-captures') {
        return new Promise<Response>((resolve) => {
          resolveUpload = resolve;
        });
      }
      return Promise.resolve(jsonResponse({}));
    });

    const { unmount } = render(
      <BankAccountFormDialog
        hasFinancials={false}
        orgSlug="org-a"
        canScanBankDetails={true}
        onSaved={onAccepted}
        onClose={vi.fn()}
      />
    );

    fireEvent.click(await screen.findByRole('button', { name: /Scan bank details/i }));
    const file = new File(['png'], 'bank.png', { type: 'image/png' });
    fireEvent.change(screen.getByLabelText('Upload image or PDF'), { target: { files: [file] } });

    // Dialog closes/unmounts before upload resolves
    unmount();

    resolveUpload(jsonResponse(makeBankCapture()));
    await new Promise((r) => setTimeout(r, 20));

    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:bank-preview-1');
    expect(onAccepted).not.toHaveBeenCalled();
  });

  it('does not render a redundant close button inside the bank capture panel when the dialog has a hide toggle (E3)', async () => {
    authFetch.mockResolvedValue(jsonResponse(bankCaps));

    render(
      <BankAccountFormDialog
        hasFinancials={false}
        orgSlug="acme"
        canScanBankDetails={true}
        onSaved={vi.fn()}
        onClose={vi.fn()}
      />
    );

    fireEvent.click(await screen.findByRole('button', { name: /Scan bank details/i }));
    await screen.findByRole('button', { name: /Choose image or PDF/i });
    expect(screen.getByRole('button', { name: /Hide bank detail scanner/i })).toBeTruthy();
    expect(screen.queryByRole('button', { name: /Close document capture/i })).toBeNull();
  });
});
