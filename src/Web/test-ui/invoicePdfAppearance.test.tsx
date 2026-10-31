import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { InvoicePdfAppearanceEditor, type InvoiceBrandingCapabilities, type InvoiceBrandingSettings, DEFAULT_INVOICE_PALETTE } from '../src/components/InvoicePdfAppearanceSection';
import { InvoiceSettingsTab } from '../src/components/InvoiceSettingsTab';

const { authFetch } = vi.hoisted(() => ({ authFetch: vi.fn() }));
vi.mock('../src/auth/AuthContext', () => ({ useAuth: () => ({ authFetch, completeStepUp: vi.fn() }) }));

afterEach(() => cleanup());

const capabilities = (overrides: Partial<InvoiceBrandingCapabilities> = {}): InvoiceBrandingCapabilities => ({
  companyId: 1,
  canCustomizeInvoiceAppearance: true,
  canHideAccountGoBranding: true,
  canManageSettings: true,
  upgradeAvailable: false,
  unavailableReason: null,
  hasRetainedSettings: true,
  effectiveShowsAccountGoBranding: true,
  inactiveReason: null,
  ...overrides,
});

const settings = (overrides: Partial<InvoiceBrandingSettings> = {}): InvoiceBrandingSettings => ({
  schemaVersion: 1,
  requestedPalette: DEFAULT_INVOICE_PALETTE,
  hideAccountGoBranding: false,
  revision: 2,
  applied: true,
  inactiveReason: null,
  effectivePalette: DEFAULT_INVOICE_PALETTE,
  effectiveShowsAccountGoBranding: true,
  ...overrides,
});

describe('invoice PDF appearance settings', () => {
  it('explains a downgrade and keeps the saved preference visible', () => {
    render(<InvoicePdfAppearanceEditor
      capabilities={capabilities({ canCustomizeInvoiceAppearance: false, canHideAccountGoBranding: false, canManageSettings: false, upgradeAvailable: true, inactiveReason: 'Saved appearance is kept and will apply again when customization is available.' })}
      settings={settings({ hideAccountGoBranding: true, applied: false, inactiveReason: 'Saved appearance is kept and will apply again when customization is available.' })}
      disabled={false}
      onChange={() => {}}
      onSave={() => {}}
      onRestore={() => {}}
      onPreview={() => {}}
    />);
    expect(screen.getByRole('status').textContent).toContain('Saved appearance is kept');
    expect(screen.getByRole('status').textContent).toContain('upgrade');
    expect((screen.getByRole('checkbox') as HTMLInputElement).checked).toBe(true);
    expect((screen.getByRole('button', { name: /Save PDF appearance/ }) as HTMLButtonElement).disabled).toBe(true);
  });

  it('saves the edited palette and hide choice', () => {
    const onChange = vi.fn();
    const onSave = vi.fn();
    render(<InvoicePdfAppearanceEditor
      capabilities={capabilities()}
      settings={settings()}
      disabled={false}
      onChange={onChange}
      onSave={onSave}
      onRestore={() => {}}
      onPreview={() => {}}
    />);
    fireEvent.click(screen.getByRole('button', { name: 'Harbor' }));
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ requestedPalette: expect.objectContaining({ accent: '#1D4ED8' }) }));
    fireEvent.click(screen.getByRole('checkbox'));
    fireEvent.click(screen.getByRole('button', { name: /Save PDF appearance/ }));
    expect(onSave).toHaveBeenCalledTimes(1);
  });

  it('tells a workspace without customization that attribution stays on the PDF', () => {
    render(<InvoicePdfAppearanceEditor
      capabilities={capabilities({ canCustomizeInvoiceAppearance: false, canHideAccountGoBranding: false, canManageSettings: false, hasRetainedSettings: false, unavailableReason: 'Invoice appearance customization is unavailable.' })}
      settings={settings({ revision: 0, applied: false })}
      disabled={false}
      onChange={() => {}}
      onSave={() => {}}
      onRestore={() => {}}
      onPreview={() => {}}
    />);
    expect(screen.getByText(/Powered by AccountGo/)).toBeTruthy();
    expect(screen.queryByRole('checkbox')).toBeNull();
  });

  it('verifies MFA without saving the surrounding invoice settings', async () => {
    authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      const method = init?.method ?? 'GET';
      if (url.endsWith('/sales-invoice-settings') && method === 'PUT') return jsonResponse({ sellerName: 'TradingName' });
      if (url.endsWith('/sales-invoice-settings')) return jsonResponse({
        sellerName: 'TradingName', showSellerAddress: true, showCustomerEmail: true, showAttention: true,
        showNotes: true, showPaymentInstructions: true, defaultNotes: null, defaultPaymentInstructions: null, paymentBankId: null,
      });
      if (url.endsWith('/business-profile')) return jsonResponse({ legalName: 'Northwind LLC' });
      if (url.endsWith('/Picker')) return jsonResponse([]);
      if (url.endsWith('/capabilities')) return jsonResponse(capabilities());
      if (url.endsWith('/invoice-branding/settings') && method === 'PUT') return jsonResponse({ code: 'mfa_step_up_required' }, 403);
      if (url.endsWith('/invoice-branding/settings')) return jsonResponse(settings());
      if (url.endsWith('/step-up/totp')) return jsonResponse({ accessToken: 'stepped-up' });
      return jsonResponse({});
    });

    render(<InvoiceSettingsTab tradingName="Northwind" canEdit />);
    fireEvent.click(await screen.findByRole('button', { name: /Save PDF appearance/ }));
    const code = await screen.findByLabelText(/Authenticator code/);
    const verify = screen.getByRole('button', { name: 'Verify code' });
    const form = document.getElementById('invoice-settings-form');
    expect(form?.contains(verify)).toBe(false);
    expect(form?.contains(code)).toBe(false);
    fireEvent.change(code, { target: { value: '123456' } });
    fireEvent.click(verify);
    await screen.findByText(/Identity verified/);

    const invoiceWrites = authFetch.mock.calls.filter((call) => String(call[0]).includes('sales-invoice-settings') && (call[1] as RequestInit | undefined)?.method === 'PUT');
    expect(invoiceWrites).toHaveLength(0);
  });
});

function jsonResponse(body: unknown, status = 200) {
  return { ok: status >= 200 && status < 300, status, json: async () => body, blob: async () => new Blob() };
}
