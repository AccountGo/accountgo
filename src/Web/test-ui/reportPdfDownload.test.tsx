import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import Reports from '../src/pages/Reports';

let pdfOk = true;
let personal = false;
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
  if (url.includes('EquityStatement')) {
    const report = {
      entityType: 'Corporation', from: '2026-01-01', to: '2026-12-31', basis: 'Accrual',
      columns: [{ key: 'contributed', label: 'Share capital' }, { key: 'earnings', label: 'Retained earnings' }],
      rows: [
        { key: 'opening', label: 'Opening equity', amounts: { contributed: 0, earnings: 0 }, total: 0 },
        { key: 'profit', label: 'Profit (loss) for the period', amounts: { contributed: 0, earnings: 1000 }, total: 1000 },
        { key: 'contributions', label: 'Shares issued', amounts: { contributed: 2000, earnings: 0 }, total: 2000 },
        { key: 'closing', label: 'Closing equity', amounts: { contributed: 2000, earnings: 1000 }, total: 3000 },
      ], balanceSheetEquity: 3000, differenceToBalanceSheet: 0,
    };
    return { ok: true, json: async () => ({ ...report, comparative: url.includes('compareFrom') ? { ...report, from: '2025-01-01', to: '2025-12-31' } : null }) };
  }
  if (url.includes('accounting-policy')) return { ok: true, json: async () => ({ booksStartDate: '2020-01-01' }) };
  if (url.includes('cash-movement')) return { ok: true, json: async () => ({ openingCash: 0, totalIn: 0, totalOut: 0, closingCash: 0, lines: [] }) };
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
    currentOrg: { slug: 'phase-three', name: 'Phase Three', currency: 'USD', isPersonalBooks: personal },
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
  personal = false;
  localStorage.clear();
  fetcher.mockClear();
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

it('shows equity components, a separate comparative, and includes comparison dates in the PDF', async () => {
  HTMLAnchorElement.prototype.click = function click() { downloads.push(this.download); };
  await openReports();
  fireEvent.click(screen.getByRole('button', { name: 'Changes in Equity' }));
  await screen.findByRole('columnheader', { name: 'Share capital' });
  expect(screen.getByRole('columnheader', { name: 'Total' })).toBeTruthy();
  expect(screen.getByText(/Agrees with the balance sheet/)).toBeTruthy();
  fireEvent.click(screen.getByLabelText('Compare with previous period'));
  await screen.findByRole('heading', { name: 'Previous period: 2025-01-01 – 2025-12-31' });
  expect(screen.getAllByRole('columnheader', { name: 'Total' })).toHaveLength(2);
  fireEvent.click(screen.getByRole('button', { name: 'Download PDF' }));
  await waitFor(() => expect(downloads).toHaveLength(1));
  const pdfCall = fetcher.mock.calls.map(call => String(call[0])).find(url => url.includes('Report.pdf'))!;
  const params = new URL(pdfCall, 'https://example.test').searchParams;
  expect(params.get('statement')).toBe('equity');
  expect(params.get('compareFrom')).toBeTruthy();
  expect(params.get('compareTo')).toBeTruthy();
});

it('hides the Changes in Equity tab in personal books', async () => {
  personal = true;
  await openReports();
  expect(screen.queryByRole('button', { name: 'Changes in Equity' })).toBeNull();
  expect(fetcher.mock.calls.some(call => String(call[0]).includes('EquityStatement'))).toBe(false);
});

it('reuses loaded data when switching between ledger tabs and fetches equity on its own', async () => {
  await openReports();
  const calls = (part: string) => fetcher.mock.calls.filter(call => String(call[0]).includes(part)).length;
  expect(calls('trialbalance')).toBe(1);
  fireEvent.click(screen.getByRole('button', { name: 'Profit & Loss' }));
  fireEvent.click(screen.getByRole('button', { name: 'Balance Sheet' }));
  expect(screen.queryByText(/Loading/)).toBeNull();
  expect(calls('trialbalance')).toBe(1);
  fireEvent.click(screen.getByRole('button', { name: 'Changes in Equity' }));
  await screen.findByRole('columnheader', { name: 'Share capital' });
  expect(calls('EquityStatement')).toBe(1);
});
