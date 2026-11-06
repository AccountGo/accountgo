import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import Reports, { cashMovementCsv } from '../src/pages/Reports';
const fetcher = vi.fn(async (url: string) => ({ ok: true, json: async () => {
  if (url.includes('cash-movement')) return { openingCash: 100, lines: [{ date: '2026-02-03', description: 'Income', accountName: 'Bank', counterpartAccountName: 'Pay', moneyIn: 25, moneyOut: 0 }], totalIn: 25, totalOut: 0, closingCash: 125 };
  if (url.includes('accounting-policy')) return { booksStartDate: '2026-01-01' };
  return [];
} }));
vi.mock('../src/auth/AuthContext', () => ({ useAuth: () => ({ authFetch: fetcher }) }));
vi.mock('../src/components/Gating', () => ({ useTenantContext: () => ({ currentOrg: { slug: 'personal', name: 'Books', currency: 'USD', isPersonalBooks: true }, hasPermission: () => true, hasModule: () => false }), PermissionDeniedCard: () => null }));
afterEach(cleanup);
it('personal reports show the cash movement register and totals without cash flow terminology', async () => {
  render(<MemoryRouter><Reports /></MemoryRouter>);
  expect(screen.getByRole('button', { name: 'Net worth (book value)' })).toBeTruthy();
  expect(screen.getByRole('button', { name: 'Money in and out' })).toBeTruthy();
  expect(screen.getByRole('heading', { name: 'Advanced' })).toBeTruthy();
  expect(screen.getByRole('button', { name: 'Trial balance' })).toBeTruthy();
  fireEvent.click(screen.getByRole('button', { name: 'Cash movement' }));
  expect(await screen.findByText('Income')).toBeTruthy();
  expect(screen.getByRole('columnheader', { name: 'Money in' })).toBeTruthy();
  expect(screen.getByRole('columnheader', { name: 'Money out' })).toBeTruthy();
  expect(screen.getByText('Opening cash')).toBeTruthy();
  expect(screen.getByText('Closing cash')).toBeTruthy();
  expect(screen.getByText('Total in')).toBeTruthy();
  expect(screen.getByText('Total out')).toBeTruthy();
  expect(document.body.textContent).not.toMatch(/profit|operating|investing|financing/i);
  expect(fetcher.mock.calls.some(([url]) => url.includes('/CashFlow'))).toBe(false);
});

it('cash movement export includes the reconciliation and protects spreadsheet cells', () => {
  const csv = cashMovementCsv({ openingCash: 100, lines: [{ date: '2026-02-03', generalLedgerHeaderId: 1, description: '=formula', accountName: 'Bank', counterpartAccountName: 'Pay', moneyIn: 25, moneyOut: 0 }], totalIn: 25, totalOut: 0, closingCash: 125 });
  expect(csv).toContain('Opening cash');
  expect(csv).toContain('Closing cash');
  expect(csv).toContain("'=formula");
});
