import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Outlet, Route, Routes } from 'react-router-dom';
import { Settings } from '../src/pages/Settings';
import { RecordHistoryPanel } from '../src/components/RecordHistoryPanel';

const { fetcher, capabilities } = vi.hoisted(() => ({
  fetcher: vi.fn(async (_url: string, _init?: RequestInit): Promise<any> => ({ ok: false, json: async () => null })),
  capabilities: { current: null as any },
}));
vi.mock('../src/auth/AuthContext', () => ({ useAuth: () => ({ authFetch: fetcher }), getOrgSlugFromLocation: () => 'books' }));
vi.mock('../src/themes/ThemeContext', () => ({ useTheme: () => ({ setTheme: vi.fn(), setScheme: vi.fn(), resolvedScheme: 'dark' }) }));
vi.mock('../src/lib/integrations', () => ({ useIntegrations: () => ({ capabilities: null }) }));
vi.mock('../src/components/WorkspaceSetupCard', () => ({ WorkspaceSetupCard: () => null }));
vi.mock('../src/components/BusinessDetailsCard', () => ({ BusinessDetailsCard: () => null }));
vi.mock('../src/lib/entitlements', () => ({ useOptionalCapabilities: () => capabilities.current }));

beforeEach(() => {
  fetcher.mockReset();
  fetcher.mockImplementation(async () => ({ ok: true, json: async () => ({ items: [], nextCursor: null }) }));
});
afterEach(cleanup);

const snapshot = (features: string[]) => ({ plan: 'accounting', features });
const json = (body: unknown, ok = true) => ({ ok, json: async () => body });

function showSettings(options: { features: string[] | null; personal?: boolean; permission?: boolean; billing?: boolean; path?: string }) {
  capabilities.current = options.features
    ? { entitlements: snapshot(options.features), ready: true, canManageBilling: !!options.billing }
    : null;
  const context = {
    currentOrg: { id: '1', name: 'Books', slug: 'books', role: 'Owner', isPersonalBooks: !!options.personal },
    user: { name: 'Owner', email: 'owner@example.test' },
    subscription: { planId: 'test' },
    updateCurrentOrg: vi.fn(),
    hasModule: () => false,
    hasPermission: (key: string) => key === 'audit.logs.view' && options.permission !== false,
  };
  render(
    <MemoryRouter initialEntries={[options.path ?? '/settings?tab=activity']}>
      <Routes>
        <Route element={<Outlet context={context} />}>
          <Route path="/settings" element={<Settings />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

it('Activity tab shows after Roles & Permissions for the 90-day level, with the notice and upgrade link for billing managers', async () => {
  showSettings({ features: ['audit.activity'], billing: true });
  const tabs = screen.getAllByRole('tab').map((tab) => tab.textContent);
  expect(tabs.indexOf('Activity')).toBe(tabs.indexOf('Roles & Permissions') + 1);
  expect(screen.getByRole('tab', { name: 'Activity' }).getAttribute('aria-selected')).toBe('true');
  expect(screen.getByRole('status').textContent).toBe('Showing the last 90 days. Your plan keeps the full history; upgrade to see all of it.');
  expect(screen.getByRole('link', { name: 'upgrade' }).getAttribute('href')).toBe('/org/books/settings?tab=subscription');
  expect(screen.queryByRole('button', { name: 'Export CSV' })).toBeNull();
  expect(screen.queryByRole('button', { name: 'Security log' })).toBeNull();
  await waitFor(() => expect(screen.getByText('No changes recorded yet.')).toBeTruthy());
});

it('the 90-day notice has no upgrade wording without billing permission', async () => {
  showSettings({ features: ['audit.activity'], billing: false });
  expect(screen.getByRole('status').textContent).toBe('Showing the last 90 days. Your plan keeps the full history.');
  expect(screen.queryByRole('link', { name: 'upgrade' })).toBeNull();
  await waitFor(() => expect(fetcher).toHaveBeenCalled());
});

it('full level shows Export CSV and the security log, and no 90-day notice', async () => {
  showSettings({ features: ['audit.history.full'], billing: true });
  expect(screen.queryByRole('status')).toBeNull();
  expect(screen.getByRole('button', { name: 'Export CSV' })).toBeTruthy();
  fireEvent.click(screen.getByRole('button', { name: 'Security log' }));
  await waitFor(() => expect(fetcher.mock.calls.some(([url]) => String(url).startsWith('/api/organizations/current/security-events'))).toBe(true));
  expect(screen.getByLabelText('Event')).toBeTruthy();
});

it('a business workspace with single-record History sees the plan-limit card and makes no activity request', () => {
  showSettings({ features: [], billing: true });
  expect(screen.getByRole('tab', { name: 'Activity' })).toBeTruthy();
  expect(screen.getByTestId('activity-plan-limit')).toBeTruthy();
  expect(screen.getByRole('link', { name: 'View plan usage' })).toBeTruthy();
  expect(fetcher.mock.calls.some(([url]) => String(url).includes('/activity') || String(url).includes('/security-events'))).toBe(false);
});

it('personal books with single-record History hide the tab and fall back to Profile, with no upgrade prompt', () => {
  showSettings({ features: [], personal: true, billing: true });
  expect(screen.queryByRole('tab', { name: 'Activity' })).toBeNull();
  expect(screen.getByRole('tab', { name: 'Profile' }).getAttribute('aria-selected')).toBe('true');
  expect(screen.queryByTestId('activity-plan-limit')).toBeNull();
  expect(document.body.textContent).not.toMatch(/upgrade/i);
});

it('personal books with the 90-day level show Activity without the notice or organization wording', async () => {
  showSettings({ features: ['audit.activity'], personal: true, billing: true });
  expect(screen.getByRole('tab', { name: 'Activity' })).toBeTruthy();
  expect(screen.queryByRole('status')).toBeNull();
  expect(screen.getByText('Changes made in your books, newest first.')).toBeTruthy();
  await waitFor(() => expect(screen.getByText('No changes recorded yet.')).toBeTruthy());
  expect(document.body.textContent).not.toMatch(/upgrade|organization|profit/i);
});

it('without the audit permission the tab is hidden and the URL falls back to General', () => {
  showSettings({ features: ['audit.history.full'], permission: false });
  expect(screen.queryByRole('tab', { name: 'Activity' })).toBeNull();
  expect(screen.getByRole('tab', { name: 'General Profile' }).getAttribute('aria-selected')).toBe('true');
});

it('without a plan snapshot the tab stays hidden', () => {
  showSettings({ features: null });
  expect(screen.queryByRole('tab', { name: 'Activity' })).toBeNull();
});

function showPanel(personal = false) {
  const context = { currentOrg: { slug: 'books' }, hasPermission: () => true };
  render(
    <MemoryRouter>
      <Routes>
        <Route element={<Outlet context={context} />}>
          <Route path="/" element={<RecordHistoryPanel entityType="SalesInvoiceHeader" entityId={10} open onClose={() => {}} personal={personal} />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

const headerRow = {
  id: 3, entityType: 'SalesInvoiceHeader', entityId: 10, action: 'Modified',
  changes: { DueDate: ['2026-11-03T00:00:00', '2026-11-10T00:00:00'] },
  actorKind: 'user', actorName: 'Ana Cruz', correlationId: 'c1', occurredAt: '2026-11-03T08:00:00Z',
};
const lineRow = {
  id: 2, entityType: 'SalesInvoiceLine', entityId: 31, parentEntityType: 'SalesInvoiceHeader', parentEntityId: 10, action: 'Modified',
  changes: { Amount: [10, 20] }, actorKind: 'user', actorName: 'Ana Cruz', correlationId: 'c1', occurredAt: '2026-11-03T08:00:00Z',
};

it('History panel loads the record with its lines and groups line changes under "Line {id}"', async () => {
  fetcher.mockImplementation(async () => json({ items: [headerRow, lineRow], nextCursor: null }));
  showPanel();
  await waitFor(() => expect(screen.getByText('Changed the due date from 3 Nov 2026 to 10 Nov 2026')).toBeTruthy());
  expect(fetcher.mock.calls[0][0]).toBe('/api/organizations/current/activity?entityType=SalesInvoiceHeader&entityId=10&includeChildren=true&limit=50');
  expect(screen.getByText('Ana Cruz')).toBeTruthy();
  expect(screen.getByText('Line 31')).toBeTruthy();
  expect(screen.getByText('Changed the amount from 10 to 20')).toBeTruthy();
  expect(screen.queryByRole('button', { name: 'Load more' })).toBeNull();
});

it('History panel pages with Load more', async () => {
  fetcher
    .mockImplementationOnce(async () => json({ items: [headerRow], nextCursor: 3 }))
    .mockImplementationOnce(async () => json({
      items: [{ ...headerRow, id: 1, correlationId: 'c0', changes: { Notes: ['a', 'b'] } }], nextCursor: null,
    }));
  showPanel();
  fireEvent.click(await screen.findByRole('button', { name: 'Load more' }));
  await waitFor(() => expect(screen.getByText('Changed the notes from a to b')).toBeTruthy());
  expect(fetcher.mock.calls[1][0]).toContain('&cursor=3');
  expect(screen.getByText('Changed the due date from 3 Nov 2026 to 10 Nov 2026')).toBeTruthy();
  expect(screen.queryByRole('button', { name: 'Load more' })).toBeNull();
});

it('History panel shows the empty state and the server error message', async () => {
  fetcher.mockImplementation(async () => json({ items: [], nextCursor: null }));
  showPanel();
  await waitFor(() => expect(screen.getByText('No changes recorded yet.')).toBeTruthy());
  cleanup();

  fetcher.mockImplementation(async () => json({ code: 'FEATURE_NOT_ENTITLED', detail: 'History is not available right now.' }, false));
  showPanel();
  await waitFor(() => expect(screen.getByRole('alert').textContent).toBe('History is not available right now.'));
  expect(screen.queryByText('No changes recorded yet.')).toBeNull();
});

it('History panel in personal books uses personal wording', async () => {
  fetcher.mockImplementation(async () => json({ items: [], nextCursor: null }));
  render(
    <MemoryRouter>
      <Routes>
        <Route element={<Outlet context={{ currentOrg: { slug: 'books' }, hasPermission: () => true }} />}>
          <Route path="/" element={<RecordHistoryPanel entityType="JournalEntryHeader" entityId={4} open onClose={() => {}} personal />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
  await waitFor(() => expect(screen.getByText('Transaction changes, newest first')).toBeTruthy());
  expect(document.body.textContent).not.toMatch(/upgrade|organization|profit/i);
});
