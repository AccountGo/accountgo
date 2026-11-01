import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { Members } from '../src/pages/Members';

const { authFetch, currentOrg, caller } = vi.hoisted(() => ({
  authFetch: vi.fn(),
  currentOrg: { id: '1', name: 'Test workspace', slug: 'test' },
  caller: { role: 'Owner' },
}));

vi.mock('../src/auth/AuthContext', () => ({
  useAuth: () => ({ authFetch, user: { id: 'owner', email: 'owner@example.com' } }),
}));
vi.mock('react-router-dom', () => ({
  useOutletContext: () => ({ currentOrg }),
  useNavigate: () => vi.fn(),
}));
vi.mock('../src/lib/entitlements', () => ({
  useCapabilities: () => ({ canCreate: () => true }),
}));

beforeEach(() => {
  caller.role = 'Owner';
  authFetch.mockImplementation(async (url: string, options?: RequestInit) => {
    if (options?.method === 'POST') return new Response(null, { status: 201 });
    const value = url === '/api/organizations/current' ? { role: caller.role }
      : url.endsWith('/access-setup/status') ? { isEligibleForBootstrap: false }
      : [];
    return new Response(JSON.stringify(value), { status: 200 });
  });
});

afterEach(() => { cleanup(); vi.clearAllMocks(); });

it('lets an owner select Admin when inviting a member', async () => {
  render(<Members />);
  const invite = await screen.findByRole('button', { name: 'Invite member' });
  await waitFor(() => expect(invite.hasAttribute('disabled')).toBe(false));
  fireEvent.click(invite);
  fireEvent.change(screen.getByLabelText('Email'), { target: { value: 'admin@example.com' } });
  fireEvent.change(screen.getByLabelText('Workspace role'), { target: { value: 'Admin' } });
  fireEvent.click(screen.getByRole('button', { name: 'Send invitation' }));
  await waitFor(() => expect(authFetch).toHaveBeenCalledWith('/api/organizations/current/invitations',
    expect.objectContaining({ method: 'POST', body: JSON.stringify({ email: 'admin@example.com', role: 'Admin', securityRoleIds: [] }) })));
});

it('limits an administrator invitation to the Member workspace role', async () => {
  caller.role = 'Admin';
  render(<Members />);
  const invite = await screen.findByRole('button', { name: 'Invite member' });
  await waitFor(() => expect(invite.hasAttribute('disabled')).toBe(false));
  fireEvent.click(invite);
  expect((screen.getByLabelText('Workspace role') as HTMLSelectElement).value).toBe('Member');
  expect(screen.queryByRole('option', { name: 'Admin' })).toBeNull();
});
