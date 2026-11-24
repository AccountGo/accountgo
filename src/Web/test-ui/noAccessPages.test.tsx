import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { clearAccessDirectoryCache } from '../src/lib/accessDirectory';
import { Parties } from '../src/pages/Parties';
import { ProductsServices } from '../src/pages/ProductsServices';
import { Bills } from '../src/pages/Bills';
import { Invoices } from '../src/pages/Invoices';
import BankingTransactionsPage from '../src/pages/BankingTransactions';

const { authFetch, outlet } = vi.hoisted(() => ({
  authFetch: vi.fn(),
  outlet: {
    currentOrg: { id: '1', name: 'Acme', slug: 'acme', role: 'Owner', currency: 'USD' } as Record<string, unknown>,
    userPermissions: [] as string[],
    hasPermission: () => false,
    canAccess: () => false,
    hasModule: () => true,
    canWrite: () => true,
    subscription: { activeModules: [], planId: '' },
  },
}));

vi.mock('../src/auth/AuthContext', () => ({
  useAuth: () => ({ authFetch, user: { id: 'me', email: 'me@example.com' } }),
  getOrgSlugFromLocation: () => 'acme',
}));
vi.mock('react-router-dom', async () => ({
  ...(await vi.importActual<typeof import('react-router-dom')>('react-router-dom')),
  useOutletContext: () => outlet,
}));
vi.mock('../src/lib/entitlements', () => ({
  useCapabilities: () => ({ canCreate: () => true, canStatementImport: false, entitlements: null }),
  useOptionalCapabilities: () => null,
  useEntitlements: () => ({ entitlements: null }),
}));

const denied = (permission: string) =>
  new Response(JSON.stringify({ title: 'Access Denied', status: 403, code: 'PERMISSION_DENIED', requiredPermission: permission }), {
    status: 403,
    headers: { 'content-type': 'application/problem+json' },
  });

function serve(listPattern: RegExp, response: () => Response) {
  authFetch.mockImplementation(async (url: string) => {
    if (listPattern.test(url)) return response();
    if (url.endsWith('/members')) return new Response(JSON.stringify([{ userId: 'me', firstName: 'Olive', lastName: 'Owner', role: 'Owner', status: 'Active' }]), { status: 200 });
    if (url.endsWith('/security-roles')) return new Response(JSON.stringify([]), { status: 200 });
    if (url.endsWith('/access-requests')) return new Response('[]', { status: 200 });
    return new Response('[]', { status: 200 });
  });
}

beforeEach(() => clearAccessDirectoryCache());
afterEach(() => { cleanup(); vi.clearAllMocks(); });

describe('a page that cannot load because of a missing permission', () => {
  it.each([
    ['Vendors', /Purchasing\/Vendors|Vendors/, 'purchases.vendors.view', () => <Parties role="vendor" />, 'Vendors'],
    ['Clients', /Customers|Clients/, 'sales.customers.view', () => <Parties role="client" />, 'Clients'],
    ['Products and services', /Catalog\/Items/, 'inventory.items.view', () => <ProductsServices />, 'Products and services'],
    ['Bills', /Purchasing\/Bills/, 'purchases.bills.view', () => <Bills />, 'Bills'],
    ['Invoices', /SalesInvoices/, 'sales.invoices.view', () => <Invoices />, 'Invoices'],
    ['Bank transactions', /Banking\/Accounts/, 'banking.accounts.view', () => <BankingTransactionsPage />, 'Bank accounts'],
  ])('%s shows the No-access page instead of an error and an empty list', async (_name, listUrl, permission, page, area) => {
    serve(listUrl, () => denied(permission));
    render(<MemoryRouter>{page()}</MemoryRouter>);

    expect(await screen.findByRole('heading', { level: 1, name: `You don't have access to ${area}` })).toBeTruthy();
    expect(document.body.textContent).not.toMatch(/\(40[13]\)/);
    expect(document.body.textContent).not.toMatch(/could not be loaded/i);
    expect(screen.queryByText(/No vendors yet|No clients yet|Add a vendor|Add your first/i)).toBeNull();
  });

  it('a plain server error still shows the page banner and not the No-access page', async () => {
    serve(/Purchasing\/Bills/, () => new Response(JSON.stringify({ title: 'Boom' }), { status: 500, headers: { 'content-type': 'application/problem+json' } }));
    render(<MemoryRouter><Bills /></MemoryRouter>);

    await waitFor(() => expect(screen.getByRole('alert').textContent).toContain('Bills could not be loaded.'));
    expect(screen.queryByTestId('no-access')).toBeNull();
    expect(document.body.textContent).not.toMatch(/\b500\b/);
  });

  it('a 403 with no recognised code gets a plain line, not a status code and not the No-access page', async () => {
    serve(/Purchasing\/Bills/, () => new Response('Forbidden', { status: 403 }));
    render(<MemoryRouter><Bills /></MemoryRouter>);

    await waitFor(() => expect(screen.getByRole('alert').textContent).toContain("You don't have access to this."));
    expect(screen.queryByTestId('no-access')).toBeNull();
    expect(document.body.textContent).not.toMatch(/\(403\)/);
  });
});
