import { cleanup, render, screen, waitFor } from '@testing-library/react';
import React, { useEffect } from 'react';
import { createMemoryRouter, Outlet, RouterProvider, type RouteObject } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { RouteGuard } from '../src/components/RouteGuard';
import { RequiresModule } from '../src/components/Gating';
import { clearAccessDirectoryCache } from '../src/lib/accessDirectory';
import { evaluateHasPermission } from '../src/layouts/OrgLayout';
import { publicInWorkspace, requires, requiresAny } from '../src/lib/routeAccess';

const { authFetch, tenant } = vi.hoisted(() => ({
  authFetch: vi.fn(),
  tenant: {
    permissions: [] as string[] | undefined,
    modules: ['purchases', 'sales'] as string[],
    role: 'Owner',
  },
}));

vi.mock('../src/auth/AuthContext', () => ({
  useAuth: () => ({ authFetch, user: { id: 'me', email: 'me@example.com' } }),
  getOrgSlugFromLocation: () => 'acme',
}));
vi.mock('../src/lib/entitlements', () => ({
  useCapabilities: () => ({ canCreate: () => true, entitlements: null }),
  useOptionalCapabilities: () => null,
}));
vi.mock('../src/lib/usePlanCatalog', () => ({ usePlanCatalog: () => ({ plans: [] }), peekPlanCatalog: () => [] }));

// A page that fetches when it mounts, like the real list pages do.
const Page: React.FC<{ name: string }> = ({ name }) => {
  useEffect(() => { void authFetch(`/api/${name}`); }, [name]);
  return <p data-testid="page">{name} page</p>;
};

const Layout: React.FC = () => (
  <Outlet
    context={{
      currentOrg: { id: '1', name: 'Acme', slug: 'acme', role: tenant.role },
      userPermissions: tenant.permissions,
      hasPermission: (permission: string | string[]) => evaluateHasPermission(tenant.permissions, permission),
      hasModule: (module: string) => tenant.modules.includes(module),
      canWrite: () => true,
    }}
  />
);

function mount(path: string, children: RouteObject[]) {
  const router = createMemoryRouter([{ path: '/org/:orgSlug', element: <Layout />, children }], { initialEntries: [path] });
  return render(<RouterProvider router={router} />);
}

const bills: RouteObject = {
  path: 'bills',
  handle: requires('purchases.bills.view', 'Bills'),
  element: <RequiresModule module="purchases"><RouteGuard><Page name="bills" /></RouteGuard></RequiresModule>,
};

beforeEach(() => {
  clearAccessDirectoryCache();
  tenant.permissions = [];
  tenant.modules = ['purchases', 'sales'];
  authFetch.mockImplementation(async (url: string) => {
    if (String(url).endsWith('/members')) return new Response(JSON.stringify([{ userId: 'me', firstName: 'Olive', lastName: 'Owner', role: 'Owner', status: 'Active' }]), { status: 200 });
    if (String(url).endsWith('/security-roles')) return new Response(JSON.stringify([
      { id: 'r1', name: 'Purchasing Agent', displayName: 'Purchasing Agent', permissions: ['purchases.bills.view', 'purchases.vendors.view'] },
      { id: 'r2', name: 'Accountant', displayName: 'Accountant', permissions: ['financials.accounts.view', 'financials.periods.close'] },
    ]), { status: 200 });
    return new Response('[]', { status: 200 });
  });
});
afterEach(() => { cleanup(); vi.clearAllMocks(); });

const pageFetches = () => authFetch.mock.calls.filter(([url]) => String(url) === '/api/bills' || String(url) === '/api/yearend');

describe('RouteGuard', () => {
  it('mounts the page when the member has the permission', async () => {
    tenant.permissions = ['purchases.bills.view'];
    mount('/org/acme/bills', [bills]);

    expect(await screen.findByTestId('page')).toBeTruthy();
    await waitFor(() => expect(pageFetches()).toHaveLength(1));
    expect(screen.queryByTestId('no-access')).toBeNull();
  });

  it('shows the No-access page as the only h1 and never mounts the page or fires its requests', async () => {
    tenant.permissions = ['sales.invoices.view']; // some access, but not to bills
    mount('/org/acme/bills', [bills]);

    expect(await screen.findByRole('heading', { level: 1, name: "You don't have access to Bills" })).toBeTruthy();
    expect(screen.getAllByRole('heading', { level: 1 })).toHaveLength(1);
    expect(screen.queryByTestId('page')).toBeNull();
    expect(pageFetches()).toHaveLength(0);
    const visible = screen.getByTestId('no-access').cloneNode(true) as HTMLElement;
    visible.querySelector('details')?.remove(); // the key is only inside the collapsed Details
    expect(visible.textContent).not.toContain('purchases.bills.view');
    expect(document.body.textContent).not.toMatch(/\b40[13]\b/);
  });

  it('treats an empty permission list as loaded and denied, not as loading', async () => {
    tenant.permissions = [];
    mount('/org/acme/bills', [bills]);

    expect(await screen.findByTestId('no-access')).toBeTruthy();
    expect(screen.queryByRole('status', { name: 'Loading' })).toBeNull();
    expect(pageFetches()).toHaveLength(0);
  });

  it('shows the loading state, not No access, while the permissions are not there yet', () => {
    tenant.permissions = undefined;
    mount('/org/acme/bills', [bills]);

    expect(screen.getByLabelText('Loading')).toBeTruthy();
    expect(screen.queryByTestId('no-access')).toBeNull();
    expect(screen.queryByTestId('page')).toBeNull();
    expect(pageFetches()).toHaveLength(0);
  });

  it('opens an anyOf route for any one of the permissions and names the page when none match', async () => {
    const yearEnd: RouteObject = {
      path: 'year-end',
      handle: requiresAny(['financials.accounts.view', 'financials.periods.close'], 'Year-end close'),
      element: <RouteGuard><Page name="yearend" /></RouteGuard>,
    };
    tenant.permissions = ['financials.periods.close'];
    const first = mount('/org/acme/year-end', [yearEnd]);
    expect(await screen.findByTestId('page')).toBeTruthy();
    first.unmount();

    tenant.permissions = ['reports.view'];
    mount('/org/acme/year-end', [yearEnd]);
    expect(await screen.findByRole('heading', { level: 1, name: "You don't have access to Year-end close" })).toBeTruthy();
    // The roles named are those that grant any one of the keys.
    await screen.findByText(/Access comes with these roles/);
    expect(screen.getByTestId('no-access').textContent).toContain('Accountant');
  });

  it('runs inside the module gate: with the module off the plan state shows, not No access, and nothing is fetched', async () => {
    tenant.permissions = []; // would be denied too, but the module is explained first
    tenant.modules = ['sales'];
    mount('/org/acme/bills', [bills]);

    expect(await screen.findByText('Subscription Gated Module')).toBeTruthy();
    expect(screen.queryByTestId('no-access')).toBeNull();
    expect(pageFetches()).toHaveLength(0);
  });

  it('does not guard a public route, even for a member with no permissions', async () => {
    tenant.permissions = [];
    const dashboard: RouteObject = { path: 'dashboard', handle: publicInWorkspace('landing'), element: <Page name="dashboard" /> };
    mount('/org/acme/dashboard', [dashboard]);

    expect(await screen.findByTestId('page')).toBeTruthy();
    expect(screen.queryByTestId('no-access')).toBeNull();
  });
});
