import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import JournalEntries, {
  JournalEditor,
  filterJournalEntries,
  type JournalEntry,
} from '../src/pages/JournalEntries';

const { authFetch } = vi.hoisted(() => ({ authFetch: vi.fn() }));

vi.mock('../src/auth/AuthContext', () => ({
  useAuth: () => ({ authFetch }),
}));

vi.mock('../src/components/Gating', () => ({
  PermissionDeniedCard: ({ permission }: { permission: string }) => <div>Denied {permission}</div>,
  ReadOnlyModuleBanner: () => null,
  useTenantContext: () => ({
    currentOrg: {
      slug: 'acme',
      currency: 'USD',
      role: 'Owner',
      operatingPolicy: { journalWorkflow: 'SingleActorAllowed' },
    },
    hasPermission: () => true,
    canWrite: () => true,
  }),
}));

const ACCOUNTS = [
  { id: 1, accountCode: '10100', accountName: 'Cash', canPost: true, childAccounts: [] },
  { id: 2, accountCode: '40100', accountName: 'Revenue', canPost: true, childAccounts: [] },
];

const IMPORTED_DRAFT: JournalEntry = {
  id: 11,
  journalDate: '2026-11-10',
  voucherType: 6,
  referenceNo: 'REF-11',
  memo: 'Imported entry',
  posted: false,
  sourceSystem: 'example-feed',
  sourceKey: 'doc:abc:v1',
  journalEntryLines: [
    { id: 101, accountId: 1, drCr: 1, amount: 50, memo: 'Dr' },
    { id: 102, accountId: 2, drCr: 2, amount: 50, memo: 'Cr' },
  ],
};

const MANUAL_DRAFT: JournalEntry = {
  id: 12,
  journalDate: '2026-11-11',
  voucherType: 6,
  referenceNo: 'REF-12',
  memo: 'Manual entry',
  posted: false,
  sourceSystem: null,
  sourceKey: null,
  journalEntryLines: [
    { id: 103, accountId: 1, drCr: 1, amount: 25, memo: 'Dr' },
    { id: 104, accountId: 2, drCr: 2, amount: 25, memo: 'Cr' },
  ],
};

const POSTED_IMPORTED: JournalEntry = {
  ...IMPORTED_DRAFT,
  id: 13,
  memo: 'Posted imported',
  posted: true,
  sourceKey: 'doc:posted:v1',
};

beforeEach(() => authFetch.mockReset());
afterEach(cleanup);

it('filters journal entries by status and imported/manual source', () => {
  const entries = [IMPORTED_DRAFT, MANUAL_DRAFT, POSTED_IMPORTED];
  expect(filterJournalEntries(entries, 'draft', 'all').map(e => e.id)).toEqual([11, 12]);
  expect(filterJournalEntries(entries, 'draft', 'imported').map(e => e.id)).toEqual([11]);
  expect(filterJournalEntries(entries, 'draft', 'manual').map(e => e.id)).toEqual([12]);
  expect(filterJournalEntries(entries, 'posted', 'imported').map(e => e.id)).toEqual([13]);
});

it('filters to imported drafts, posts selected drafts in batch, and renders per-item failures', async () => {
  authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    if (url === '/api/Financials/JournalEntries') {
      return new Response(JSON.stringify([IMPORTED_DRAFT, MANUAL_DRAFT, POSTED_IMPORTED]), { status: 200 });
    }
    if (url === '/api/Financials/Accounts') {
      return new Response(JSON.stringify(ACCOUNTS), { status: 200 });
    }
    if (url === '/api/Financials/YearEnd') {
      return new Response('[]', { status: 200 });
    }
    if (url === '/api/Financials/JournalEntries/PostBatch' && init?.method === 'POST') {
      return new Response(JSON.stringify([
        { id: 11, posted: true, code: null, message: null },
        { id: 12, posted: false, code: 'period_locked', message: 'This accounting period is locked.' },
      ]), { status: 200 });
    }
    return new Response('{}', { status: 404 });
  });

  render(
    <MemoryRouter>
      <JournalEntries />
    </MemoryRouter>,
  );

  await screen.findByText('Imported · example-feed');
  expect(screen.getByText('Manual entry')).toBeTruthy();

  fireEvent.click(screen.getByRole('button', { name: 'Imported' }));
  expect(screen.queryByText('Manual entry')).toBeNull();
  expect(screen.getByText('Imported entry')).toBeTruthy();

  fireEvent.click(screen.getByRole('button', { name: 'All' }));
  fireEvent.click(screen.getByLabelText('Select journal #11'));
  fireEvent.click(screen.getByLabelText('Select journal #12'));

  const postButton = screen.getByRole('button', { name: 'Post selected (2)' });
  fireEvent.click(postButton);

  await waitFor(() => {
    expect(authFetch).toHaveBeenCalledWith(
      '/api/Financials/JournalEntries/PostBatch',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ ids: [11, 12] }),
      }),
    );
  });

  const summary = await screen.findByText(/1 posted, 1 failed/);
  expect(summary).toBeTruthy();
  expect(await screen.findByText('Journal #12: This accounting period is locked.')).toBeTruthy();
});

it('renders imported draft read-only without Save and discards via ConfirmDialog', async () => {
  const onDiscarded = vi.fn();
  authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    if (url === '/api/Financials/JournalEntries/11/DiscardImported' && init?.method === 'POST') {
      return new Response(JSON.stringify({ discarded: true, id: 11 }), { status: 200 });
    }
    return new Response('{}', { status: 404 });
  });

  render(
    <JournalEditor
      initial={IMPORTED_DRAFT}
      accounts={ACCOUNTS}
      closedYears={[]}
      canCreate
      canPost
      fetcher={authFetch}
      onSaved={vi.fn()}
      onDiscarded={onDiscarded}
      currency="USD"
    />,
  );

  expect(screen.getByText('Imported · example-feed')).toBeTruthy();
  expect(screen.getByText('doc:abc:v1')).toBeTruthy();
  expect(screen.queryByRole('button', { name: 'Save draft' })).toBeNull();
  expect(screen.getByRole('button', { name: 'Post journal' })).toBeTruthy();

  fireEvent.click(screen.getByRole('button', { name: 'Discard' }));
  expect(await screen.findByRole('alertdialog')).toBeTruthy();

  fireEvent.click(screen.getByRole('button', { name: 'Discard draft' }));
  await waitFor(() => {
    expect(authFetch).toHaveBeenCalledWith(
      '/api/Financials/JournalEntries/11/DiscardImported',
      expect.objectContaining({ method: 'POST' }),
    );
    expect(onDiscarded).toHaveBeenCalledTimes(1);
  });
});
