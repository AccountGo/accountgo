import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { Dashboard } from '../src/pages/Dashboard';

const { authFetch, hasModule, hasPermission } = vi.hoisted(() => ({
  authFetch: vi.fn(),
  hasModule: vi.fn((module: string) => module === 'financials' || module === 'sales' || module === 'purchases'),
  hasPermission: vi.fn(() => false),
}));

vi.mock('../src/auth/AuthContext', () => ({
  useAuth: () => ({ authFetch }),
}));

vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return {
    ...actual,
    useOutletContext: () => ({
      currentOrg: {
        id: '1',
        name: 'Acme',
        slug: 'acme',
        currency: 'USD',
        role: 'Member',
        plan: 'accounting',
        operatingPolicy: { purpose: 'Business' },
      },
      user: { name: 'Pat', email: 'pat@example.com' },
      hasModule,
      hasPermission,
    }),
  };
});

vi.mock('../src/components/OnboardingChecklist', () => ({
  OnboardingChecklist: () => null,
}));

vi.mock('../src/components/BankCloseStatusCard', () => ({
  BankCloseStatusCard: () => null,
}));

vi.mock('../src/components/MoneyInBankBreakdown', () => ({
  MoneyInBankBreakdown: () => null,
}));

beforeEach(() => {
  authFetch.mockReset();
  hasModule.mockImplementation(
    (module: string) => module === 'financials' || module === 'sales' || module === 'purchases',
  );
  hasPermission.mockReturnValue(false);
});

afterEach(cleanup);

function renderDashboard() {
  return render(
    <MemoryRouter>
      <Dashboard />
    </MemoryRouter>,
  );
}

it('renders the server invoicing surface even when the workspace has financials module', async () => {
  authFetch.mockResolvedValue(
    new Response(
      JSON.stringify({
        surface: 'Invoicing',
        capabilities: {
          financials: false,
          sales: true,
          purchases: true,
          banking: true,
          moneyInBank: true,
          receivables: true,
          payables: true,
        },
        kpis: {
          moneyInBank: 400,
          outstandingReceivables: 110,
          totalPayables: 20,
          totalAssets: 0,
          totalSalesYtd: 115,
          totalExpensesYtd: 0,
          netProfitYtd: 0,
          cashFlowYtd: 0,
          overdueReceivables: 110,
          openInvoiceCount: 1,
          overdueInvoiceCount: 1,
        },
        balanceSnapshot: { totalAssets: 0, totalLiabilities: 0, totalEquity: 0 },
        monthlySales: [],
        monthlyExpenses: [],
        topReceivables: [],
        upcomingPayables: [],
        recentActivity: [],
      }),
      { status: 200 },
    ),
  );

  renderDashboard();

  expect(await screen.findByText('Invoicing workspace')).toBeTruthy();
  expect(screen.getByText('Invoiced (YTD)')).toBeTruthy();
  expect(screen.getByText(/Issued invoice totals incl\. tax and shipping/)).toBeTruthy();
  expect(screen.queryByText('Operating Sales (YTD)')).toBeNull();
  expect(screen.queryByText('General Ledger Accounts')).toBeNull();
});

it('shows Unavailable for restricted metrics instead of healthy zeros', async () => {
  authFetch.mockResolvedValue(
    new Response(
      JSON.stringify({
        surface: 'Accounting',
        capabilities: {
          financials: false,
          sales: true,
          purchases: false,
          banking: false,
          moneyInBank: false,
          receivables: true,
          payables: false,
        },
        kpis: {
          moneyInBank: 0,
          outstandingReceivables: 50,
          totalPayables: 0,
          totalAssets: 0,
          totalSalesYtd: 0,
          totalExpensesYtd: 0,
          netProfitYtd: 0,
          cashFlowYtd: 0,
        },
        balanceSnapshot: { totalAssets: 0, totalLiabilities: 0, totalEquity: 0 },
        monthlySales: [],
        monthlyExpenses: [],
        topReceivables: [],
        upcomingPayables: [],
        recentActivity: [],
      }),
      { status: 200 },
    ),
  );

  renderDashboard();

  await waitFor(() => expect(screen.getAllByText('Unavailable').length).toBeGreaterThan(0));
  expect(screen.queryByText('General Ledger Accounts')).toBeNull();
});

it('shows an error and retry when the summary request fails', async () => {
  authFetch.mockResolvedValue(new Response(null, { status: 403 }));

  renderDashboard();

  expect(await screen.findByTestId('dashboard-load-error')).toBeTruthy();
  expect(screen.getByText(/Could not load dashboard \(403\)/)).toBeTruthy();
  expect(screen.getByRole('button', { name: 'Retry' })).toBeTruthy();
});
