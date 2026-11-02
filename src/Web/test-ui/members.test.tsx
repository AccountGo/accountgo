import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
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

it('renders solo setup confirmation banner and calls solo-setup/confirm with expected revision', async () => {
  authFetch.mockImplementation(async (url: string, options?: RequestInit) => {
    if (options?.method === 'POST') return new Response(null, { status: 200 });
    if (url === '/api/organizations/current') return new Response(JSON.stringify({ role: 'Owner' }), { status: 200 });
    if (url.endsWith('/access-setup/status')) return new Response(JSON.stringify({ isEligibleForBootstrap: false }), { status: 200 });
    if (url.endsWith('/solo-setup/eligibility')) {
      return new Response(JSON.stringify({
        status: 'Eligible',
        reasonCode: 'eligible',
        message: 'This workspace appears to have always been solo.',
        policyRevision: 5,
      }), { status: 200 });
    }
    if (url.endsWith('/operating-settings')) {
      return new Response(JSON.stringify({
        collaborationState: 'LegacyUnclassified',
        policyRevision: 5,
      }), { status: 200 });
    }
    return new Response(JSON.stringify([]), { status: 200 });
  });

  render(<Members />);
  const confirmBtns = await screen.findAllByRole('button', { name: 'Confirm solo setup' });
  expect(screen.getByText('Solo Workspace Setup Available')).toBeDefined();
  fireEvent.click(confirmBtns[0]);

  const dialog = await screen.findByRole('alertdialog');
  const dialogConfirm = within(dialog).getByRole('button', { name: 'Confirm solo setup' });
  fireEvent.click(dialogConfirm);

  await waitFor(() =>
    expect(authFetch).toHaveBeenCalledWith(
      '/api/organizations/current/solo-setup/confirm',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ expectedPolicyRevision: 5 }),
      })
    )
  );
});

it('renders controlled recovery banner when solo setup returns RecoveryRequired', async () => {
  authFetch.mockImplementation(async (url: string) => {
    if (url === '/api/organizations/current') return new Response(JSON.stringify({ role: 'Owner' }), { status: 200 });
    if (url.endsWith('/solo-setup/eligibility')) {
      return new Response(JSON.stringify({
        status: 'RecoveryRequired',
        reasonCode: 'recovery_required',
        message: 'History is ambiguous and requires controlled recovery.',
        policyRevision: 2,
      }), { status: 200 });
    }
    return new Response(JSON.stringify([]), { status: 200 });
  });

  render(<Members />);
  expect(await screen.findByText('Controlled Recovery Required')).toBeDefined();
  expect(screen.getByText('History is ambiguous and requires controlled recovery.')).toBeDefined();
});

it('renders your access summary for solo active workspace', async () => {
  authFetch.mockImplementation(async (url: string) => {
    if (url === '/api/organizations/current') return new Response(JSON.stringify({ role: 'Owner' }), { status: 200 });
    if (url.endsWith('/operating-settings')) {
      return new Response(JSON.stringify({
        collaborationState: 'SoloActive',
        ownAccessProfileDisplayName: 'Full workspace access',
        journalWorkflow: 'SingleActorAllowed',
        policyRevision: 3,
      }), { status: 200 });
    }
    return new Response(JSON.stringify([]), { status: 200 });
  });

  render(<Members />);
  expect(await screen.findByText('Your access summary')).toBeDefined();
  expect(screen.getByText('Solo workspace (just me)')).toBeDefined();
  expect(screen.getByText('Full workspace access')).toBeDefined();
  expect(screen.getByText(/single-user workflow/i)).toBeDefined();
});

it('offers return to solo and executes return-to-solo endpoint with policy revision', async () => {
  authFetch.mockImplementation(async (url: string, options?: RequestInit) => {
    if (options?.method === 'POST') return new Response(null, { status: 200 });
    if (url === '/api/organizations/current') return new Response(JSON.stringify({ role: 'Owner' }), { status: 200 });
    if (url.endsWith('/operating-settings')) {
      return new Response(JSON.stringify({
        collaborationState: 'TeamActive',
        allowedNextActions: ['return_to_solo'],
        policyRevision: 7,
        soloSetupEnabled: true,
      }), { status: 200 });
    }
    return new Response(JSON.stringify([]), { status: 200 });
  });

  render(<Members />);
  const returnBtn = await screen.findByRole('button', { name: 'Return to just me' });
  fireEvent.click(returnBtn);

  const dialog = await screen.findByRole('alertdialog');
  const dialogConfirm = within(dialog).getByRole('button', { name: 'Return to just me' });
  fireEvent.click(dialogConfirm);

  await waitFor(() =>
    expect(authFetch).toHaveBeenCalledWith(
      '/api/organizations/current/operating-settings/return-to-solo',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ expectedPolicyRevision: 7, revokeOutstandingInvitations: true }),
      })
    )
  );
});
