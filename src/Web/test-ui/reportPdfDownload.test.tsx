import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import Reports from '../src/pages/Reports';

let pdfOk = true;
const fetcher = vi.fn(async (url: string) => {
  if (url.includes('Report.pdf')) {
    if (!pdfOk) return { ok: false, blob: async () => new Blob(), headers: { get: () => null } };
    return {
      ok: true,
      blob: async () => new Blob(['%PDF-1.4'], { type: 'application/pdf' }),
      headers: {
        get: (name: string) => name.toLowerCase() === 'content-disposition'
          ? 'attachment; filename="phase-three-trial-balance-accrual-2026-04-30.pdf"'
          : null,
      },
    };
  }
  if (url.includes('accounting-policy')) return { ok: true, json: async () => ({ booksStartDate: '2020-01-01' }) };
  if (url.includes('CashFlow')) return {
    ok: true,
    json: async () => ({
      accountingBasis: 'Accrual', netIncome: 0, operatingCashFlow: 0, investingCashFlow: 0, financingCashFlow: 0,
      netChangeInCash: 0, startingCash: 0, endingCash: 0, operatingActivities: [], investingActivities: [], financingActivities: [],
    }),
  };
  return { ok: true, json: async () => [] };
});

vi.mock('../src/auth/AuthContext', () => ({ useAuth: () => ({ authFetch: fetcher }) }));
vi.mock('../src/components/Gating', () => ({
  useTenantContext: () => ({
    currentOrg: { slug: 'phase-three', name: 'Phase Three', currency: 'USD', isPersonalBooks: false },
    hasPermission: () => true,
    hasModule: () => false,
  }),
  PermissionDeniedCard: () => null,
}));

const downloads: string[] = [];
const originalClick = HTMLAnchorElement.prototype.click;
URL.createObjectURL = () => 'blob:pdf';
URL.revokeObjectURL = () => {};

afterEach(() => {
  cleanup();
  pdfOk = true;
  downloads.length = 0;
  HTMLAnchorElement.prototype.click = originalClick;
});

async function openReports() {
  render(<MemoryRouter><Reports /></MemoryRouter>);
  await waitFor(() => expect((screen.getByRole('button', { name: 'Download PDF' }) as HTMLButtonElement).disabled).toBe(false));
}

it('downloads the PDF using the server file name', async () => {
  HTMLAnchorElement.prototype.click = function click() {
    downloads.push(this.download);
  };
  await openReports();
  fireEvent.click(screen.getByRole('button', { name: 'Download PDF' }));
  await waitFor(() => expect(downloads).toEqual(['phase-three-trial-balance-accrual-2026-04-30.pdf']));
  const pdfCall = fetcher.mock.calls.map(call => String(call[0])).find(url => url.includes('Report.pdf'));
  expect(pdfCall).toContain('/api/Financials/Report.pdf?');
  expect(pdfCall).toContain('statement=trialBalance');
  expect(pdfCall).not.toContain('hideZero=false');
});

it('shows an error when the PDF request fails', async () => {
  pdfOk = false;
  await openReports();
  fireEvent.click(screen.getByRole('button', { name: 'Download PDF' }));
  expect((await screen.findByRole('alert')).textContent).toContain('Unable to download the report PDF.');
});
