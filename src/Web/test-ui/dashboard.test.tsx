import { useState } from 'react';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
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
      updateCurrentOrg: vi.fn(),
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
  outlet.updateCurrentOrg.mockClear();
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
    refresh: () => act(() => bumpRef.current?.()),
    switchWorkspace(next: Partial<typeof outlet.currentOrg> & { slug: string; name: string; id: string }) {
      outlet.currentOrg = { ...outlet.currentOrg, ...next };
      act(() => {
        bumpRef.current?.();
      });
    },
  };
}

it('offers an eligible owner access and refreshes dashboard data after confirmation', async () => {
  outlet.currentOrg.role = 'Owner';
  let granted = false;
  authFetch.mockImplementation(async (path: string, init?: RequestInit) => {
    if (path.endsWith('/owner-access-setup/full-access')) {
      if (init?.method === 'POST') {
        granted = true;
        return new Response('{}');
      }
      return Response.json({ eligible: !granted });
    }
    if (path === '/api/organizations/current') {
      return Response.json({ permissions: ['financials.journal.view'] });
    }
    if (path === '/api/Dashboard/Summary' && granted) {
      return Response.json(summaryPayload({ capabilities: { ...summaryPayload().capabilities, financials: true } }));
    }
    return Response.json(summaryPayload());
  });

  renderDashboard();

  fireEvent.click(await screen.findByRole('button', { name: 'Give me full workspace access' }));
  const dialog = await screen.findByRole('alertdialog');
  expect(granted).toBe(false);
  fireEvent.click(within(dialog).getByRole('button', { name: 'Give me full workspace access' }));

  expect(await screen.findByText('Full workspace access granted.')).toBeTruthy();
  expect(screen.queryByRole('button', { name: 'Give me full workspace access' })).toBeNull();
  await waitFor(() => expect(outlet.updateCurrentOrg).toHaveBeenCalledWith({ permissions: ['financials.journal.view'] }));
  expect(await screen.findByText('General Ledger Accounts')).toBeTruthy();
});

it('keeps self-service access hidden when the server rejects owner eligibility', async () => {
  outlet.currentOrg.role = 'Owner';
  authFetch.mockImplementation(async (path: string) => Response.json(
    path.endsWith('/owner-access-setup/full-access') ? { eligible: false } : summaryPayload(),
  ));

  renderDashboard();

  await screen.findByText('Invoicing workspace');
  await waitFor(() => expect(authFetch).toHaveBeenCalledWith(
    '/api/organizations/current/owner-access-setup/full-access', expect.any(Object),
  ));
  expect(screen.queryByRole('button', { name: 'Give me full workspace access' })).toBeNull();
});

it('applies refreshed permissions through the current workspace updater after a rerender', async () => {
  outlet.currentOrg.role = 'Owner';
  const previousUpdater = outlet.updateCurrentOrg;
  let completeGrant!: (response: Response) => void;
  const pendingGrant = new Promise<Response>((resolve) => { completeGrant = resolve; });
  authFetch.mockImplementation(async (path: string, init?: RequestInit) => {
    if (path.endsWith('/owner-access-setup/full-access')) {
      return init?.method === 'POST' ? pendingGrant : Response.json({ eligible: true });
    }
    if (path === '/api/organizations/current') return Response.json({ permissions: ['sales.invoices.post'] });
    return Response.json(summaryPayload());
  });

  const dashboard = renderDashboard();
  fireEvent.click(await screen.findByRole('button', { name: 'Give me full workspace access' }));
  const dialog = await screen.findByRole('alertdialog');
  fireEvent.click(within(dialog).getByRole('button', { name: 'Give me full workspace access' }));
  outlet.updateCurrentOrg = vi.fn();

  try {
    dashboard.refresh();
    await act(async () => completeGrant(Response.json({})));
    expect(await screen.findByText('Full workspace access granted.')).toBeTruthy();
    expect(outlet.updateCurrentOrg).toHaveBeenCalledWith({ permissions: ['sales.invoices.post'] });
    expect(previousUpdater).not.toHaveBeenCalled();
  } finally {
    outlet.updateCurrentOrg = previousUpdater;
  }
});

it('discards an old workspace grant response after switching workspaces', async () => {
  outlet.currentOrg.role = 'Owner';
  let completeGrant!: (response: Response) => void;
  const pendingGrant = new Promise<Response>((resolve) => { completeGrant = resolve; });
  authFetch.mockImplementation(async (path: string, init?: RequestInit) => {
    if (path.endsWith('/owner-access-setup/full-access')) {
      const slug = new Headers(init?.headers).get('X-AccountGo-Org');
      if (init?.method === 'POST' && slug === 'acme') return pendingGrant;
      return Response.json({ eligible: slug === 'acme' });
    }
    return Response.json(summaryPayload());
  });

  const dashboard = renderDashboard();
  fireEvent.click(await screen.findByRole('button', { name: 'Give me full workspace access' }));
  const dialog = await screen.findByRole('alertdialog');
  fireEvent.click(within(dialog).getByRole('button', { name: 'Give me full workspace access' }));
  expect(await within(dialog).findByRole('button', { name: 'Working…' })).toBeTruthy();

  dashboard.switchWorkspace({ id: '2', slug: 'other', name: 'Other' });
  await act(async () => completeGrant(Response.json({})));

  expect(await screen.findByRole('heading', { name: 'Other' })).toBeTruthy();
  expect(screen.queryByRole('alertdialog')).toBeNull();
  expect(screen.queryByRole('button', { name: 'Give me full workspace access' })).toBeNull();
  expect(outlet.updateCurrentOrg).not.toHaveBeenCalled();
});

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
