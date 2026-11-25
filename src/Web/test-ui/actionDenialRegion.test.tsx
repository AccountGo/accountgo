import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, useNavigate } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ActionDenialRegion, MAX_ACTION_NOTICES } from '../src/components/ActionDenialRegion';
import { actionNotices } from '../src/lib/actionDenialNotices';
import { clearAccessDirectoryCache } from '../src/lib/accessDirectory';
import { PERMISSION_DENIED_EVENT, type PermissionDeniedDetail } from '../src/lib/permissionDenialEvents';

const { authFetch, authUser } = vi.hoisted(() => ({ authFetch: vi.fn(), authUser: { id: 'me' } }));

vi.mock('../src/auth/AuthContext', () => ({
  useAuth: () => ({ authFetch, user: { id: authUser.id, email: 'me@example.com' } }),
  getOrgSlugFromLocation: () => 'acme',
}));

const roles = [
  { id: 'role_full', name: 'WorkspaceFullAccess.v1', displayName: 'Workspace full access', permissions: ['purchases.bills.post', 'sales.invoices.post', 'purchases.vendors.manage', 'sales.customers.manage'] },
  { id: 'role_buyer', name: 'Purchasing Agent', displayName: 'Purchasing Agent', permissions: ['purchases.bills.post', 'purchases.vendors.manage'] },
  { id: 'role_acct', name: 'Accountant', displayName: 'Accountant', permissions: ['purchases.bills.post', 'sales.invoices.post', 'financials.journal.post', 'banking.accounts.view'] },
];
const owner = { userId: 'me', firstName: 'Olive', lastName: 'Owner', role: 'Owner', status: 'Active' };
const admin = { userId: 'a1', firstName: 'Ada', lastName: 'Admin', role: 'Admin', status: 'Active' };
const member = { userId: 'me', firstName: 'Max', lastName: 'Member', role: 'Member', status: 'Active' };

function serve(members: unknown[], requests: unknown[] = []) {
  authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    if (init?.method === 'POST') return new Response(JSON.stringify({ requestId: 'acreq_1', status: 'Pending' }), { status: 200 });
    if (String(url).endsWith('/members')) return new Response(JSON.stringify(members), { status: 200 });
    if (String(url).endsWith('/security-roles')) return new Response(JSON.stringify(roles), { status: 200 });
    if (String(url).endsWith('/access-requests')) return new Response(JSON.stringify(requests), { status: 200 });
    return new Response('{}', { status: 404 });
  });
}

const deny = (permission: string, action = true, requestId?: string) =>
  act(() => {
    window.dispatchEvent(new CustomEvent<PermissionDeniedDetail>(PERMISSION_DENIED_EVENT, {
      detail: { action, problem: { category: 'permission-denied', status: 403, code: 'PERMISSION_DENIED', requiredPermission: permission, requestId } },
    }));
  });

let navigate: (to: string) => void = () => {};
const Nav: React.FC = () => { navigate = useNavigate(); return null; };
const org = { id: '1', slug: 'acme', role: 'Owner' as string };

const mount = (currentOrg = org) => render(<MemoryRouter initialEntries={['/org/acme/purchases']}><Nav /><ActionDenialRegion currentOrg={currentOrg} /></MemoryRouter>);

beforeEach(() => { authUser.id = 'me'; clearAccessDirectoryCache(); actionNotices.reset(); org.role = 'Owner'; serve([owner]); });
afterEach(() => { cleanup(); vi.clearAllMocks(); actionNotices.reset(); });

describe('ActionDenialRegion', () => {
  it('is a polite live region that exists before any notice and is never a dialog', async () => {
    mount();
    const region = screen.getByTestId('action-denial-region');
    expect(region.getAttribute('role')).toBe('status');
    expect(region.getAttribute('aria-live')).toBe('polite');

    deny('purchases.bills.post');
    await screen.findByTestId('action-denial-notice');

    expect(screen.queryByRole('dialog')).toBeNull();
    expect(screen.queryByRole('alertdialog')).toBeNull();
    expect(region.contains(screen.getByTestId('action-denial-notice'))).toBe(true);
  });

  it('says which roles the action needs, in plain words, with no raw key outside Details', async () => {
    mount();
    deny('purchases.bills.post', true, 'req-7');

    await screen.findByText('To post Bills you need Purchasing Agent, Accountant or Full workspace access.');
    const notice = screen.getByTestId('action-denial-notice').cloneNode(true) as HTMLElement;
    const details = notice.querySelector('details') as HTMLElement;
    expect(details.textContent).toContain('PERMISSION_DENIED');
    expect(details.textContent).toContain('purchases.bills.post');
    expect(details.textContent).toContain('req-7');
    details.remove();
    expect(notice.textContent).not.toContain('purchases.bills');
    expect(notice.textContent).not.toMatch(/\b40[13]\b|POL-/);
  });

  it('falls back to a plain sentence when the action or the roles are unknown', async () => {
    authFetch.mockImplementation(async () => new Response('x', { status: 500 })); // directory cannot be read
    mount();
    deny('purchases.bills.post');
    deny('something.new.view');

    expect(await screen.findByText("You don't have access to post Bills.")).toBeTruthy();
    expect(screen.getByText("You don't have access to do that.")).toBeTruthy();
  });

  it('does not show a notice for a denied read, which its page already explains', async () => {
    mount();
    deny('purchases.vendors.view', false);

    await new Promise((resolve) => setTimeout(resolve, 30));
    expect(screen.queryByTestId('action-denial-notice')).toBeNull();
  });

  it('does not take focus when a notice appears', async () => {
    const button = document.createElement('button');
    document.body.appendChild(button);
    button.focus();
    mount();

    deny('purchases.bills.post');
    await screen.findByTestId('action-denial-notice');

    expect(document.activeElement).toBe(button);
    button.remove();
  });

  it('offers a solo owner to set up their own access, with the least powerful role preselected', async () => {
    mount();
    deny('purchases.bills.post');

    const link = await screen.findByRole('link', { name: 'Set up my access' });
    expect(link.getAttribute('href')).toBe('/org/acme/members?access=setup&grant=Purchasing%20Agent');
  });

  it('offers an owner or admin of a team to request roles', async () => {
    org.role = 'Admin';
    serve([owner, admin]);
    mount();
    deny('purchases.bills.post');

    const link = await screen.findByRole('link', { name: 'Request functional roles' });
    expect(link.getAttribute('href')).toBe('/org/acme/members?access=request&grant=Purchasing%20Agent');
  });

  it('lets a member ask an admin with one request per click, then shows it as sent', async () => {
    org.role = 'Member';
    serve([owner, member]);
    mount();
    deny('purchases.bills.post');

    const ask = await screen.findByRole('button', { name: 'Ask an admin for access' });
    await act(async () => { fireEvent.click(ask); fireEvent.click(ask); });

    await screen.findByText(/Request sent to Olive Owner/);
    const posts = authFetch.mock.calls.filter(([, init]) => init?.method === 'POST');
    expect(posts).toHaveLength(1);
    expect(JSON.parse(posts[0][1].body)).toEqual({ securityRoleIds: ['role_buyer'], reason: 'Needs access to Bills' });
    expect((screen.getByRole('button', { name: 'Ask an admin for access' }) as HTMLButtonElement).disabled).toBe(true);
  });

  it('shows a request already pending as pending', async () => {
    org.role = 'Member';
    serve([owner, member], [{ status: 'Pending', targetUserId: 'me', requestedRoleIds: ['role_buyer'] }]);
    mount();
    deny('purchases.bills.post');

    await screen.findByText(/Request already sent/);
    expect(authFetch.mock.calls.some(([, init]) => init?.method === 'POST')).toBe(false);
  });

  it('can be dismissed, and dismissing one leaves the others', async () => {
    mount();
    deny('purchases.bills.post');
    deny('sales.invoices.post');
    await waitFor(() => expect(screen.getAllByTestId('action-denial-notice')).toHaveLength(2));

    fireEvent.click(within(screen.getAllByTestId('action-denial-notice')[0]).getByRole('button', { name: 'Dismiss this notice' }));

    expect(screen.getAllByTestId('action-denial-notice')).toHaveLength(1);
  });

  it('replaces a notice for the same permission instead of stacking, and keeps at most three', async () => {
    mount();
    deny('purchases.bills.post');
    deny('purchases.bills.post');
    await waitFor(() => expect(screen.getAllByTestId('action-denial-notice')).toHaveLength(1));

    deny('sales.invoices.post');
    deny('purchases.vendors.manage');
    deny('sales.customers.manage');
    await waitFor(() => expect(screen.getAllByTestId('action-denial-notice')).toHaveLength(MAX_ACTION_NOTICES));

    expect(screen.queryByText(/post Bills/)).toBeNull(); // the oldest was dropped
  });

  it('clears the notices when the route changes and when the workspace changes', async () => {
    const view = mount();
    deny('purchases.bills.post');
    await screen.findByTestId('action-denial-notice');

    act(() => navigate('/org/acme/sales'));
    await waitFor(() => expect(screen.queryByTestId('action-denial-notice')).toBeNull());

    deny('sales.invoices.post');
    await screen.findByTestId('action-denial-notice');
    view.rerender(<MemoryRouter initialEntries={['/org/acme/purchases']}><Nav /><ActionDenialRegion currentOrg={{ ...org, id: '2', slug: 'other' }} /></MemoryRouter>);
    await waitFor(() => expect(screen.queryByTestId('action-denial-notice')).toBeNull());
  });

  it('keeps its notices when the region remounts inside the same context, as it does when a permission refresh remounts the shell', async () => {
    const first = mount();
    deny('purchases.bills.post');
    await screen.findByTestId('action-denial-notice');

    first.unmount();
    mount(); // same user, workspace and route

    expect(await screen.findByTestId('action-denial-notice')).toBeTruthy();
  });

  it('clears the notices when a different user signs in on the same page', async () => {
    const view = mount();
    deny('purchases.bills.post');
    expect(await screen.findByTestId('action-denial-notice')).toBeTruthy();

    view.unmount();
    authUser.id = 'someone-else';
    mount();
    await waitFor(() => expect(screen.queryByTestId('action-denial-notice')).toBeNull());
  });

  it('sits above the phone bottom bar and the safe-area inset, full width on a phone, and does not block the page', () => {
    mount();
    const region = screen.getByTestId('action-denial-region');

    expect(region.className).toContain('fixed');
    expect(region.className).toContain('bottom-[calc(env(safe-area-inset-bottom,0px)+4.5rem)]'); // 4rem bar + inset + a gap
    expect(region.className).toContain('md:bottom-4');
    expect(region.className).toContain('left-4');
    expect(region.className).toContain('right-4');
    expect(region.className).toContain('pointer-events-none'); // only the notices themselves take clicks
  });

  it('puts the next-step link only in the notice, never in the page banner line', () => {
    // The banner line for a failed action is a bare sentence (see describeProblem); the link lives only here.
    mount();
    expect(screen.queryByRole('link')).toBeNull();
  });
});
