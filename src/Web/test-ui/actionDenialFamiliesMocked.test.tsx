import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import React from 'react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ActionDenialRegion } from '../src/components/ActionDenialRegion';
import { BooksLockCard } from '../src/components/BooksLockCard';
import { Members } from '../src/pages/Members';
import { clearAccessDirectoryCache } from '../src/lib/accessDirectory';
import { announcePermissionDenial } from '../src/lib/permissionDenialEvents';

// The financials and members families, with the same behaviour as the real authFetch (it announces a permission denial for
// a write): these components load data when they mount, which the real signed-out provider in jsdom does not settle for.
// The announcement itself through the real authFetch is covered in permissionDenialEvents.test.tsx.
const { server, outlet } = vi.hoisted(() => ({
  server: { writeUrl: '', writeMethod: 'POST', permission: '', writes: [] as string[] },
  outlet: { currentOrg: { id: '1', name: 'Acme', slug: 'acme', role: 'Owner' }, hasModule: () => true, hasPermission: () => true },
}));

vi.mock('../src/auth/AuthContext', async () => {
  const { announcePermissionDenial: announce } = await import('../src/lib/permissionDenialEvents');
  const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
  const catalog = [
    { id: 'role_acct', name: 'Accountant', displayName: 'Accountant', permissions: ['financials.periods.close', 'admin.members.manage'] },
  ];
  const authFetch = async (url: string, init?: RequestInit) => {
    const method = (init?.method ?? 'GET').toUpperCase();
    let res: Response;
    if (method !== 'GET' && url.startsWith(server.writeUrl) && method === server.writeMethod) {
      server.writes.push(url);
      res = new Response(JSON.stringify({
        title: 'Access Denied', status: 403, detail: `You do not have the required permission '${server.permission}' to perform this action.`,
        code: 'PERMISSION_DENIED', requiredPermission: server.permission, policyId: 'POL-X',
      }), { status: 403, headers: { 'Content-Type': 'application/problem+json' } });
    } else if (url.endsWith('/books-lock')) res = json({ lockedThrough: null, history: [] });
    else if (url === '/api/organizations/current') res = json({ role: 'Owner' });
    else if (url.endsWith('/access-setup/status')) res = json({ isEligibleForBootstrap: false });
    else if (url.endsWith('/security-roles')) res = json(catalog);
    else if (url.endsWith('/members')) res = json([{ userId: 'me', firstName: 'Olive', lastName: 'Owner', role: 'Owner', status: 'Active', email: 'o@example.com', securityRoles: [], joinedAt: '2026-01-01' }]);
    else res = json([]);
    await announce(res, { action: method !== 'GET' });
    return res;
  };
  return { useAuth: () => ({ authFetch, user: { id: 'me', email: 'me@example.com' } }), getOrgSlugFromLocation: () => 'acme' };
});
vi.mock('react-router-dom', async () => ({
  ...(await vi.importActual<typeof import('react-router-dom')>('react-router-dom')),
  useOutletContext: () => outlet,
  useSearchParams: () => [new URLSearchParams(), vi.fn()],
}));
vi.mock('../src/lib/entitlements', () => ({ useCapabilities: () => ({ canCreate: () => true, canModule: () => true, canWrite: () => true, entitlements: null }) }));

const Harness: React.FC<{ children: React.ReactNode }> = ({ children }) => (
  <MemoryRouter initialEntries={['/org/acme/page']}>
    <ActionDenialRegion currentOrg={{ id: '1', slug: 'acme', role: 'Owner' }} />
    {children}
  </MemoryRouter>
);

const readableText = () => {
  const body = document.body.cloneNode(true) as HTMLElement;
  body.querySelectorAll('details').forEach((node) => node.remove());
  return body.textContent ?? '';
};
const RAW_KEY = /\b(?:sales|purchases|financials|banking|inventory|admin|reports)\.[a-z_]+(?:\.[a-z_]+)?\b/;

beforeEach(() => { clearAccessDirectoryCache(); server.writes = []; server.writeMethod = 'POST'; });
afterEach(() => { cleanup(); vi.clearAllMocks(); });

describe('a server permission denial on a real action shows the notice (financials and members)', () => {
  it('financials: saving the books lock shows the notice and a short line, not the server sentence with the key', async () => {
    server.writeUrl = '/api/organizations/current/books-lock';
    server.writeMethod = 'PUT';
    server.permission = 'financials.periods.close';
    const { container } = render(<Harness><BooksLockCard canClose /></Harness>);
    fireEvent.change(await screen.findByLabelText('Lock date'), { target: { value: '2026-10-31' } });

    fireEvent.click(container.ownerDocument.body.querySelector('button[type="submit"]') as HTMLButtonElement);

    await screen.findByTestId('action-denial-notice');
    await waitFor(() => expect(screen.getByTestId('action-denial-notice').textContent).toMatch(/To close Financial periods you need Accountant\./));
    expect(await screen.findByText("You don't have access to do that.")).toBeTruthy();
    expect(server.writes).toHaveLength(1);
    expect(document.querySelectorAll('[role="dialog"]')).toHaveLength(0);
    expect(readableText()).not.toMatch(RAW_KEY);
    expect(readableText()).not.toMatch(/\b403\b|POL-X/);
  });

  it('members: sending an invitation shows the notice, not a modal and not the raw sentence', async () => {
    server.writeUrl = '/api/organizations/current/invitations';
    server.permission = 'admin.members.manage';
    const { container } = render(<Harness><Members /></Harness>);
    const invite = await screen.findByRole('button', { name: 'Invite member' });
    await waitFor(() => expect(invite.hasAttribute('disabled')).toBe(false));
    fireEvent.click(invite);
    fireEvent.change(screen.getByLabelText('Email'), { target: { value: 'new.person@example.com' } });

    await act(async () => { fireEvent.click(container.ownerDocument.body.querySelector('button[type="submit"]') as HTMLButtonElement); });

    await screen.findByTestId('action-denial-notice');
    await waitFor(() => expect(screen.getByTestId('action-denial-notice').textContent).toMatch(/To change Members and access you need Accountant\./));
    expect(server.writes).toHaveLength(1);
    expect(document.querySelectorAll('[role="dialog"]')).toHaveLength(0);
    expect(readableText()).not.toMatch(RAW_KEY);
  });
});

// Keeps the import used: the helper is what the real authFetch calls for every write.
void announcePermissionDenial;
