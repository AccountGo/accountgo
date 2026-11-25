import { cleanup, render, screen, waitFor } from '@testing-library/react';
import React, { useCallback, useEffect, useState } from 'react';
import { createMemoryRouter, Outlet, RouterProvider, type RouteObject } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { RouteGuard } from '../src/components/RouteGuard';
import { RequiresModule } from '../src/components/Gating';
import { clearAccessDirectoryCache } from '../src/lib/accessDirectory';
import { evaluateHasPermission } from '../src/layouts/OrgLayout';
import { publicInWorkspace, requires, requiresAny } from '../src/lib/routeAccess';
import { refreshWorkspaceAccess, resetPermissionRefresh, throttledRefresh } from '../src/lib/permissionRefresh';

const { authFetch, tenant } = vi.hoisted(() => ({
  authFetch: vi.fn(),
  tenant: {
    permissions: [] as string[] | undefined,
    /** What the server will report on the next read of the workspace (a role granted since the page opened). */
    serverPermissions: [] as string[],
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

// Mirrors OrgLayout: permissions held in state, and the same shared, throttled refresh that applies what the server reports.
const Layout: React.FC = () => {
  const [permissions, setPermissions] = useState(tenant.permissions);
  const refreshPermissions = useCallback(
    () => throttledRefresh('acme\u0000me', () => refreshWorkspaceAccess({
      fetchCurrent: () => authFetch('/api/organizations/current'),
      getCurrent: () => ({ slug: 'acme', role: tenant.role, permissions: permissionsRef.current ?? [] }),
      apply: (updates) => { permissionsRef.current = updates.permissions; setPermissions(updates.permissions); },
    })),
    [],
  );
  permissionsRef.current = permissions;
  return (
    <Outlet
      context={{
        currentOrg: { id: '1', name: 'Acme', slug: 'acme', role: tenant.role },
        userPermissions: permissions,
        hasPermission: (permission: string | string[]) => evaluateHasPermission(permissions, permission),
        hasModule: (module: string) => tenant.modules.includes(module),
        canWrite: () => true,
        refreshPermissions,
      }}
    />
  );
};
const permissionsRef: { current: string[] | undefined } = { current: [] };

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
  resetPermissionRefresh();
  tenant.permissions = [];
  tenant.serverPermissions = [];
  tenant.modules = ['purchases', 'sales'];
  authFetch.mockImplementation(async (url: string) => {
    if (String(url) === '/api/organizations/current') {
      return new Response(JSON.stringify({ slug: 'acme', role: tenant.role, permissions: tenant.serverPermissions }), { status: 200 });
    }
    if (String(url).endsWith('/members')) return new Response(JSON.stringify([{ userId: 'me', firstName: 'Olive', lastName: 'Owner', role: 'Owner', status: 'Active' }]), { status: 200 });
    if (String(url).endsWith('/security-roles')) return new Response(JSON.stringify([
      { id: 'r1', name: 'Purchasing Agent', displayName: 'Purchasing Agent', permissions: ['purchases.bills.view', 'purchases.vendors.view'] },
      { id: 'r2', name: 'Accountant', displayName: 'Accountant', permissions: ['financials.accounts.view', 'financials.periods.close'] },
    ]), { status: 200 });
    return new Response('[]', { status: 200 });
  });
});
afterEach(() => { cleanup(); vi.clearAllMocks(); });

const refreshCalls = () => authFetch.mock.calls.filter(([url]) => String(url) === '/api/organizations/current');
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

  describe('refreshing stale permissions', () => {
    it('re-reads the workspace once when it denies, and keeps showing No access meanwhile', async () => {
      tenant.permissions = [];
      tenant.serverPermissions = [];
      mount('/org/acme/bills', [bills]);

      expect(await screen.findByTestId('no-access')).toBeTruthy();
      await waitFor(() => expect(refreshCalls()).toHaveLength(1));
      expect(screen.queryByTestId('page')).toBeNull();
    });

    it('mounts the page in place, and only then fetches, when the refreshed permissions allow it', async () => {
      tenant.permissions = [];
      tenant.serverPermissions = ['purchases.bills.view']; // an admin granted the role after this page opened
      mount('/org/acme/bills', [bills]);

      expect(await screen.findByTestId('page')).toBeTruthy();
      await waitFor(() => expect(pageFetches()).toHaveLength(1));
      expect(screen.queryByTestId('no-access')).toBeNull();
      expect(refreshCalls()).toHaveLength(1);
    });

    it('stays on No access when the server still denies, and does not refresh again within the minute', async () => {
      tenant.permissions = [];
      tenant.serverPermissions = [];
      const first = mount('/org/acme/bills', [bills]);
      await waitFor(() => expect(refreshCalls()).toHaveLength(1));
      first.unmount();

      mount('/org/acme/bills', [bills]); // the member clicks it again, well inside the minute
      expect(await screen.findByTestId('no-access')).toBeTruthy();
      await new Promise((resolve) => setTimeout(resolve, 50));

      expect(refreshCalls()).toHaveLength(1);
      expect(pageFetches()).toHaveLength(0);
    });

    it('shares one refresh between two guards on screen', async () => {
      tenant.permissions = [];
      const both: RouteObject = {
        path: 'both',
        handle: requires('purchases.bills.view', 'Bills'),
        element: <><RouteGuard><Page name="a" /></RouteGuard><RouteGuard><Page name="b" /></RouteGuard></>,
      };
      mount('/org/acme/both', [both]);

      await waitFor(() => expect(screen.getAllByTestId('no-access')).toHaveLength(2));
      await waitFor(() => expect(refreshCalls()).toHaveLength(1));
    });

    it('does not refresh for an allowed route, a public route or a still-loading one', async () => {
      tenant.permissions = ['purchases.bills.view'];
      const allowed = mount('/org/acme/bills', [bills]);
      await screen.findByTestId('page');
      allowed.unmount();
      tenant.permissions = undefined;
      mount('/org/acme/bills', [bills]);
      await new Promise((resolve) => setTimeout(resolve, 50));

      expect(refreshCalls()).toHaveLength(0);
    });
  });
});

describe('throttledRefresh and refreshWorkspaceAccess', () => {
  beforeEach(() => resetPermissionRefresh());

  it('runs at most once per key per minute, even when the run fails, and again after the minute', async () => {
    let clock = 1_000_000;
    const run = vi.fn(async () => { throw new Error('network'); });

    expect(await throttledRefresh('k', run, () => clock)).toBe(false);
    clock += 30_000;
    expect(await throttledRefresh('k', run, () => clock)).toBe(false);
    expect(run).toHaveBeenCalledTimes(1);

    clock += 31_000;
    await throttledRefresh('k', run, () => clock);
    expect(run).toHaveBeenCalledTimes(2);
    expect(await throttledRefresh('other', async () => true, () => clock)).toBe(true); // another workspace or user is independent
  });

  it('shares one in-flight run between concurrent callers', async () => {
    const run = vi.fn(() => new Promise<boolean>((resolve) => setTimeout(() => resolve(true), 10)));

    const [a, b] = await Promise.all([throttledRefresh('s', run), throttledRefresh('s', run)]);

    expect([a, b]).toEqual([true, true]);
    expect(run).toHaveBeenCalledTimes(1);
  });

  it('applies only a real change, and ignores a failed read or another workspace', async () => {
    const apply = vi.fn();
    const server = (body: unknown, status = 200) => () => Promise.resolve(new Response(JSON.stringify(body), { status }));
    const shown = () => ({ slug: 'acme', role: 'Member', permissions: ['reports.view'] });

    expect(await refreshWorkspaceAccess({ fetchCurrent: server({ slug: 'acme', role: 'Member', permissions: ['reports.view'] }), getCurrent: shown, apply })).toBe(false);
    expect(await refreshWorkspaceAccess({ fetchCurrent: server({}, 500), getCurrent: shown, apply })).toBe(false);
    expect(await refreshWorkspaceAccess({ fetchCurrent: server({ slug: 'other', role: 'Member', permissions: ['x.y.view'] }), getCurrent: shown, apply })).toBe(false);
    expect(apply).not.toHaveBeenCalled();

    expect(await refreshWorkspaceAccess({ fetchCurrent: server({ slug: 'acme', role: 'Admin', permissions: ['reports.view', 'purchases.bills.view'] }), getCurrent: shown, apply })).toBe(true);
    expect(apply).toHaveBeenCalledWith({ permissions: ['reports.view', 'purchases.bills.view'], role: 'Admin' });
  });
});
