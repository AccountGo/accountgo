import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { WorkspaceSetupCard } from '../src/components/WorkspaceSetupCard';
import { buildWorkspaceSetupRows } from '../src/lib/workspaceSetupLabels';

const { authFetch } = vi.hoisted(() => ({ authFetch: vi.fn() }));

vi.mock('../src/auth/AuthContext', () => ({
  useAuth: () => ({ authFetch }),
}));

beforeEach(() => authFetch.mockReset());
afterEach(cleanup);

it('lists purpose, who uses it, access, and journal posting from operating settings', async () => {
  authFetch.mockResolvedValue(new Response(JSON.stringify({
    purpose: 'Personal',
    collaborationIntent: 'WithOthers',
    collaborationState: 'TeamActive',
    ownAccessProfileDisplayName: 'Full workspace access',
    journalWorkflow: 'IndependentReviewerRequired',
    policyRevision: 1,
  }), { status: 200 }));

  render(<WorkspaceSetupCard />);

  const rows = await screen.findByTestId('workspace-setup-rows');
  expect(rows.textContent).toContain('Personal finances');
  expect(rows.textContent).toContain('Me and others');
  expect(rows.textContent).toContain('Owner · Full workspace access');
  expect(rows.textContent).toContain('Independent review');
  expect(rows.textContent).toContain('Team active');
});

it('shows an unset message when purpose and collaboration were never recorded', async () => {
  authFetch.mockResolvedValue(new Response(JSON.stringify({
    purpose: 'Unspecified',
    collaborationIntent: 'Unspecified',
    collaborationState: 'LegacyUnclassified',
    policyRevision: 0,
  }), { status: 200 }));

  render(<WorkspaceSetupCard />);

  expect(await screen.findByTestId('workspace-setup-unset')).toBeTruthy();
  expect(screen.queryByTestId('workspace-setup-rows')).toBeNull();
});

it('maps JustMe to the same labels as CreateOrg review', () => {
  const rows = Object.fromEntries(buildWorkspaceSetupRows({
    purpose: 'Business',
    collaborationIntent: 'JustMe',
    journalWorkflow: 'SingleActorAllowed',
  }));
  expect(rows.Purpose).toBe('Business or freelance work');
  expect(rows['Who uses it']).toBe('Just me');
  expect(rows['Journal posting']).toContain('prepare and post your own journals');
});
