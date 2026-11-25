import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { NoAccess } from '../src/components/NoAccess';
import { adminNamesOf, clearAccessDirectoryCache, grantingRolesFor, joinRoleNames } from '../src/lib/accessDirectory';
import type { ClassifiedProblem } from '../src/lib/apiProblems';

const { authFetch, outlet, authUser } = vi.hoisted(() => ({
  authFetch: vi.fn(),
  authUser: { id: 'me' },
  outlet: { currentOrg: { id: '1', name: 'Acme', slug: 'acme', role: 'Owner' as string } },
}));

vi.mock('../src/auth/AuthContext', () => ({
  useAuth: () => ({ authFetch, user: { id: authUser.id, email: 'me@example.com' } }),
  getOrgSlugFromLocation: () => 'acme',
}));
vi.mock('react-router-dom', async () => ({
  ...(await vi.importActual<typeof import('react-router-dom')>('react-router-dom')),
  useOutletContext: () => outlet,
}));

const problem: ClassifiedProblem = {
  category: 'permission-denied',
  status: 403,
  code: 'PERMISSION_DENIED',
  requiredPermission: 'purchases.vendors.view',
  requestId: 'req-42',
};

const roles = [
  { id: 'role_full', name: 'WorkspaceFullAccess.v1', displayName: 'Workspace full access', permissions: ['purchases.vendors.view', 'sales.customers.view', 'reports.view', 'audit.logs.view'] },
  { id: 'role_buyer', name: 'Purchasing Agent', displayName: 'Purchasing Agent', permissions: ['purchases.vendors.view', 'purchases.vendors.manage', 'purchases.bills.view'] },
  { id: 'role_acct', name: 'Accountant', displayName: 'Accountant', permissions: ['purchases.vendors.view', 'financials.journal.view', 'financials.journal.post', 'sales.invoices.view', 'banking.accounts.view'] },
  { id: 'role_sales', name: 'Sales Representative', displayName: 'Sales Representative', permissions: ['sales.customers.view'] },
];
const owner = { userId: 'me', firstName: 'Olive', lastName: 'Owner', email: 'olive@example.com', role: 'Owner', status: 'Active' };
const admin = { userId: 'a1', firstName: 'Ada', lastName: 'Admin', email: 'ada@example.com', role: 'Admin', status: 'Active' };
const nameless = { userId: 'a2', firstName: '', lastName: '', email: 'ghost@example.com', role: 'Admin', status: 'Active' };
const member = { userId: 'me', firstName: 'Max', lastName: 'Member', email: 'max@example.com', role: 'Member', status: 'Active' };

function serve(opts: { members: unknown[]; requests?: unknown[]; failMembers?: boolean; post?: () => Response }) {
  authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    if (init?.method === 'POST') return opts.post ? opts.post() : new Response(JSON.stringify({ requestId: 'acreq_1', status: 'Pending' }), { status: 200 });
    if (url.endsWith('/members')) return opts.failMembers ? new Response('x', { status: 500 }) : new Response(JSON.stringify(opts.members), { status: 200 });
    if (url.endsWith('/security-roles')) return new Response(JSON.stringify(roles), { status: 200 });
    if (url.endsWith('/access-requests')) return new Response(JSON.stringify(opts.requests ?? []), { status: 200 });
    return new Response('{}', { status: 404 });
  });
}

const renderIt = () => render(<MemoryRouter><NoAccess problem={problem} /></MemoryRouter>);

beforeEach(() => {
  authUser.id = 'me';
  clearAccessDirectoryCache();
  outlet.currentOrg.role = 'Owner';
});
afterEach(() => { cleanup(); vi.clearAllMocks(); });

describe('NoAccess', () => {
  it('names the area, moves focus to the heading and never shows a status code or a permission key in the main text', async () => {
    serve({ members: [owner] });
    renderIt();

    const heading = screen.getByRole('heading', { level: 2, name: "You don't have access to Vendors" });
    expect(document.activeElement).toBe(heading);
    await screen.findByRole('link', { name: 'Set up my access' });
    const main = screen.getByTestId('no-access');
    expect(main.textContent).not.toMatch(/\b40[13]\b/);
    // The key is only inside the collapsed details.
    const visible = main.cloneNode(true) as HTMLElement;
    visible.querySelector('details')?.remove();
    expect(visible.textContent).not.toContain('purchases.vendors');
  });

  it('lists the granting roles from the live catalog, least powerful first, with full access last', async () => {
    serve({ members: [owner] });
    renderIt();

    await screen.findByText(/Access comes with these roles/);
    expect(screen.getByTestId('no-access').textContent).toContain('Purchasing Agent, Accountant or Full workspace access');
  });

  it('sends a solo owner to set up their own access with the least powerful role preselected', async () => {
    serve({ members: [owner] });
    renderIt();

    const link = await screen.findByRole('link', { name: 'Set up my access' });
    expect(link.getAttribute('href')).toBe('/org/acme/members?access=setup&grant=Purchasing%20Agent');
    expect(screen.getByRole('link', { name: 'Go to dashboard' }).getAttribute('href')).toBe('/org/acme/dashboard');
  });

  it('sends an admin of a team to request functional roles', async () => {
    outlet.currentOrg.role = 'Admin';
    serve({ members: [owner, admin] });
    renderIt();

    const link = await screen.findByRole('link', { name: 'Request functional roles' });
    expect(link.getAttribute('href')).toBe('/org/acme/members?access=request&grant=Purchasing%20Agent');
  });

  it('lets a member ask the admins: names only, one request on click, then a pending state with no second request', async () => {
    outlet.currentOrg.role = 'Member';
    serve({ members: [owner, admin, nameless, member] });
    renderIt();

    const ask = await screen.findByRole('button', { name: 'Ask an admin for access' });
    const text = screen.getByTestId('no-access').textContent ?? '';
    expect(text).toContain('Olive Owner, Ada Admin or a workspace admin');
    expect(text).not.toContain('@');
    expect(authFetch.mock.calls.some(([, init]) => init?.method === 'POST')).toBe(false); // nothing is sent until the click

    await act(async () => { fireEvent.click(ask); fireEvent.click(ask); });

    await screen.findByText(/Request sent to/);
    const posts = authFetch.mock.calls.filter(([, init]) => init?.method === 'POST');
    expect(posts).toHaveLength(1);
    expect(posts[0][0]).toBe('/api/organizations/current/access-requests');
    expect(JSON.parse(posts[0][1].body)).toEqual({ securityRoleIds: ['role_buyer'], reason: 'Needs access to Vendors' });
    expect((screen.getByRole('button', { name: 'Ask an admin for access' }) as HTMLButtonElement).disabled).toBe(true);
    expect(screen.getByText(/Request sent to/).getAttribute('role')).toBe('status');
  });

  it('shows an already pending request as pending and disables asking again', async () => {
    outlet.currentOrg.role = 'Member';
    serve({ members: [owner, member], requests: [{ status: 'Pending', targetUserId: 'me', requestedRoleIds: ['role_buyer'] }] });
    renderIt();

    await screen.findByText(/Request already sent/);
    expect((screen.getByRole('button', { name: 'Ask an admin for access' }) as HTMLButtonElement).disabled).toBe(true);
    expect(authFetch.mock.calls.some(([, init]) => init?.method === 'POST')).toBe(false);
  });

  it('says so when the request cannot be sent, without a code', async () => {
    outlet.currentOrg.role = 'Member';
    serve({ members: [owner, member], post: () => new Response('{}', { status: 500 }) });
    renderIt();

    fireEvent.click(await screen.findByRole('button', { name: 'Ask an admin for access' }));

    const note = await screen.findByText(/couldn’t be sent/);
    expect(note.textContent).not.toMatch(/\b500\b/);
  });

  it('still shows the heading, the sentence and the way back when the directory cannot be read', async () => {
    serve({ members: [], failMembers: true });
    renderIt();

    await waitFor(() => expect(authFetch).toHaveBeenCalled());
    expect(screen.getByRole('heading', { level: 2 }).textContent).toBe("You don't have access to Vendors");
    expect(screen.getByText(/doesn’t include this part of the workspace/)).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Go to dashboard' })).toBeTruthy();
    expect(screen.queryByText(/Access comes with these roles/)).toBeNull();
    expect(screen.queryByRole('button')).toBeNull();
    expect(screen.queryByRole('link', { name: 'Set up my access' })).toBeNull();
    expect(screen.queryByRole('alert')).toBeNull();
    expect(authFetch).toHaveBeenCalledTimes(3); // no retry loop
  });

  it('reads the directory once for several No-access views of the same workspace', async () => {
    serve({ members: [owner] });
    const first = renderIt();
    await screen.findByRole('link', { name: 'Set up my access' });
    first.unmount();
    renderIt();
    await screen.findByRole('link', { name: 'Set up my access' });

    expect(authFetch).toHaveBeenCalledTimes(3);
  });

  it('uses an h2 inside a page and an h1 only when it is the whole page', async () => {
    serve({ members: [owner] });
    const inPage = renderIt();
    expect(screen.queryByRole('heading', { level: 1 })).toBeNull();
    inPage.unmount();

    render(<MemoryRouter><NoAccess problem={problem} headingLevel={1} /></MemoryRouter>);
    const heading = screen.getByRole('heading', { level: 1, name: "You don't have access to Vendors" });
    expect(document.activeElement).toBe(heading);
  });

  it('shows "already sent" in every No-access view of the workspace right after a request, without another fetch', async () => {
    outlet.currentOrg.role = 'Member';
    serve({ members: [owner, member] });
    const first = renderIt();
    fireEvent.click(await screen.findByRole('button', { name: 'Ask an admin for access' }));
    await screen.findByText(/Request sent to/);
    first.unmount();
    const callsBefore = authFetch.mock.calls.length;

    renderIt(); // another page of the same workspace, well inside the cache window
    await screen.findByText(/Request already sent/);

    expect((screen.getByRole('button', { name: 'Ask an admin for access' }) as HTMLButtonElement).disabled).toBe(true);
    expect(authFetch.mock.calls.length).toBe(callsBefore); // served from the shared entry
    expect(authFetch.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(1);
  });

  it('keeps one directory per signed-in user, so another user in the same tab never sees it', async () => {
    outlet.currentOrg.role = 'Member';
    serve({ members: [owner, member] });
    const first = renderIt();
    fireEvent.click(await screen.findByRole('button', { name: 'Ask an admin for access' }));
    await screen.findByText(/Request sent to/);
    first.unmount();

    authUser.id = 'someone-else';
    renderIt();
    const ask = await screen.findByRole('button', { name: 'Ask an admin for access' });

    expect((ask as HTMLButtonElement).disabled).toBe(false);
    expect(screen.queryByText(/Request already sent/)).toBeNull();
    expect(authFetch.mock.calls.filter(([url]) => String(url).endsWith('/members'))).toHaveLength(2); // read again for the new user
  });

  it('keeps the code and request id in a collapsed Details section', async () => {
    serve({ members: [owner] });
    renderIt();

    const details = screen.getByText('Details').closest('details') as HTMLDetailsElement;
    expect(details.open).toBe(false);
    expect(details.textContent).toContain('PERMISSION_DENIED');
    expect(details.textContent).toContain('req-42');
  });

  it('says "this page" when the permission has no area name', async () => {
    serve({ members: [owner] });
    render(<MemoryRouter><NoAccess problem={{ ...problem, requiredPermission: 'something.new.view' }} /></MemoryRouter>);

    expect(screen.getByRole('heading', { level: 2 }).textContent).toBe("You don't have access to this page");
  });
});

describe('role helpers', () => {
  it('puts the fewest-permission role first, ties by name, and Full access only first when alone', () => {
    const tie = [
      { id: 'b', name: 'B', displayName: 'B role', permissions: ['x.y.view', 'x.z.view'] },
      { id: 'a', name: 'A', displayName: 'A role', permissions: ['x.y.view', 'x.z.view'] },
      { id: 'c', name: 'C', displayName: 'C role', permissions: ['x.y.view'] },
    ];
    expect(grantingRolesFor(tie, 'x.y.view').map((r) => r.id)).toEqual(['c', 'a', 'b']);

    const onlyFull = [{ id: 'f', name: 'WorkspaceFullAccess.v1', displayName: 'Whatever', permissions: ['x.y.view'] }];
    const result = grantingRolesFor(onlyFull, 'x.y.view');
    expect(result.map((r) => r.displayName)).toEqual(['Full workspace access']);
    expect(grantingRolesFor(onlyFull, 'other.permission.view')).toEqual([]);
  });

  it('accepts permissions as names or as objects', () => {
    expect(grantingRolesFor([{ id: 'r', name: 'R', displayName: 'R', permissions: [{ name: 'a.b.view' }] }], 'a.b.view')).toHaveLength(1);
  });

  it('joins names the way a sentence does', () => {
    expect(joinRoleNames([])).toBe('');
    expect(joinRoleNames(['A'])).toBe('A');
    expect(joinRoleNames(['A', 'B'])).toBe('A or B');
    expect(joinRoleNames(['A', 'B', 'C'])).toBe('A, B or C');
  });

  it('lists active owners and admins by name only, never emails', () => {
    const names = adminNamesOf([owner, admin, nameless, member, { ...admin, userId: 'x', status: 'Suspended', firstName: 'Gone' }]);
    expect(names).toEqual(['Olive Owner', 'Ada Admin', 'a workspace admin']);
  });
});
