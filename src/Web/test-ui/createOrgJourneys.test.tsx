import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { CreateOrgPage } from '../src/pages/CreateOrg/CreateOrgPage';

const { authFetch, searchParams } = vi.hoisted(() => ({
  authFetch: vi.fn(),
  searchParams: new URLSearchParams('name=Acme&slug=acme'),
}));

vi.mock('../src/auth/AuthContext', () => ({
  useAuth: () => ({
    authFetch,
    user: { id: 'owner-1', email: 'owner@example.com' },
  }),
}));

vi.mock('react-router-dom', () => ({
  useNavigate: () => vi.fn(),
  useSearchParams: () => [searchParams],
}));

const mockStorage = (() => {
  let store: Record<string, string> = {};
  return {
    getItem: (key: string) => store[key] ?? null,
    setItem: (key: string, value: string) => {
      store[key] = String(value);
    },
    removeItem: (key: string) => {
      delete store[key];
    },
    clear: () => {
      store = {};
    },
  };
})();

if (typeof window !== 'undefined') {
  Object.defineProperty(window, 'localStorage', { value: mockStorage, writable: true });
}

beforeEach(() => {
  mockStorage.clear();
  searchParams.set('name', 'Acme');
  searchParams.set('slug', 'acme');
});

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
  mockStorage.clear();
});

async function advanceToReview() {
  // Step 1: About -> Step 2: Intent
  fireEvent.click(screen.getByRole('button', { name: /Next/i }));
  await screen.findByRole('progressbar', { name: 'Step 2 of 6' });
  // Step 2: Intent -> Step 3: Identity
  fireEvent.click(screen.getByRole('button', { name: /Next/i }));
  await screen.findByRole('progressbar', { name: 'Step 3 of 6' });
  // Step 3: Identity -> Step 4: MoneyTime
  fireEvent.click(screen.getByRole('button', { name: /Next/i }));
  await screen.findByRole('progressbar', { name: 'Step 4 of 6' });
  // Step 4: MoneyTime -> Step 5: Reporting
  fireEvent.click(screen.getByRole('button', { name: /Next/i }));
  await screen.findByRole('progressbar', { name: 'Step 5 of 6' });
  // Step 5: Reporting -> Step 6: Review
  fireEvent.click(screen.getByRole('button', { name: /Next/i }));
  await screen.findByRole('progressbar', { name: 'Step 6 of 6' });
}

describe('CreateOrg wizard capability-aware journeys', () => {
  it('known-disabled journey: hides unapplicable choices, clearly states they are not saved, and agrees with persisted payload', async () => {
    const postCalls: any[] = [];
    authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (url === '/api/organizations/setup-capabilities') {
        return new Response(
          JSON.stringify({
            soloSetupEnabled: false,
            personalPurposeEnabled: false,
            controlledRecoveryEnabled: false,
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        );
      }
      if (url === '/api/organizations' && init?.method === 'POST') {
        postCalls.push(JSON.parse(String(init.body)));
        return new Response(JSON.stringify({ slug: 'acme' }), {
          status: 201,
          headers: { 'Content-Type': 'application/json' },
        });
      }
      return new Response(null, { status: 404 });
    });

    render(<CreateOrgPage />);

    // About step: notice is displayed explaining standard controls and that purpose/collaboration choices are not saved
    const note = await screen.findByRole('note');
    expect(note.textContent).toContain('Standard workspace setup');
    expect(note.textContent).toContain(
      'Solo workspace auto-initialization is disabled; workspaces are created under standard access controls.'
    );
    expect(note.textContent).toContain(
      'Workspace purpose and collaboration choices cannot be applied and will not be saved.'
    );

    // Unapplicable choices are hidden from customer
    expect(screen.queryByDisplayValue('Business')).toBeNull();
    expect(screen.queryByDisplayValue('Personal')).toBeNull();
    expect(screen.queryByDisplayValue('JustMe')).toBeNull();
    expect(screen.queryByDisplayValue('WithOthers')).toBeNull();

    // Advance to Review step without being blocked for About choices
    await advanceToReview();

    // Review step: does NOT present Purpose or Who uses it as part of the workspace being created
    expect(screen.queryByText('Purpose')).toBeNull();
    expect(screen.queryByText('Who uses it')).toBeNull();

    // Clearly states before confirmation that purpose and collaboration choices will not be saved
    const reviewNote = screen.getByRole('note');
    expect(reviewNote.textContent).toContain('Standard workspace setup');
    expect(reviewNote.textContent).toContain(
      'Workspace purpose and collaboration choices are not saved and will not be applied to this workspace.'
    );

    // Review displays standard access controls
    expect(screen.getByText('Owner · Standard access setup required')).toBeTruthy();
    expect(
      screen.getByText('Standard accounting controls apply until solo workflow is activated.')
    ).toBeTruthy();
    expect(
      screen.getByText(
        'Standard access setup is required after creation. You can assign functional roles and configure permissions in Members settings.'
      )
    ).toBeTruthy();

    // Submit workspace creation
    fireEvent.click(screen.getByRole('button', { name: 'Create workspace' }));

    await screen.findByText('Your workspace is ready');

    // Verify exactly one POST call occurred and persisted payload matches the reviewed information
    expect(postCalls).toHaveLength(1);
    expect(postCalls[0].workspacePurpose).toBeUndefined();
    expect(postCalls[0].collaborationIntent).toBeUndefined();
    expect(postCalls[0].ownerAccessPreset).toBeUndefined();
    expect(postCalls[0].initialJournalWorkflow).toBeUndefined();
    expect(postCalls[0].name).toBe('Acme');
    expect(postCalls[0].slug).toBe('acme');
  });

  it('stale-capability journey: on 422 solo_setup_disabled, removes discarded choices from review, clearly states they are not saved, and agrees with persisted payload', async () => {
    let postCount = 0;
    const postCalls: any[] = [];
    authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (url === '/api/organizations/setup-capabilities') {
        // Initially returns true, or on refresh after 422 returns false
        return new Response(
          JSON.stringify({
            soloSetupEnabled: postCount === 0,
            personalPurposeEnabled: false,
            controlledRecoveryEnabled: false,
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        );
      }
      if (url === '/api/organizations' && init?.method === 'POST') {
        postCount++;
        const parsed = JSON.parse(String(init.body));
        postCalls.push(parsed);
        if (postCount === 1) {
          // Reject first call with 422 solo_setup_disabled
          return new Response(
            JSON.stringify({
              status: 422,
              reasonCode: 'solo_setup_disabled',
              detail: 'Solo workspace auto-initialization is disabled.',
            }),
            { status: 422, headers: { 'Content-Type': 'application/json' } }
          );
        }
        return new Response(JSON.stringify({ slug: 'acme' }), {
          status: 201,
          headers: { 'Content-Type': 'application/json' },
        });
      }
      return new Response(null, { status: 404 });
    });

    render(<CreateOrgPage />);

    // Wait for setup capabilities to load
    await waitFor(() => {
      expect(screen.queryByRole('note')).toBeNull();
    });

    // Choices are visible when enabled
    fireEvent.click(screen.getByDisplayValue('Business'));
    fireEvent.click(screen.getByDisplayValue('JustMe'));

    // Advance to Review
    await advanceToReview();

    // Review step initially shows full access and single-actor posting
    expect(screen.getByText('Purpose')).toBeTruthy();
    expect(screen.getByText('Business or freelance work')).toBeTruthy();
    expect(screen.getByText('Who uses it')).toBeTruthy();
    expect(screen.getByText('Just me')).toBeTruthy();
    expect(screen.getByText('Owner · Full workspace access')).toBeTruthy();
    expect(
      screen.getByText('You can prepare and post your own journals under the single-user workflow.')
    ).toBeTruthy();

    // First attempt: click Create workspace
    fireEvent.click(screen.getByRole('button', { name: 'Create workspace' }));

    // Assert alert is shown to user and wizard DOES NOT silently auto-retry
    const alert = await screen.findByRole('alert');
    expect(alert.textContent).toContain(
      'Solo setup is not enabled. Review and confirm the updated access and accounting policy before creating your workspace.'
    );

    // Verify exactly 1 POST call has happened
    expect(postCalls).toHaveLength(1);
    expect(postCalls[0].workspacePurpose).toBe('Business');
    expect(postCalls[0].collaborationIntent).toBe('JustMe');
    expect(postCalls[0].ownerAccessPreset).toBe('FullWorkspaceAccess');
    expect(postCalls[0].initialJournalWorkflow).toBe('SingleActorAllowed');

    // Review step is updated: discarded choices are REMOVED from the created workspace summary
    expect(screen.queryByText('Purpose')).toBeNull();
    expect(screen.queryByText('Who uses it')).toBeNull();
    expect(screen.queryByText('Business or freelance work')).toBeNull();

    // Clearly states before confirmation that purpose and collaboration choices will not be saved
    const reviewNote = screen.getByRole('note');
    expect(reviewNote.textContent).toContain(
      'Workspace purpose and collaboration choices are not saved and will not be applied to this workspace.'
    );

    // Access rows reflect standard controls
    expect(screen.getByText('Owner · Standard access setup required')).toBeTruthy();
    expect(
      screen.getByText('Standard accounting controls apply until solo workflow is activated.')
    ).toBeTruthy();

    // User explicitly reviews and clicks Create workspace a second time
    fireEvent.click(screen.getByRole('button', { name: 'Create workspace' }));

    // Success screen appears
    await screen.findByText('Your workspace is ready');

    // Exactly 2 POST calls now
    expect(postCalls).toHaveLength(2);
    // Second POST call omitted About fields so persisted state matches reviewed state
    expect(postCalls[1].workspacePurpose).toBeUndefined();
    expect(postCalls[1].collaborationIntent).toBeUndefined();
    expect(postCalls[1].ownerAccessPreset).toBeUndefined();
    expect(postCalls[1].initialJournalWorkflow).toBeUndefined();
    expect(postCalls[1].name).toBe('Acme');
    expect(postCalls[1].slug).toBe('acme');
  });

  it('stale-capability journey with extensions.reasonCode: handles problem details extension structure correctly', async () => {
    let postCount = 0;
    authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (url === '/api/organizations/setup-capabilities') {
        return new Response(
          JSON.stringify({
            soloSetupEnabled: postCount === 0,
            personalPurposeEnabled: false,
            controlledRecoveryEnabled: false,
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        );
      }
      if (url === '/api/organizations' && init?.method === 'POST') {
        postCount++;
        if (postCount === 1) {
          return new Response(
            JSON.stringify({
              status: 422,
              extensions: { reasonCode: 'solo_setup_disabled' },
              detail: 'Solo workspace auto-initialization is disabled.',
            }),
            { status: 422, headers: { 'Content-Type': 'application/json' } }
          );
        }
        return new Response(JSON.stringify({ slug: 'acme' }), {
          status: 201,
          headers: { 'Content-Type': 'application/json' },
        });
      }
      return new Response(null, { status: 404 });
    });

    render(<CreateOrgPage />);
    fireEvent.click(screen.getByDisplayValue('Business'));
    fireEvent.click(screen.getByDisplayValue('JustMe'));

    // Advance to Review
    await advanceToReview();

    fireEvent.click(screen.getByRole('button', { name: 'Create workspace' }));

    // Alert shown
    const alert = await screen.findByRole('alert');
    expect(alert.textContent).toContain('Solo setup is not enabled. Review and confirm');
    expect(postCount).toBe(1);
    expect(screen.getByText('Owner · Standard access setup required')).toBeTruthy();
    expect(screen.queryByText('Purpose')).toBeNull();
  });
});
