import { cleanup, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { PermissionDeniedCard, RoleRestrictedCard } from '../src/components/Gating';
import IncomingReview from '../src/pages/IncomingReview';
import { clearAccessDirectoryCache } from '../src/lib/accessDirectory';

const { authFetch } = vi.hoisted(() => ({ authFetch: vi.fn() }));

vi.mock('../src/auth/AuthContext', () => ({
  useAuth: () => ({ authFetch, user: { id: 'me', email: 'me@example.com' } }),
  getOrgSlugFromLocation: () => 'acme',
}));
vi.mock('react-router-dom', async () => ({
  ...(await vi.importActual<typeof import('react-router-dom')>('react-router-dom')),
  useOutletContext: () => ({ currentOrg: { id: '1', name: 'Acme', slug: 'acme', role: 'Owner' }, hasPermission: () => false, hasModule: () => true }),
}));
vi.mock('../src/lib/entitlements', () => ({ useCapabilities: () => ({ canCreate: () => true, entitlements: null }), useOptionalCapabilities: () => null }));
vi.mock('../src/lib/integrations', () => ({ useIntegrations: () => ({ capabilities: { canReview: true } }) }));

beforeEach(() => {
  clearAccessDirectoryCache();
  authFetch.mockImplementation(async (url: string) => {
    if (String(url).includes('/review/')) {
      return new Response(JSON.stringify({ title: 'Access Denied', status: 403, code: 'PERMISSION_DENIED', requiredPermission: 'banking.accounts.view' }), { status: 403, headers: { 'content-type': 'application/problem+json' } });
    }
    return new Response('[]', { status: 200 });
  });
});
afterEach(() => { cleanup(); vi.clearAllMocks(); });

describe('PermissionDeniedCard', () => {
  it('renders the No-access view for a permission key and never prints the key or the old wording', () => {
    render(<MemoryRouter><PermissionDeniedCard permission="financials.journal.create" /></MemoryRouter>);

    expect(screen.getByRole('heading', { level: 2, name: "You don't have access to Journal entries" })).toBeTruthy();
    const visible = screen.getByTestId('no-access').cloneNode(true) as HTMLElement;
    visible.querySelector('details')?.remove();
    expect(visible.textContent).not.toContain('financials.journal');
    expect(document.body.textContent).not.toContain('Access Restricted');
  });

  it('shows the owner/admin notice without a key or policy id when the value is not a permission', () => {
    render(<MemoryRouter><PermissionDeniedCard permission="Owner or Admin" /></MemoryRouter>);

    expect(screen.getByTestId('role-restricted')).toBeTruthy();
    expect(screen.queryByTestId('no-access')).toBeNull();
    expect(document.body.textContent).not.toMatch(/Owner or Admin|POL-|\w+\.\w+\.\w+/);
  });

  it('RoleRestrictedCard names no key, policy id or status code', () => {
    render(<RoleRestrictedCard />);

    expect(document.body.textContent).not.toMatch(/POL-|\b40[13]\b|[a-z]+\.[a-z]+\.[a-z]+/);
  });
});

describe('Incoming review', () => {
  it('explains a server-side denial with the No-access view instead of a raw message', async () => {
    render(<MemoryRouter><IncomingReview /></MemoryRouter>);

    expect(await screen.findByRole('heading', { level: 2, name: "You don't have access to Bank accounts" })).toBeTruthy();
    expect(screen.getAllByRole('heading', { level: 1 })).toHaveLength(1);
    expect(document.body.textContent).not.toMatch(/Check access and refresh/);
  });
});
