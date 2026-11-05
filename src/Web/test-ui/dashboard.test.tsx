import { useState } from 'react';
import { act, cleanup, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { Dashboard } from '../src/pages/Dashboard';

const { authFetch, hasModule, hasPermission, outlet } = vi.hoisted(() => {
  const hasModuleFn = vi.fn(
    (module: string) => module === 'financials' || module === 'sales' || module === 'purchases',
  );
  const hasPermissionFn = vi.fn(() => false);
  return {
    authFetch: vi.fn(),
    hasModule: hasModuleFn,
    hasPermission: hasPermissionFn,
    outlet: {
      currentOrg: {
        id: '1',
        name: 'Acme',
        slug: 'acme',
        currency: 'USD',
        role: 'Member',
        plan: 'accounting',
        entityType: 'Company',
        operatingPolicy: { purpose: 'Business' },
      },
      user: { name: 'Pat', email: 'pat@example.com' },
      hasModule: hasModuleFn,
      hasPermission: hasPermissionFn,
    },
  };
});

vi.mock('../src/auth/AuthContext', () => ({
  useAuth: () => ({ authFetch }),
}));

vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return {
    ...actual,
    useOutletContext: () => outlet,
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

function summaryPayload(overrides: Record<string, unknown> = {}) {
  return {
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
    ...overrides,
  };
}

beforeEach(() => {
  authFetch.mockReset();
  hasModule.mockImplementation(
    (module: string) => module === 'financials' || module === 'sales' || module === 'purchases',
  );
  hasPermission.mockReturnValue(false);
  outlet.currentOrg = {
    id: '1',
    name: 'Acme',
    slug: 'acme',
    currency: 'USD',
    role: 'Member',
    plan: 'accounting',
    entityType: 'Company',
    operatingPolicy: { purpose: 'Business' },
  };
});

afterEach(cleanup);

/** Keeps Dashboard mounted across workspace switches so stale-request races are exercised. */
function DashboardHarness({ bumpRef }: { bumpRef: { current: (() => void) | null } }) {
  const [, setTick] = useState(0);
  bumpRef.current = () => setTick((n) => n + 1);
  return (
    <MemoryRouter>
      <Dashboard />
    </MemoryRouter>
  );
}

function renderDashboard() {
  const bumpRef = { current: null as (() => void) | null };
  const view = render(<DashboardHarness bumpRef={bumpRef} />);
  return {
    ...view,
    switchWorkspace(next: Partial<typeof outlet.currentOrg> & { slug: string; name: string; id: string }) {
      outlet.currentOrg = { ...outlet.currentOrg, ...next };
      act(() => {
        bumpRef.current?.();
      });
    },
  };
}

it('renders the server invoicing surface even when the workspace has financials module', async () => {
  authFetch.mockResolvedValue(
    new Response(JSON.stringify(summaryPayload()), { status: 200 }),
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
      JSON.stringify(
        summaryPayload({
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
        }),
      ),
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

it('keeps freelance income separate from earned income and shows the linked workspace chip', async () => {
  outlet.currentOrg = {
    ...outlet.currentOrg,
    operatingPolicy: { purpose: 'Personal' },
  };
  authFetch.mockImplementation(async (url: string) => url === '/api/linked-entries'
    ? new Response(JSON.stringify({ otherWorkspace: { name: 'Studio', slug: 'studio' } }), { status: 200 })
    : new Response(JSON.stringify(summaryPayload({
      surface: 'Accounting',
      capabilities: { financials: true, sales: true, purchases: true, banking: true, moneyInBank: true, receivables: true, payables: true },
      kpis: { moneyInBank: 120, outstandingReceivables: 0, totalPayables: 0, totalAssets: 120, totalSalesYtd: 500, totalExpensesYtd: 40, netProfitYtd: 460, earnedIncomeYtd: 300, moneyFromFreelanceWorkYtd: 200, drawingsYtd: 0, cashFlowYtd: 120 },
    })), { status: 200 }));

  renderDashboard();

  expect(await screen.findByTestId('linked-workspace-chip')).toBeTruthy();
  expect(screen.getByTestId('linked-workspace-chip').getAttribute('href')).toBe('/org/studio/dashboard');
  expect(screen.getByText('Money in and out')).toBeTruthy();
  expect(screen.getAllByText('Earned income').length).toBeGreaterThan(0);
  expect(screen.getAllByText('Money from my freelance work').length).toBeGreaterThan(0);
  expect(screen.getAllByText('Net worth (book value)').length).toBeGreaterThan(0);
});

it('leads a sole proprietor with money in, money out and drawings without an empty profit panel', async () => {
  outlet.currentOrg = { ...outlet.currentOrg, entityType: 'SoleProprietor' };
  authFetch.mockResolvedValue(new Response(JSON.stringify(summaryPayload({
    surface: 'Accounting',
    capabilities: { financials: true, sales: true, purchases: true, banking: true, moneyInBank: true, receivables: true, payables: true },
    kpis: { moneyInBank: 120, outstandingReceivables: 0, totalPayables: 0, totalAssets: 120, totalSalesYtd: 0, totalExpensesYtd: 40, netProfitYtd: -40, earnedIncomeYtd: 0, moneyFromFreelanceWorkYtd: 0, drawingsYtd: 25, cashFlowYtd: 120 },
  })), { status: 200 }));

  renderDashboard();

  expect(await screen.findByText('Money in')).toBeTruthy();
  expect(screen.getByText('Money out')).toBeTruthy();
  expect(screen.getByText('Taken for personal use')).toBeTruthy();
  expect(screen.queryByText('Work profit')).toBeNull();
});

it('ignores a stale workspace summary that resolves after switching orgs', async () => {
  let finishWorkspaceAJson!: () => void;
  const workspaceAJsonGate = new Promise<void>((resolve) => {
    finishWorkspaceAJson = resolve;
  });

  authFetch.mockImplementation(async (url: string) => {
    if (url === '/api/linked-entries') return new Response(JSON.stringify({ otherWorkspace: null }), { status: 200 });
    const slugAtRequest = outlet.currentOrg.slug;
    if (slugAtRequest === 'workspace-a') {
      // Headers resolve immediately; body is delayed until after B has loaded.
      return {
        ok: true,
        status: 200,
        async json() {
          await workspaceAJsonGate;
          return summaryPayload({
            kpis: {
              moneyInBank: 0,
              outstandingReceivables: 0,
              totalPayables: 0,
              totalAssets: 0,
              totalSalesYtd: 1111.11,
              totalExpensesYtd: 0,
              netProfitYtd: 0,
              cashFlowYtd: 0,
              overdueReceivables: 0,
              openInvoiceCount: 0,
              overdueInvoiceCount: 0,
            },
            recentActivity: [
              { type: 'Sales Invoice', description: 'Stale workspace A activity', date: '2026-02-01' },
            ],
          });
        },
      };
    }
    return new Response(
      JSON.stringify(
        summaryPayload({
          kpis: {
            moneyInBank: 0,
            outstandingReceivables: 0,
            totalPayables: 0,
            totalAssets: 0,
            totalSalesYtd: 2222.22,
            totalExpensesYtd: 0,
            netProfitYtd: 0,
            cashFlowYtd: 0,
            overdueReceivables: 0,
            openInvoiceCount: 0,
            overdueInvoiceCount: 0,
          },
          recentActivity: [
            { type: 'Sales Invoice', description: 'Only workspace B activity', date: '2026-03-01' },
          ],
        }),
      ),
      { status: 200 },
    );
  });

  outlet.currentOrg = {
    ...outlet.currentOrg,
    id: 'a',
    name: 'Workspace A',
    slug: 'workspace-a',
  };
  const view = renderDashboard();

  await waitFor(() => expect(authFetch).toHaveBeenCalledTimes(2));

  view.switchWorkspace({ id: 'b', name: 'Workspace B', slug: 'workspace-b' });

  expect(await screen.findByText('Workspace B')).toBeTruthy();
  expect(await screen.findByText('Only workspace B activity')).toBeTruthy();
  expect(screen.getByText(/2,222\.22/)).toBeTruthy();

  await act(async () => {
    finishWorkspaceAJson();
    await Promise.resolve();
  });

  expect(screen.queryByText('Stale workspace A activity')).toBeNull();
  expect(screen.getByText('Only workspace B activity')).toBeTruthy();
  expect(screen.getByText(/2,222\.22/)).toBeTruthy();
  expect(screen.queryByText(/1,111\.11/)).toBeNull();
});

it('links purchases-only members to purchases, not sales', async () => {
  authFetch.mockResolvedValue(
    new Response(
      JSON.stringify(
        summaryPayload({
          capabilities: {
            financials: false,
            sales: false,
            purchases: true,
            banking: false,
            moneyInBank: false,
            receivables: false,
            payables: true,
          },
          kpis: {
            moneyInBank: 0,
            outstandingReceivables: 0,
            totalPayables: 80,
            totalAssets: 0,
            totalSalesYtd: 0,
            totalExpensesYtd: 0,
            netProfitYtd: 0,
            cashFlowYtd: 0,
            overdueReceivables: 0,
            openInvoiceCount: 0,
            overdueInvoiceCount: 0,
          },
        }),
      ),
      { status: 200 },
    ),
  );

  renderDashboard();

  expect(await screen.findByText('Invoicing workspace')).toBeTruthy();
  const purchasesLink = await screen.findByRole('link', { name: 'View purchases →' });
  expect(purchasesLink.getAttribute('href')).toBe('/org/acme/purchases');
  expect(screen.queryByRole('link', { name: 'View sales →' })).toBeNull();
});
