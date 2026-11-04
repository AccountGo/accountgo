import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { JournalApprovalPolicyCard } from '../src/components/JournalApprovalPolicyCard';
import { JournalEditor, journalApprovalHeaders, type JournalEntry } from '../src/pages/JournalEntries';

const { authFetch } = vi.hoisted(() => ({ authFetch: vi.fn() }));

vi.mock('../src/auth/AuthContext', () => ({
  useAuth: () => ({ authFetch }),
}));

const OPERATING_SETTINGS = '/api/organizations/current/operating-settings';
const POLICY_CHANGE = `${OPERATING_SETTINGS}/journal-approval-policy`;
const CURRENT_REVISION = 7;

function operatingSettings(mode: string, canChange = true) {
  return {
    policyRevision: CURRENT_REVISION,
    journalApproval: { mode, provenance: 'LegacyEnforced', eligibleOtherReviewerCount: 1, canChange },
  };
}

function respondWith(settings: object, changeResponse?: Response) {
  authFetch.mockImplementation(async (url: string, options?: RequestInit) => {
    if (url === POLICY_CHANGE && options?.method === 'POST') return changeResponse ?? new Response('{}', { status: 200 });
    return new Response(JSON.stringify(settings), { status: 200 });
  });
}

beforeEach(() => authFetch.mockReset());
afterEach(cleanup);

it('shows the effective independent-review policy and posts an explicit change with the expected revision', async () => {
  respondWith(operatingSettings('IndependentReviewerRequired'));
  render(<JournalApprovalPolicyCard isOwner />);

  expect((await screen.findByTestId('journal-approval-mode')).textContent).toContain('Independent review required');
  expect(screen.getByTestId('journal-approval-reviewers').textContent).toBe('1');

  fireEvent.click(screen.getByRole('button', { name: 'Allow single-user posting' }));
  await waitFor(() => expect(authFetch).toHaveBeenCalledWith(POLICY_CHANGE, expect.objectContaining({
    method: 'POST',
    body: JSON.stringify({ expectedPolicyRevision: CURRENT_REVISION, journalWorkflow: 'SingleActorAllowed' }),
  })));
});

it('surfaces the server reason when independent review cannot be required and offers no change to non-owners', async () => {
  respondWith(
    operatingSettings('SingleActorAllowed'),
    new Response(JSON.stringify({ detail: 'Add another reviewer first.', reasonCode: 'reviewer_required' }), { status: 422 }),
  );
  render(<JournalApprovalPolicyCard isOwner />);

  fireEvent.click(await screen.findByRole('button', { name: 'Require independent review' }));
  const alert = await screen.findByRole('alert');
  expect(alert.textContent).toContain('Add another reviewer first.');
  expect(alert.getAttribute('data-reason-code')).toBe('reviewer_required');

  cleanup();
  respondWith(operatingSettings('IndependentReviewerRequired', false));
  render(<JournalApprovalPolicyCard isOwner={false} />);
  await screen.findByTestId('journal-approval-mode');
  expect(screen.queryByRole('button', { name: 'Allow single-user posting' })).toBeNull();
  expect(screen.getByText('Only a workspace Owner can change this policy.')).toBeTruthy();
});

it('builds approval headers only when both ids are present', () => {
  expect(journalApprovalHeaders(' req-1 ', ' user-2 ')).toEqual({
    'X-Approval-Request-Id': 'req-1',
    'X-Approved-By-User-Id': 'user-2',
  });
  expect(journalApprovalHeaders('req-1', '')).toEqual({});
});

const savedDraft: JournalEntry = {
  id: 9,
  journalDate: '2026-04-01T00:00:00',
  voucherType: 6,
  referenceNo: 'ADJ-9',
  memo: 'Adjustment',
  posted: false,
  journalEntryLines: [
    { id: 1, accountId: 12, drCr: 1, amount: 10, memo: '' },
    { id: 2, accountId: 40, drCr: 2, amount: 10, memo: '' },
  ],
};
const accounts = [
  { id: 12, accountCode: '10120', accountName: 'Receivables', canPost: true, childAccounts: [] },
  { id: 40, accountCode: '40100', accountName: 'Sales', canPost: true, childAccounts: [] },
];

function renderEditor(overrides: { canReview?: boolean } = {}) {
  return render(
    <JournalEditor
      initial={savedDraft}
      accounts={accounts}
      closedYears={[]}
      canCreate
      canPost
      fetcher={authFetch}
      onSaved={vi.fn()}
      currency="PHP"
      independentReview
      canReview={overrides.canReview ?? false}
    />,
  );
}

it('posts a journal with the reviewer approval headers under independent review', async () => {
  authFetch.mockResolvedValue(new Response(JSON.stringify({ id: 9 }), { status: 200 }));
  renderEditor();

  fireEvent.change(screen.getByLabelText('Approval request ID'), { target: { value: 'req-77' } });
  fireEvent.change(screen.getByLabelText('Approver user ID'), { target: { value: 'reviewer-1' } });
  fireEvent.click(screen.getByRole('button', { name: 'Post journal' }));

  await waitFor(() => expect(authFetch).toHaveBeenCalledWith(
    expect.stringContaining('/api/Financials/PostJournalEntry'),
    expect.objectContaining({
      headers: expect.objectContaining({
        'X-Approval-Request-Id': 'req-77',
        'X-Approved-By-User-Id': 'reviewer-1',
      }),
    }),
  ));
});

it('lets an eligible reviewer record an approval and shows the ids to hand to the preparer', async () => {
  authFetch.mockResolvedValue(new Response(JSON.stringify({ approvalRequestId: 'req-88', approverUserId: 'reviewer-2' }), { status: 201 }));
  renderEditor({ canReview: true });

  fireEvent.click(screen.getByRole('button', { name: 'Record my review approval' }));

  await waitFor(() => expect(authFetch).toHaveBeenCalledWith(
    '/api/organizations/current/members/access-approvals',
    expect.objectContaining({
      method: 'POST',
      body: JSON.stringify({ operationKey: 'financials.journal.post', resourceId: '9' }),
    }),
  ));
  expect(await screen.findByText(/req-88/)).toBeTruthy();
});

it('shows the server problem detail when the reviewer cannot approve their own journal', async () => {
  authFetch.mockResolvedValue(new Response(
    JSON.stringify({ detail: 'The person who prepared a journal entry cannot review it.' }),
    { status: 403 },
  ));
  renderEditor({ canReview: true });

  fireEvent.click(screen.getByRole('button', { name: 'Record my review approval' }));

  expect((await screen.findByRole('alert')).textContent).toContain('cannot review it');
});
