import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { Members } from '../src/pages/Members';

const { authFetch, currentOrg, caller, hasModule } = vi.hoisted(() => ({
  authFetch: vi.fn(),
  currentOrg: { id: '1', name: 'Test workspace', slug: 'test' },
  caller: { role: 'Owner' },
  hasModule: vi.fn((_module: string) => true),
}));

vi.mock('../src/auth/AuthContext', () => ({
  useAuth: () => ({ authFetch, user: { id: 'owner', email: 'owner@example.com' } }),
}));
vi.mock('react-router-dom', () => ({
  useOutletContext: () => ({ currentOrg, hasModule }),
  useNavigate: () => vi.fn(),
}));
vi.mock('../src/lib/entitlements', () => ({
  useCapabilities: () => ({ canCreate: () => true }),
}));

beforeEach(() => {
  caller.role = 'Owner';
  hasModule.mockImplementation((_module: string) => true);
  authFetch.mockImplementation(async (url: string, options?: RequestInit) => {
    if (options?.method === 'POST') return new Response(null, { status: 201 });
    const value = url === '/api/organizations/current' ? { role: caller.role }
      : url.endsWith('/access-setup/status') ? { isEligibleForBootstrap: false }
      : [];
    return new Response(JSON.stringify(value), { status: 200 });
  });
});

afterEach(() => { cleanup(); vi.clearAllMocks(); });

it('lets a sole owner add their own functional roles', async () => {
  authFetch.mockImplementation(async (url: string, options?: RequestInit) => {
    if (options?.method === 'POST') return new Response(null, { status: 200 });
    const value = url === '/api/organizations/current' ? { role: 'Owner' }
      : url.endsWith('/access-setup/status') ? { ownerCanManageOwnAccess: true, isEligibleForBootstrap: false }
      : url.endsWith('/security-roles') ? [{ id: 3, name: 'Accountant', displayName: 'Accountant' }]
      : [];
    return new Response(JSON.stringify(value), { status: 200 });
  });
  render(<Members />);
  const buttons = await screen.findAllByRole('button', { name: 'Set up my access' });
  fireEvent.click(buttons[0]);
  fireEvent.click(screen.getByLabelText('Accountant'));
  fireEvent.click(screen.getByRole('button', { name: 'Save and activate roles' }));
  await waitFor(() => expect(authFetch).toHaveBeenCalledWith('/api/organizations/current/owner-access-setup',
    expect.objectContaining({ method: 'POST', body: JSON.stringify({ securityRoleIds: [3] }) })));
});

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

const reviewerMember = {
  memberId: 11, userId: 'reviewer', email: 'reviewer@example.com', firstName: 'Rae', lastName: 'Viewer',
  role: 'Admin', status: 'Active', joinedAt: '2026-01-01T00:00:00Z', securityRoles: [],
};

function mockLastReviewerConflict(targetRole = 'Admin') {
  authFetch.mockImplementation(async (url: string, options?: RequestInit) => {
    if (options?.method === 'DELETE' && url.includes('confirmPolicyChangeToSingleActor')) return new Response(null, { status: 204 });
    if (options?.method === 'DELETE') {
      return new Response(JSON.stringify({
        detail: 'This change would leave the workspace without a usable independent reviewer for journal posting.',
        reasonCode: 'last_reviewer_unavailable',
      }), { status: 409 });
    }
    if (url === '/api/organizations/current') return new Response(JSON.stringify({ role: caller.role }), { status: 200 });
    if (url.endsWith('/operating-settings')) return new Response(JSON.stringify({ policyRevision: 12, collaborationState: 'TeamActive' }), { status: 200 });
    if (url.endsWith('/members')) return new Response(JSON.stringify([{ ...reviewerMember, role: targetRole }]), { status: 200 });
    return new Response(JSON.stringify([]), { status: 200 });
  });
}

it('offers an Owner an explicit single-actor policy change with the current revision when removing the last reviewer', async () => {
  mockLastReviewerConflict();
  render(<Members />);

  fireEvent.click(await screen.findByRole('button', { name: 'Remove' }));
  fireEvent.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Remove member' }));

  const policyDialog = await screen.findByText('Change journal approval to single-user posting?');
  expect(policyDialog).toBeDefined();
  expect(screen.getByRole('alert').getAttribute('data-reason-code')).toBe('last_reviewer_unavailable');
  expect(screen.getByRole('alert').textContent).toContain('usable independent reviewer');

  fireEvent.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Change policy and continue' }));
  await waitFor(() => expect(authFetch).toHaveBeenCalledWith(
    '/api/organizations/current/members/11?confirmPolicyChangeToSingleActor=true&expectedPolicyRevision=12',
    expect.objectContaining({ method: 'DELETE' }),
  ));
});

it('shows the reviewer conflict without a policy-change offer to an Admin', async () => {
  caller.role = 'Admin';
  mockLastReviewerConflict('Member');
  render(<Members />);

  fireEvent.click(await screen.findByRole('button', { name: 'Remove' }));
  fireEvent.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Remove member' }));

  await waitFor(() => expect(screen.getByRole('alert').textContent).toContain('usable independent reviewer'));
  expect(screen.queryByText('Change journal approval to single-user posting?')).toBeNull();
});

const ROLE_ROWS = [
  { id: 1, name: 'WorkspaceFullAccess.v1', displayName: 'Full workspace access', permissions: ['financials.journal.post', 'sales.invoices.post'] },
  { id: 2, name: 'Accountant', displayName: 'Accountant', permissions: ['financials.journal.post', 'sales.invoices.post', 'purchases.bills.post', 'reports.view'] },
  { id: 3, name: 'Sales Representative', displayName: 'Sales Representative', permissions: ['sales.invoices.create', 'sales.invoices.view'] },
  { id: 4, name: 'Purchasing Agent', displayName: 'Purchasing Agent', permissions: ['purchases.bills.create'] },
  { id: 5, name: 'Auditor / Viewer', displayName: 'Auditor / Viewer', permissions: ['reports.view', 'financials.journal.view', 'audit.logs.view'] },
];

function mockWorkspace(handlers: { invitations?: object[]; onPost?: (url: string, options: RequestInit) => Response | undefined } = {}) {
  authFetch.mockImplementation(async (url: string, options?: RequestInit) => {
    if (options?.method === 'POST' || options?.method === 'DELETE') {
      const custom = handlers.onPost?.(url, options);
      return custom ?? new Response(null, { status: 201 });
    }
    if (url === '/api/organizations/current') return new Response(JSON.stringify({ role: caller.role }), { status: 200 });
    if (url.endsWith('/security-roles')) return new Response(JSON.stringify(ROLE_ROWS), { status: 200 });
    if (url.endsWith('/invitations')) return new Response(JSON.stringify(handlers.invitations ?? []), { status: 200 });
    if (url.endsWith('/operating-settings')) return new Response(JSON.stringify({ policyRevision: 21, collaborationState: 'TeamActive' }), { status: 200 });
    if (url.endsWith('/access-setup/status')) return new Response(JSON.stringify({ isEligibleForBootstrap: false }), { status: 200 });
    return new Response(JSON.stringify([]), { status: 200 });
  });
}

async function openInviteForm() {
  const invite = await screen.findByRole('button', { name: 'Invite member' });
  await waitFor(() => expect(invite.hasAttribute('disabled')).toBe(false));
  fireEvent.click(invite);
  fireEvent.change(screen.getByLabelText('Email'), { target: { value: 'new@example.com' } });
}

it('maps the Read-only accountant preset to the Auditor / Viewer role and previews the grant', async () => {
  mockWorkspace();
  render(<Members />);
  await openInviteForm();

  expect(screen.getByText(/Your own access will stay the same/)).toBeDefined();
  expect(screen.getByText(/does not change this approval policy/)).toBeDefined();
  expect(screen.getByText(/No functional access/)).toBeDefined();

  fireEvent.click(screen.getByLabelText(/Read-only accountant/));
  expect(screen.getByText('Reports: View only')).toBeDefined();
  expect(screen.getByText('Accounting: View only')).toBeDefined();

  fireEvent.click(screen.getByRole('button', { name: 'Send invitation' }));
  await waitFor(() => expect(authFetch).toHaveBeenCalledWith('/api/organizations/current/invitations',
    expect.objectContaining({ method: 'POST', body: JSON.stringify({ email: 'new@example.com', role: 'Member', securityRoleIds: [5] }) })));
});

it('maps the Bookkeeper preset to the post and AR/AP oriented seeded roles', async () => {
  mockWorkspace();
  render(<Members />);
  await openInviteForm();

  fireEvent.click(screen.getByLabelText(/Bookkeeper/));
  expect(screen.getByText('Accounting: Manage and post')).toBeDefined();
  fireEvent.click(screen.getByRole('button', { name: 'Send invitation' }));

  await waitFor(() => expect(authFetch).toHaveBeenCalledWith('/api/organizations/current/invitations',
    expect.objectContaining({ body: JSON.stringify({ email: 'new@example.com', role: 'Member', securityRoleIds: [2, 3, 4] }) })));
});

it('marks a preset as plan-limited when the workspace lacks some of its modules, and keeps the custom list', async () => {
  hasModule.mockImplementation((module: string) => module !== 'purchases');
  mockWorkspace();
  render(<Members />);
  await openInviteForm();

  expect(screen.getByText(/Some areas are not included in this workspace plan/)).toBeDefined();
  fireEvent.click(screen.getByLabelText('Purchasing Agent'));
  expect(screen.getByText(/Custom selection/)).toBeDefined();
});

it('shows the server detail and reason code when an invitation is refused', async () => {
  mockWorkspace({
    onPost: () => new Response(JSON.stringify({
      title: 'Independent approval required',
      detail: 'Granting this access needs approval from another Owner.',
      reasonCode: 'reviewer_required',
    }), { status: 403 }),
  });
  render(<Members />);
  await openInviteForm();
  fireEvent.click(screen.getByRole('radio', { name: /Full workspace access/ }));
  fireEvent.click(screen.getByRole('button', { name: 'Send invitation' }));

  const alert = await screen.findByRole('alert');
  expect(alert.textContent).toContain('needs approval from another Owner');
  expect(alert.getAttribute('data-reason-code')).toBe('reviewer_required');
});

const staleInvitation = {
  id: 31, email: 'late@example.com', role: 'Member', securityRoleIds: [5],
  expiresAt: '2026-01-01T00:00:00Z', expired: true, policyChanged: true,
};

it('lists expired and policy-changed invitations and resends only after explicit confirmation at the current revision', async () => {
  mockWorkspace({ invitations: [staleInvitation] });
  render(<Members />);

  expect(await screen.findByText('Expired')).toBeDefined();
  expect(screen.getByText('Approval policy changed')).toBeDefined();
  fireEvent.click(screen.getByRole('button', { name: 'Renew and resend' }));

  const dialog = await screen.findByRole('alertdialog');
  expect(within(dialog).getByText(/Journal approval policy changed since this invitation was sent/)).toBeDefined();
  expect(authFetch).not.toHaveBeenCalledWith(expect.stringContaining('/resend'), expect.anything());

  fireEvent.click(within(dialog).getByRole('button', { name: 'Confirm and resend' }));
  await waitFor(() => expect(authFetch).toHaveBeenCalledWith('/api/organizations/current/invitations/31/resend',
    expect.objectContaining({ method: 'POST', body: JSON.stringify({ confirmChangedPolicy: true, expectedPolicyRevision: 21 }) })));
});

it('offers reconfirmation when a plain resend is refused because the policy changed', async () => {
  mockWorkspace({
    invitations: [{ ...staleInvitation, id: 32, expired: false, policyChanged: false }],
    onPost: (url, options) => url.endsWith('/32/resend') && !options.body
      ? new Response(JSON.stringify({
        detail: 'Workspace journal approval policy changed since this invitation was issued.',
        reasonCode: 'policy_assumptions_changed',
      }), { status: 409 })
      : undefined,
  });
  render(<Members />);

  fireEvent.click(await screen.findByRole('button', { name: 'Resend' }));
  const dialog = await screen.findByRole('alertdialog');
  expect(screen.getByRole('alert').getAttribute('data-reason-code')).toBe('policy_assumptions_changed');

  fireEvent.click(within(dialog).getByRole('button', { name: 'Confirm and resend' }));
  await waitFor(() => expect(authFetch).toHaveBeenCalledWith('/api/organizations/current/invitations/32/resend',
    expect.objectContaining({ body: JSON.stringify({ confirmChangedPolicy: true, expectedPolicyRevision: 21 }) })));
});

it('offers return to just me from the server action list even when the setup flag is not reported', async () => {
  authFetch.mockImplementation(async (url: string, options?: RequestInit) => {
    if (options?.method === 'POST') return new Response('{}', { status: 200 });
    if (url === '/api/organizations/current') return new Response(JSON.stringify({ role: 'Owner' }), { status: 200 });
    if (url.endsWith('/operating-settings')) {
      return new Response(JSON.stringify({
        collaborationState: 'TeamSetupPending',
        allowedNextActions: ['return_to_solo'],
        policyRevision: 4,
      }), { status: 200 });
    }
    return new Response(JSON.stringify([]), { status: 200 });
  });

  render(<Members />);
  fireEvent.click(await screen.findByRole('button', { name: 'Return to just me' }));
  const dialog = await screen.findByRole('alertdialog');
  expect(within(dialog).getByText(/journal approval policy stay exactly as they are/)).toBeDefined();
  fireEvent.click(within(dialog).getByRole('button', { name: 'Return to just me' }));

  await waitFor(() => expect(authFetch).toHaveBeenCalledWith(
    '/api/organizations/current/operating-settings/return-to-solo',
    expect.objectContaining({ body: JSON.stringify({ expectedPolicyRevision: 4, revokeOutstandingInvitations: true }) }),
  ));
});

it('hides return to just me when the server does not list it', async () => {
  authFetch.mockImplementation(async (url: string) => {
    if (url === '/api/organizations/current') return new Response(JSON.stringify({ role: 'Owner' }), { status: 200 });
    if (url.endsWith('/operating-settings')) {
      return new Response(JSON.stringify({ collaborationState: 'TeamActive', allowedNextActions: [], policyRevision: 4, soloSetupEnabled: true }), { status: 200 });
    }
    return new Response(JSON.stringify([]), { status: 200 });
  });

  render(<Members />);
  await screen.findByRole('button', { name: 'Invite member' });
  expect(screen.queryByRole('button', { name: 'Return to just me' })).toBeNull();
});
