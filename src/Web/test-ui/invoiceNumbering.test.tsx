import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { formatInvoiceNumberPreview, InvoiceNumberingSection, type InvoiceNumberingSettings } from '../src/components/InvoiceNumberingSection';

const { authFetch } = vi.hoisted(() => ({ authFetch: vi.fn() }));
vi.mock('../src/auth/AuthContext', () => ({ useAuth: () => ({ authFetch }) }));

afterEach(() => {
  cleanup();
  authFetch.mockReset();
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
