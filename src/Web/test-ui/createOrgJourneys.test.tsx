import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { CreateOrgPage } from '../src/pages/CreateOrg/CreateOrgPage';

const { authFetch, refreshSession, navigate, searchParams } = vi.hoisted(() => ({
  authFetch: vi.fn(),
  refreshSession: vi.fn(),
  navigate: vi.fn(),
  searchParams: new URLSearchParams('name=Acme&slug=acme'),
}));

vi.mock('../src/auth/AuthContext', () => ({
  useAuth: () => ({
    authFetch,
    refreshSession,
    user: { id: 'owner-1', email: 'owner@example.com' },
  }),
}));

vi.mock('react-router-dom', () => ({
  useNavigate: () => navigate,
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
  searchParams.delete('continue');
  searchParams.set('name', 'Acme');
  searchParams.set('slug', 'acme');
  refreshSession.mockReset();
  refreshSession.mockResolvedValue('new-token');
  navigate.mockReset();
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

// Continuation mode presents ONLY About + Review (2 steps). It never walks the user through plan/identity/
// money/reporting steps because the continuation endpoint discards those fields.
async function advanceContinuationToReview() {
  await screen.findByRole('progressbar', { name: 'Step 1 of 2' });
  // About -> Review (the only transition).
  fireEvent.click(screen.getByRole('button', { name: /Next/i }));
  await screen.findByRole('progressbar', { name: 'Step 2 of 2' });
}

describe('CreateOrg wizard capability-aware journeys', () => {
  it('about step waits for setup capabilities instead of flashing the questions', async () => {
    let release: (res: Response) => void = () => {};
    const pending = new Promise<Response>((resolve) => { release = resolve; });
    authFetch.mockImplementation(async (url: string) => {
      if (url === '/api/organizations/setup-capabilities') return pending;
      return new Response(null, { status: 404 });
    });

    render(<CreateOrgPage />);

    expect(await screen.findByText('Loading setup options…')).toBeTruthy();
    expect(screen.queryByDisplayValue('Freelance')).toBeNull();
    expect(screen.queryByRole('note')).toBeNull();
    expect((screen.getByRole('button', { name: /Next/ }) as HTMLButtonElement).disabled).toBe(true);

    release(new Response(
      JSON.stringify({ soloSetupEnabled: true, personalPurposeEnabled: false, controlledRecoveryEnabled: false }),
      { status: 200, headers: { 'Content-Type': 'application/json' } },
    ));

    expect(await screen.findByDisplayValue('Freelance')).toBeTruthy();
    expect(screen.queryByText('Loading setup options…')).toBeNull();
    expect(screen.queryByRole('note')).toBeNull();
  });

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
    expect(screen.queryByDisplayValue('Freelance')).toBeNull();
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

  it('team journey: explains independent review, keeps owner Full access, and posts Business + WithOthers without a single-actor workflow', async () => {
    const postCalls: any[] = [];
    authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (url === '/api/organizations/setup-capabilities') {
        return new Response(
          JSON.stringify({ soloSetupEnabled: true, personalPurposeEnabled: false, controlledRecoveryEnabled: false }),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        );
      }
      if (url === '/api/organizations' && init?.method === 'POST') {
        postCalls.push(JSON.parse(String(init.body)));
        return new Response(JSON.stringify({ slug: 'acme' }), { status: 201, headers: { 'Content-Type': 'application/json' } });
      }
      return new Response(null, { status: 404 });
    });

    render(<CreateOrgPage />);

    fireEvent.click(await screen.findByRole('radio', { name: /Freelance or self-employed/ }));
    fireEvent.click(screen.getByRole('radio', { name: /Me and others/ }));
    expect(screen.getByText(/Journals will need independent review/)).toBeTruthy();

    await advanceToReview();

    expect(screen.getByText('Me and others')).toBeTruthy();
    expect(screen.getByText('Owner · Full workspace access (you can narrow later)')).toBeTruthy();
    expect(screen.getByText(/Independent review: another Owner or Admin who can post journals approves each journal/)).toBeTruthy();
    expect(screen.getByText(/You keep full access as the owner/)).toBeTruthy();

    fireEvent.click(screen.getByRole('button', { name: 'Create workspace' }));
    await screen.findByText('Your workspace is ready');

    expect(postCalls).toHaveLength(1);
    expect(postCalls[0].workspacePurpose).toBe('Business');
    expect(postCalls[0].collaborationIntent).toBe('WithOthers');
    expect(postCalls[0].ownerAccessPreset).toBe('FullWorkspaceAccess');
    expect(postCalls[0].initialJournalWorkflow).toBeUndefined();
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
    fireEvent.click(screen.getByDisplayValue('Freelance'));
    fireEvent.click(screen.getByDisplayValue('JustMe'));

    // Advance to Review
    await advanceToReview();

    // Review step initially shows full access and single-actor posting
    expect(screen.getByText('Purpose')).toBeTruthy();
    expect(screen.getByText('Freelance or self-employed')).toBeTruthy();
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
    expect(screen.queryByText('Freelance or self-employed')).toBeNull();

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
    fireEvent.click(await screen.findByDisplayValue('Freelance'));
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

  it('signup continuation journey: initializes the existing workspace in place (no create), refreshes access, and reaches the first useful task', async () => {
    // A signup-created, still-unclassified workspace: finish setup through the SAME onboarding flow via
    // ?continue=<slug>, which must call current/initialize (NOT create a new org) and refresh access.
    searchParams.delete('name');
    searchParams.delete('slug');
    searchParams.set('continue', 'acme');

    const createCalls: any[] = [];
    const initCalls: Array<{ body: any; org: string | null }> = [];
    authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (url === '/api/organizations/setup-capabilities') {
        return new Response(
          JSON.stringify({ soloSetupEnabled: true, personalPurposeEnabled: false, controlledRecoveryEnabled: false }),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        );
      }
      if (url === '/api/organizations' && init?.method === 'POST') {
        createCalls.push(JSON.parse(String(init.body)));
        return new Response(JSON.stringify({ slug: 'acme' }), { status: 201, headers: { 'Content-Type': 'application/json' } });
      }
      if (url === '/api/organizations/current/initialize' && init?.method === 'POST') {
        const headers = new Headers(init.headers);
        initCalls.push({ body: JSON.parse(String(init.body)), org: headers.get('X-AccountGo-Org') });
        return new Response(
          JSON.stringify({ initialized: true, plan: 'community', collaborationState: 'SoloActive', journalWorkflow: 'SingleActorAllowed', accessProfileKey: 'full' }),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        );
      }
      if (url === '/api/organizations/current' && (init?.method ?? 'GET') === 'GET') {
        // The existing workspace's STORED settings, shown read-only on the review.
        return new Response(
          JSON.stringify({ name: 'Acme Books', slug: 'acme', plan: 'community', currency: 'PHP', fiscalYearEnd: '2026-12-31' }),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        );
      }
      return new Response(null, { status: 404 });
    });

    render(<CreateOrgPage />);

    // Continuation presents ONLY About + Review — it never opens the plan-choice (Intent) step, nor the
    // identity/money/reporting steps, because the continuation endpoint discards those fields. Regression
    // for the finding that continuation still walked users through — and validated — discarded settings.
    await screen.findByRole('progressbar', { name: 'Step 1 of 2' });
    expect(screen.queryByText('What do you need?')).toBeNull();

    // Choose Business + Just me, then advance to review.
    fireEvent.click(screen.getByDisplayValue('Freelance'));
    fireEvent.click(screen.getByDisplayValue('JustMe'));
    await advanceContinuationToReview();

    // The plan-choice step was never shown between About and Review.
    expect(screen.queryByText('What do you need?')).toBeNull();

    // The review shows the full-access, single-user outcome.
    expect(screen.getByText('Owner · Full workspace access')).toBeTruthy();
    expect(screen.getByText('You can prepare and post your own journals under the single-user workflow.')).toBeTruthy();
    // It shows the workspace's STORED facts (read-only), not local draft values. The stored currency PHP
    // appears plainly (no misleading "set at creation" on an unsaved draft value), and no "Books start"
    // draft row is presented as if it will be saved.
    expect(screen.getByText('PHP')).toBeTruthy();
    expect(screen.getByText('Acme Books')).toBeTruthy();
    expect(screen.queryByText('Books start')).toBeNull();

    // Finish setup.
    fireEvent.click(screen.getByRole('button', { name: 'Create workspace' }));
    await screen.findByText('Your workspace is ready');

    // Initialized the EXISTING workspace in place — no create call, exactly one scoped initialize.
    expect(createCalls).toHaveLength(0);
    expect(initCalls).toHaveLength(1);
    expect(initCalls[0].org).toBe('acme');
    expect(initCalls[0].body.workspacePurpose).toBe('Business');
    expect(initCalls[0].body.collaborationIntent).toBe('JustMe');
    expect(initCalls[0].body.ownerAccessPreset).toBe('FullWorkspaceAccess');
    expect(initCalls[0].body.initialJournalWorkflow).toBe('SingleActorAllowed');
    // The continuation never sends a plan — plan changes are billing-gated and handled separately.
    expect(initCalls[0].body.plan).toBeUndefined();

    // Access was refreshed so the just-granted workspace access is live before the dashboard.
    expect(refreshSession).toHaveBeenCalledTimes(1);

    // Routed to the first useful task (the initialized workspace dashboard), not a second create.
    await waitFor(() => {
      expect(navigate).toHaveBeenCalledWith('/org/acme/dashboard', { replace: true });
    });
  });

  it('signup continuation journey: ineligible workspace is not duplicated and surfaces a clear message', async () => {
    searchParams.delete('name');
    searchParams.delete('slug');
    searchParams.set('continue', 'acme');

    const createCalls: any[] = [];
    authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (url === '/api/organizations/setup-capabilities') {
        return new Response(
          JSON.stringify({ soloSetupEnabled: true, personalPurposeEnabled: false, controlledRecoveryEnabled: false }),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        );
      }
      if (url === '/api/organizations' && init?.method === 'POST') {
        createCalls.push(JSON.parse(String(init.body)));
        return new Response(JSON.stringify({ slug: 'acme' }), { status: 201, headers: { 'Content-Type': 'application/json' } });
      }
      if (url === '/api/organizations/current/initialize' && init?.method === 'POST') {
        return new Response(
          JSON.stringify({ status: 422, reasonCode: 'not_pending_signup_workspace', detail: 'Not eligible.' }),
          { status: 422, headers: { 'Content-Type': 'application/json' } }
        );
      }
      if (url === '/api/organizations/current' && (init?.method ?? 'GET') === 'GET') {
        return new Response(
          JSON.stringify({ name: 'Acme Books', slug: 'acme', plan: 'community', currency: 'PHP', fiscalYearEnd: '2026-12-31' }),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        );
      }
      return new Response(null, { status: 404 });
    });

    render(<CreateOrgPage />);
    fireEvent.click(await screen.findByDisplayValue('Freelance'));
    fireEvent.click(screen.getByDisplayValue('JustMe'));
    await advanceContinuationToReview();
    fireEvent.click(screen.getByRole('button', { name: 'Create workspace' }));

    const alert = await screen.findByRole('alert');
    expect(alert.textContent).toContain('already set up or is not eligible');
    // No workspace was created as a side effect; no session refresh on a failed init.
    expect(createCalls).toHaveLength(0);
    expect(refreshSession).not.toHaveBeenCalled();
  });
});
