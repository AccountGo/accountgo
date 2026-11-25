import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { OwnerFinancialAccessBanner } from '../src/components/OwnerFinancialAccessBanner';

const { authFetch, authUser } = vi.hoisted(() => ({ authFetch: vi.fn(), authUser: { id: 'u1' } }));
vi.mock('../src/auth/AuthContext', () => ({ useAuth: () => ({ authFetch, user: authUser }) }));

const setupPath = '/api/organizations/current/owner-access-setup/full-access';
let server: { oneClick: object; status: object; solo: object };
const KEY = (user: string, org: string) => `accountgo.setupAccessPrompt.v1:${user}:${org}`;

const mount = (props: Partial<React.ComponentProps<typeof OwnerFinancialAccessBanner>> = {}) => render(
  <MemoryRouter><OwnerFinancialAccessBanner slug="acme" orgId="org_1" onAccessGranted={() => undefined} {...props} /></MemoryRouter>,
);

beforeEach(() => {
  window.localStorage.clear();
  authUser.id = 'u1';
  server = {
    oneClick: { eligible: false },
    status: { ownerCanManageOwnAccess: true, totalFunctionalRoles: 0 },
    solo: { status: 'NotEligible' },
  };
  authFetch.mockImplementation(async (path: string) => {
    if (path === setupPath) return Response.json(server.oneClick);
    if (path.endsWith('/access-setup/status')) return Response.json(server.status);
    if (path.endsWith('/solo-setup/eligibility')) return Response.json(server.solo);
    return Response.json({});
  });
});
afterEach(() => { cleanup(); vi.clearAllMocks(); vi.restoreAllMocks(); });

describe('the "Set up your access" prompt on the dashboard', () => {
  it('shows for a new owner with no functional role, with the Team & Members setup as its action', async () => {
    mount();

    expect(await screen.findByRole('heading', { name: 'Set up your access' })).toBeTruthy();
    const action = screen.getByRole('link', { name: 'Set up your access' });
    expect(action.getAttribute('href')).toBe('/org/acme/members?access=setup');
  });

  it('keeps the one-click grant when the server offers it, and does not also show the setup variant', async () => {
    server.oneClick = { eligible: true };
    mount();

    expect(await screen.findByRole('button', { name: 'Give me full workspace access' })).toBeTruthy();
    expect(screen.queryByRole('link', { name: 'Set up your access' })).toBeNull();
    expect(authFetch).not.toHaveBeenCalledWith(expect.stringContaining('/access-setup/status'), expect.anything());
  });

  it('never shows once the owner holds any functional role, or when they cannot manage their own access', async () => {
    server.status = { ownerCanManageOwnAccess: true, totalFunctionalRoles: 1 };
    const withRole = mount();
    await waitFor(() => expect(authFetch).toHaveBeenCalledWith(expect.stringContaining('/access-setup/status'), expect.anything()));
    expect(screen.queryByRole('heading', { name: 'Set up your access' })).toBeNull();
    withRole.unmount();

    server.status = { ownerCanManageOwnAccess: false, totalFunctionalRoles: 0 };
    mount();
    await waitFor(() => expect(authFetch.mock.calls.filter(([path]) => String(path).endsWith('/access-setup/status'))).toHaveLength(2));
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(screen.queryByRole('heading', { name: 'Set up your access' })).toBeNull();
  });

  it('leaves the solo path to Members when the owner is eligible for it', async () => {
    server.solo = { status: 'Eligible' };
    mount();
    await waitFor(() => expect(authFetch).toHaveBeenCalledWith(expect.stringContaining('/solo-setup/eligibility'), expect.anything()));
    expect(screen.queryByRole('heading', { name: 'Set up your access' })).toBeNull();
  });

  it('does not apply to personal books', async () => {
    mount({ isPersonalBooks: true });
    await waitFor(() => expect(authFetch).toHaveBeenCalledWith(setupPath, expect.anything()));
    expect(authFetch).not.toHaveBeenCalledWith(expect.stringContaining('/access-setup/status'), expect.anything());
    expect(screen.queryByRole('heading', { name: 'Set up your access' })).toBeNull();
  });

  it('is dismissible, remembered per user and workspace, and does not hide other people or workspaces', async () => {
    const first = mount();
    fireEvent.click(await screen.findByRole('button', { name: 'Not now' }));
    expect(screen.queryByRole('heading', { name: 'Set up your access' })).toBeNull();
    expect(window.localStorage.getItem(KEY('u1', 'org_1'))).toBe('1');
    first.unmount();

    mount(); // same user, same workspace: stays dismissed
    await waitFor(() => expect(authFetch).toHaveBeenCalledWith(setupPath, expect.anything()));
    expect(screen.queryByRole('heading', { name: 'Set up your access' })).toBeNull();
    cleanup();

    mount({ orgId: 'org_2' }); // another workspace of the same user
    expect(await screen.findByRole('heading', { name: 'Set up your access' })).toBeTruthy();
    cleanup();

    authUser.id = 'u2'; // another user in the same workspace
    mount();
    expect(await screen.findByRole('heading', { name: 'Set up your access' })).toBeTruthy();
  });

  it('shows again, without breaking, when storage cannot be read or written', async () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('blocked'); });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('blocked'); });
    mount();

    fireEvent.click(await screen.findByRole('button', { name: 'Not now' }));

    expect(screen.queryByRole('heading', { name: 'Set up your access' })).toBeNull(); // hidden for this visit
  });
});
