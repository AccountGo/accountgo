import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import BankStatementImportPage from '../src/pages/BankStatementImport';
import { LinkedEntryDialog } from '../src/components/LinkedEntryDialog';

const { authFetch, hasPermission, outlet } = vi.hoisted(() => ({
  authFetch: vi.fn(),
  hasPermission: vi.fn((permission: string) => ['financials.journal.post', 'financials.journal.create', 'banking.accounts.manage'].includes(permission)),
  outlet: { currentOrg: { id: '1', name: 'Work', slug: 'work', entityType: 'SoleProprietor', currency: 'USD' } },
}));
vi.mock('../src/auth/AuthContext', () => ({ useAuth: () => ({ authFetch }) }));
vi.mock('../src/components/Gating', () => ({ useTenantContext: () => ({ hasPermission }) }));
vi.mock('../src/lib/entitlements', () => ({ useCapabilities: () => ({ canModule: () => true, canWrite: () => true, canCreate: () => true }) }));
vi.mock('../src/lib/integrations', () => ({ refreshIncomingReview: vi.fn() }));
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return { ...actual, useOutletContext: () => outlet };
});

const accounts = {
  personalCurrency: 'USD',
  accounts: [
    { id: 20, accountCode: '50000', accountName: 'Household spending', accountClassId: 5, isCash: false },
    { id: 21, accountCode: '10100', accountName: 'Personal bank', accountClassId: 1, isCash: true },
  ],
  workExpenseAccounts: [{ id: 30, accountCode: '60100', accountName: 'Telephone', accountClassId: 5, isCash: false }],
};
const line = (id: number, amount: number) => ({ id, bankId: 4, bankStatementImportId: 2, source: 1,
  postedDate: '2026-11-05', amount, payee: 'Supplier', memo: 'Statement memo', isMatched: false, candidates: [] });

function page() {
  return render(<MemoryRouter initialEntries={['/org/work/banking/4/statement-import']}>
    <Routes><Route path="/org/:slug/banking/:bankId/statement-import" element={<BankStatementImportPage />} /></Routes>
  </MemoryRouter>);
}

beforeEach(() => {
  authFetch.mockReset();
  hasPermission.mockImplementation((permission: string) => ['financials.journal.post', 'financials.journal.create', 'banking.accounts.manage'].includes(permission));
  outlet.currentOrg = { id: '1', name: 'Work', slug: 'work', entityType: 'SoleProprietor', currency: 'USD' };
  authFetch.mockImplementation(async (url: string) => {
    if (url.includes('/StatementLines?')) return new Response(JSON.stringify({ lines: [line(11, -48), line(12, 80)], totalCount: 2, page: 1, pageSize: 100 }), { status: 200 });
    if (url.endsWith('/Banking/Accounts/4')) return new Response(JSON.stringify({ id: 4, name: 'Work bank', currencyCode: 'USD', isDefaultForReceipts: false, isDefaultForPayments: false, bookBalance: 0 }), { status: 200 });
    if (url === '/api/linked-entries/accounts') return new Response(JSON.stringify(accounts), { status: 200 });
    return new Response('{}', { status: 200 });
  });
});
afterEach(() => { cleanup(); vi.clearAllMocks(); });

describe('linked bank statement entries', () => {
  it('shows Personal and Money I put in actions for a sole proprietor', async () => {
    page();
    expect(await screen.findByRole('button', { name: 'Personal' })).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Money I put in' })).toBeTruthy();
  });

  it('shows neither linked action for a company workspace', async () => {
    outlet.currentOrg = { ...outlet.currentOrg, entityType: 'Corporation' };
    page();
    await screen.findAllByText('Supplier');
    expect(screen.queryByRole('button', { name: 'Personal' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Money I put in' })).toBeNull();
  });

  it('offers split amounts and submits the work and personal portions', async () => {
    authFetch.mockImplementation(async (url: string) => {
      if (url === '/api/linked-entries/accounts') return new Response(JSON.stringify(accounts), { status: 200 });
      if (url === '/api/linked-entries') return new Response(JSON.stringify({ pair: { id: 1, state: 'PendingSource' } }), { status: 200 });
      return new Response('{}', { status: 200 });
    });
    const onSaved = vi.fn();
    render(<LinkedEntryDialog line={line(11, -48)} workBankId={4} initialKind="Personal" workCurrency="USD" onClose={vi.fn()} onSaved={onSaved} />);
    fireEvent.click(await screen.findByLabelText('Split part of this purchase as a work expense'));
    fireEvent.change(screen.getByLabelText('Work expense category'), { target: { value: '30' } });
    fireEvent.change(screen.getByLabelText('Work amount'), { target: { value: '18' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create linked entry' }));
    await waitFor(() => expect(onSaved).toHaveBeenCalled());
    const request = authFetch.mock.calls.find(([url]) => url === '/api/linked-entries')?.[1] as RequestInit;
    const body = JSON.parse(String(request.body));
    expect(body).toMatchObject({ kind: 'Personal', amount: 48, statementLineId: 11, splitWorkExpenseAccountId: 30, splitWorkAmount: 18 });
  });
});
