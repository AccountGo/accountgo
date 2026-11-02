import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { IntegrationsProvider, type Delivery, type SetupIdentity } from '../src/lib/integrations';
import { IncomingActivity } from '../src/components/IncomingActivity';
import { IntegrationsSettingsTab } from '../src/components/IntegrationsSettingsTab';
import { BankAccountFormDialog } from '../src/components/BankAccountFormDialog';
import { StatementLineExpenseDialog } from '../src/components/StatementLineExpenseDialog';
import IncomingReview from '../src/pages/IncomingReview';
import type { BankAccount, BankStatementLine } from '../src/types/banking';

const state = vi.hoisted(() => ({
  authFetch: vi.fn(), companyId: '1', permissions: new Set<string>(),
}));
vi.mock('../src/auth/AuthContext', () => ({ useAuth: () => ({ authFetch: state.authFetch }) }));
vi.mock('react-router-dom', async importOriginal => ({
  ...await importOriginal<typeof import('react-router-dom')>(),
  useOutletContext: () => ({ currentOrg: { id: state.companyId, slug: `workspace-${state.companyId}` } }),
}));
vi.mock('../src/components/Gating', () => ({
  useTenantContext: () => ({ hasPermission: (key: string) => state.permissions.has(key), hasModule: () => true }),
}));
vi.mock('../src/lib/entitlements', () => ({
  useCapabilities: () => ({ canModule: () => true, canWrite: () => true, canCreate: () => true }),
}));

const response = (body: unknown, status = 200) => ({ ok: status >= 200 && status < 300, status, json: async () => body });
const endpoint = {
  id: 'endpoint-1', label: 'Wise GBP', provider: 'wise', environment: 'sandbox', status: 'setup_required',
  bankId: 3, bankAccountName: 'GBP account', currency: 'GBP', providerAccountRef: 'profile:123:balance:456' as string | null,
  providerSubscriptionRef: null, revision: 'revision-1', lastDeliveryAt: null,
};
let connections: typeof endpoint[] = [];
let read = false;
let manage = true;
let setupIdentity: SetupIdentity | null = null;
let deliveries: Delivery[] = [];
let banks: BankAccount[] = [];

function defaultFetch(url: string, init?: RequestInit) {
  const method = init?.method || 'GET';
  if (url === '/api/integrations/capabilities') return response({ companyId: Number(state.companyId), canManage: manage, canReview: true });
  if (url.includes('/review/pending-count')) return response({ companyId: Number(state.companyId), pendingCount: 2, purchasePendingCount: 1, unreadCount: read ? 0 : 1, latestLineId: 22 });
  if (url.endsWith('/activity/read')) { read = true; return response(null, 204); }
  if (url.endsWith('/providers')) return response([
    { slug: 'wise', displayName: 'Wise', category: 'Payments', description: 'Incoming account movements', isAvailable: true },
    { slug: 'paypal', displayName: 'PayPal', category: 'Payments', description: 'Future provider', isAvailable: false },
    { slug: 'revolut', displayName: 'Revolut Business', category: 'Payments', description: 'Future provider', isAvailable: false },
  ]);
  if (url === '/api/Banking/Accounts') return response({ accounts: banks });
  if (url === `/api/integrations/connections/${endpoint.id}` && method === 'PATCH') {
    const body = JSON.parse(String(init?.body));
    connections = connections.map(c => c.id === endpoint.id ? { ...c, label: body.label, revision: 'renamed-revision' } : c);
    return response({ label: body.label, revision: 'renamed-revision' });
  }
  if (url.startsWith(`/api/integrations/connections/${endpoint.id}?revision=`) && method === 'DELETE') {
    connections = connections.filter(c => c.id !== endpoint.id);
    return response(null, 204);
  }
  if (url === '/api/integrations/connections' && method === 'POST') {
    connections = [{ ...endpoint, providerAccountRef: JSON.parse(String(init?.body)).providerAccountRef ?? null }];
    return response({ id: endpoint.id, webhookUrl: 'https://api.example.test/api/webhooks/wise/whk_once' });
  }
  if (url === '/api/integrations/connections') return response(connections);
  if (url.endsWith('/activate')) { connections = [{ ...endpoint, status: 'active' }]; return response({ status: 'active' }); }
  if (url.endsWith('/setup-identity')) return response({ identity: setupIdentity });
  if (url.includes('/deliveries')) return response(deliveries);
  if (url.endsWith('/review/options')) return response({ companyId: Number(state.companyId), connections: [], banks: [] });
  if (url.includes('/review/items')) return response({
    companyId: Number(state.companyId), totalCount: 1, page: 1, pageSize: 25,
    items: [{ lineId: 22, bankId: 3, bankName: 'GBP account', currency: 'GBP', postedDate: '2026-11-01',
      amount: -50, payee: null, reference: null, memo: null, draftBillId: null, state: 'needs_review',
      source: { provider: 'wise', label: 'Wise GBP', providerAccountRef: 'profile:123:balance:456' } }],
  });
  if (url.endsWith('/StatementExpenseCategories')) return response({ curated: [{ accountId: 7, name: 'Software' }], more: [] });
  if (url.endsWith('/Vendors')) return response([{ id: 8, name: 'Chosen vendor' }]);
  if (url.endsWith('/Bills')) return response([]);
  if (url.includes('/StatementDescriptionRule')) return response(null, 204);
  if (url.endsWith('/Taxes')) return response({ taxes: [] });
  if (url.endsWith('/DraftExpense')) return response({ line: { draftBillId: 99 }, bill: { id: 99 }, payment: null });
  throw new Error(`Unexpected request: ${method} ${url}`);
}

function wrapper(child: React.ReactNode, companyId = state.companyId, canManage = true, canView = true) {
  return <MemoryRouter initialEntries={['/org/workspace-1/banking/needs-review?direction=out']}>
    <IntegrationsProvider key={companyId} companyId={companyId} canView={canView} canManage={canManage}>{child}</IntegrationsProvider>
  </MemoryRouter>;
}

beforeEach(() => {
  state.authFetch.mockReset();
  state.authFetch.mockImplementation(async (url: string, init?: RequestInit) => defaultFetch(url, init));
  state.companyId = '1';
  state.permissions = new Set(['banking.accounts.manage', 'purchases.bills.create']);
  connections = []; read = false; manage = true; setupIdentity = null; deliveries = [];
  banks = [{ id: 3, name: 'GBP account', type: 1, currencyCode: 'GBP', archivedAt: null,
    accountNumberMasked: '**** 1234', isDefaultForReceipts: false, isDefaultForPayments: false, bookBalance: 0 }];
  localStorage.clear();
});
afterEach(cleanup);

describe('shared integration experience', () => {
  it('renames a connection without regenerating its callback or exposing permanent deletion before revocation', async () => {
    connections = [{ ...endpoint }];
    render(wrapper(<IntegrationsSettingsTab />));
    fireEvent.click(await screen.findByRole('button', { name: /Wise GBP/ }));
    expect(screen.queryByRole('button', { name: 'Permanently delete' })).toBeNull();
    fireEvent.change(screen.getByLabelText('Connection name'), { target: { value: '  Daily Wise  ' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save name' }));
    await screen.findByText('Connection name updated.');
    await screen.findByRole('button', { name: /Daily Wise/ });
    const rename = state.authFetch.mock.calls.find(c => c[1]?.method === 'PATCH')!;
    expect(rename[0]).toBe(`/api/integrations/connections/${endpoint.id}`);
    expect(JSON.parse(rename[1].body)).toEqual({ label: 'Daily Wise', revision: 'revision-1' });
    expect(state.authFetch.mock.calls.some(c => String(c[0]).endsWith('/rotate-key'))).toBe(false);
  });

  it('requires confirmation before permanently deleting a revoked connection and removes it after success', async () => {
    connections = [{ ...endpoint, status: 'revoked' }];
    render(wrapper(<IntegrationsSettingsTab />));
    fireEvent.click(await screen.findByRole('button', { name: /Wise GBP/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Permanently delete' }));
    await screen.findByRole('alertdialog');
    expect(state.authFetch.mock.calls.some(c => c[1]?.method === 'DELETE')).toBe(false);
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(state.authFetch.mock.calls.some(c => c[1]?.method === 'DELETE')).toBe(false);
    fireEvent.click(screen.getByRole('button', { name: 'Permanently delete' }));
    fireEvent.click(screen.getByRole('button', { name: 'Delete permanently' }));
    await screen.findByText('Revoked connection permanently deleted. Imported transactions, delivery history and audit records were retained.');
    await screen.findByText('No connections yet.');
    expect(screen.queryByRole('heading', { name: 'Wise GBP' })).toBeNull();
    const deletion = state.authFetch.mock.calls.find(c => c[1]?.method === 'DELETE')!;
    expect(deletion[0]).toBe(`/api/integrations/connections/${endpoint.id}?revision=revision-1`);
  });

  it('keeps the connection visible if permanent deletion is rejected', async () => {
    connections = [{ ...endpoint, status: 'revoked' }];
    state.authFetch.mockImplementation(async (url: string, init?: RequestInit) =>
      init?.method === 'DELETE' ? response({ message: 'The connection changed. Refresh before retrying.' }, 409) : defaultFetch(url, init));
    render(wrapper(<IntegrationsSettingsTab />));
    fireEvent.click(await screen.findByRole('button', { name: /Wise GBP/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Permanently delete' }));
    fireEvent.click(screen.getByRole('button', { name: 'Delete permanently' }));
    await screen.findByText('The connection changed. Refresh before retrying.');
    expect(screen.getByRole('heading', { name: 'Wise GBP' })).toBeTruthy();
    expect(screen.getByRole('button', { name: /Wise GBP/ })).toBeTruthy();
  });

  it('identifies the workspace bank with its masked number and uses its currency for callback setup', async () => {
    banks = [{ ...banks[0], name: 'Wise', currencyCode: 'USD', accountNumber: '999988881234' }];
    render(wrapper(<IntegrationsSettingsTab />));
    fireEvent.click(await screen.findByRole('button', { name: 'Connect Wise' }));
    expect(screen.getByRole('option', { name: 'Wise - **** 1234 (USD)' })).toBeTruthy();
    expect(screen.queryByText(/999988881234/)).toBeNull();
    fireEvent.change(screen.getByLabelText('Associated bank account'), { target: { value: '3' } });
    expect((screen.getByLabelText('Bank account currency') as HTMLInputElement).value).toBe('USD');
    fireEvent.click(screen.getByRole('button', { name: 'Generate callback URL' }));
    await screen.findByLabelText('One-time callback URL');
    const create = state.authFetch.mock.calls.find(c => c[0] === '/api/integrations/connections' && c[1]?.method === 'POST')!;
    expect(JSON.parse(create[1].body)).toEqual(expect.objectContaining({ bankId: 3, balanceCurrency: 'USD' }));
  });

  it('distinguishes an unset workspace bank currency from a genuine currency mismatch', async () => {
    banks = [{ ...banks[0], name: 'Wise', currencyCode: null }];
    const view = render(wrapper(<IntegrationsSettingsTab />));
    fireEvent.click(await screen.findByRole('button', { name: 'Connect Wise' }));
    expect(screen.getByRole('option', { name: 'Wise - **** 1234 (Currency not set)' })).toBeTruthy();
    fireEvent.change(screen.getByLabelText('Associated bank account'), { target: { value: '3' } });
    fireEvent.change(screen.getByLabelText('Bank account currency'), { target: { value: 'USD' } });
    fireEvent.click(screen.getByRole('button', { name: 'Generate callback URL' }));
    await screen.findByText('Set a currency on the associated workspace bank account before connecting Wise.');
    expect(screen.getByRole('link', { name: 'Edit workspace bank accounts' }).getAttribute('href')).toBe('/org/workspace-1/banking');
    expect(state.authFetch.mock.calls.some(c => c[0] === '/api/integrations/connections' && c[1]?.method === 'POST')).toBe(false);
    view.unmount();

    banks = [{ ...banks[0], currencyCode: 'GBP' }];
    render(wrapper(<IntegrationsSettingsTab />));
    fireEvent.click(await screen.findByRole('button', { name: 'Connect Wise' }));
    fireEvent.change(screen.getByLabelText('Associated bank account'), { target: { value: '3' } });
    fireEvent.change(screen.getByLabelText('Bank account currency'), { target: { value: 'USD' } });
    fireEvent.click(screen.getByRole('button', { name: 'Generate callback URL' }));
    await screen.findByText('The bank account currency must match the associated bank account.');
    expect(state.authFetch.mock.calls.some(c => c[0] === '/api/integrations/connections' && c[1]?.method === 'POST')).toBe(false);
  });

  it.each([false, true])('saves an explicit bank currency when editing=%s', async editing => {
    const onSaved = vi.fn();
    state.authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (init?.method === (editing ? 'PUT' : 'POST')) {
        return response({ ...banks[0], ...JSON.parse(String(init.body)) });
      }
      return defaultFetch(url, init);
    });
    render(<BankAccountFormDialog account={editing ? { ...banks[0], currencyCode: null } : undefined}
      hasFinancials={false} onSaved={onSaved} onClose={vi.fn()} />);
    if (!editing) fireEvent.change(screen.getByLabelText(/Display name/), { target: { value: 'Wise' } });
    fireEvent.change(screen.getByLabelText('Currency'), { target: { value: 'usd' } });
    fireEvent.click(screen.getByRole('button', { name: editing ? 'Save' : 'Add account' }));
    await waitFor(() => expect(onSaved).toHaveBeenCalled());
    const save = state.authFetch.mock.calls.find(c => c[1]?.method === (editing ? 'PUT' : 'POST'))!;
    expect(JSON.parse(save[1].body).currencyCode).toBe('USD');
  });

  it('creates a one-time callback first, then binds the confirmed subscription; future providers cannot connect', async () => {
    const view = render(wrapper(<IntegrationsSettingsTab />));
    fireEvent.click(await screen.findByRole('button', { name: 'Connect Wise' }));
    expect(screen.getAllByRole('button', { name: 'Coming soon' }).every(b => (b as HTMLButtonElement).disabled)).toBe(true);
    fireEvent.change(screen.getByLabelText('Associated bank account'), { target: { value: '3' } });
    fireEvent.change(screen.getByLabelText('Bank account currency'), { target: { value: 'GBP' } });
    fireEvent.change(screen.getByLabelText('Wise profile ID'), { target: { value: '123' } });
    fireEvent.change(screen.getByLabelText('Wise balance ID'), { target: { value: '456' } });
    fireEvent.click(screen.getByRole('button', { name: 'Generate callback URL' }));
    expect((await screen.findByLabelText('One-time callback URL') as HTMLInputElement).value).toContain('whk_once');
    const create = state.authFetch.mock.calls.find(c => c[0] === '/api/integrations/connections' && c[1]?.method === 'POST')!;
    expect(JSON.parse(create[1].body)).toEqual(expect.objectContaining({ setupOnly: true, environment: 'sandbox', providerAccountRef: 'profile:123:balance:456', balanceCurrency: 'GBP' }));
    expect(JSON.parse(create[1].body).secret).toBeUndefined();
    expect(localStorage.length).toBe(0);
    fireEvent.change(screen.getByLabelText('Confirmed Wise subscription UUID'), { target: { value: 'f2264fe5-a0f5-4dab-a1b4-6faa87761425' } });
    fireEvent.click(screen.getByRole('checkbox'));
    fireEvent.click(screen.getByRole('button', { name: 'Bind subscription and activate' }));
    await screen.findByText(/Connection activated/);
    expect(screen.queryByLabelText('One-time callback URL')).toBeNull();
    const activate = state.authFetch.mock.calls.find(c => String(c[0]).endsWith('/activate'))!;
    expect(JSON.parse(activate[1].body)).toEqual({ subscriptionId: 'f2264fe5-a0f5-4dab-a1b4-6faa87761425', revision: 'revision-1' });
    view.unmount();
    render(wrapper(<IntegrationsSettingsTab />));
    await screen.findByRole('button', { name: /Wise GBP/ });
    expect(screen.queryByDisplayValue(/whk_once/)).toBeNull();
  });

  it('keeps pending work after marking activity read and reloads the persistent state', async () => {
    const view = render(wrapper(<IncomingActivity orgSlug="workspace-1" />));
    fireEvent.click(await screen.findByRole('button', { name: 'Incoming activity: 1 unread' }));
    expect(screen.getByRole('link', { name: '2 transactions need review' }).getAttribute('href')).toContain('/banking/needs-review');
    fireEvent.click(screen.getByRole('button', { name: 'Mark activity read' }));
    await screen.findByRole('button', { name: 'Incoming activity: 0 unread' });
    expect(screen.getByRole('link', { name: '2 transactions need review' })).toBeTruthy();
    view.unmount();
    render(wrapper(<IncomingActivity orgSlug="workspace-1" />));
    await screen.findByRole('button', { name: 'Incoming activity: 0 unread' });
  });

  it('creates a callback without IDs or tokens and requires explicit confirmation of a verified real deposit', async () => {
    render(wrapper(<IntegrationsSettingsTab />));
    fireEvent.click(await screen.findByRole('button', { name: 'Connect Wise' }));
    fireEvent.change(screen.getByLabelText('Associated bank account'), { target: { value: '3' } });
    fireEvent.change(screen.getByLabelText('Bank account currency'), { target: { value: 'GBP' } });
    fireEvent.change(screen.getByLabelText('Wise profile ID'), { target: { value: '123' } });
    fireEvent.click(screen.getByRole('button', { name: 'Generate callback URL' }));
    await screen.findByText('Enter both existing balance-update identifiers, or leave both empty.');
    expect(state.authFetch.mock.calls.some(c => c[0] === '/api/integrations/connections' && c[1]?.method === 'POST')).toBe(false);
    fireEvent.change(screen.getByLabelText('Wise profile ID'), { target: { value: '' } });
    fireEvent.click(screen.getByRole('button', { name: 'Generate callback URL' }));
    await screen.findByLabelText('One-time callback URL');
    await screen.findByText(/Waiting for a signed, non-test notification/);
    const create = state.authFetch.mock.calls.find(c => c[0] === '/api/integrations/connections' && c[1]?.method === 'POST')!;
    expect(JSON.parse(create[1].body).providerAccountRef).toBeUndefined();
    expect(JSON.parse(create[1].body).secret).toBeUndefined();
    fireEvent.click(screen.getByRole('checkbox'));
    expect((screen.getByRole('button', { name: 'Bind subscription and activate' }) as HTMLButtonElement).disabled).toBe(true);

    setupIdentity = { deliveryId: 'verified-real-deposit', accountReference: 'profile:123:account:456',
      subscriptionReference: 'f2264fe5-a0f5-4dab-a1b4-6faa87761425', currency: 'GBP' };
    fireEvent.click(screen.getByRole('button', { name: 'Refresh connection' }));
    await screen.findByText(/Verified notification identity/);
    expect((screen.getByRole('button', { name: 'Bind subscription and activate' }) as HTMLButtonElement).disabled).toBe(true);
    fireEvent.click(screen.getByRole('checkbox'));
    fireEvent.click(screen.getByRole('button', { name: 'Bind subscription and activate' }));
    await screen.findByText(/Connection activated/);
    const activate = state.authFetch.mock.calls.find(c => String(c[0]).endsWith('/activate'))!;
    expect(JSON.parse(activate[1].body)).toEqual({ sourceDeliveryId: 'verified-real-deposit', revision: 'revision-1' });
  });

  it('previews transfer tests and issues without activation, then explicitly binds real transfer evidence without a currency', async () => {
    connections = [{ ...endpoint, providerAccountRef: null }];
    deliveries = [
      { id: 'test-transfer', eventType: 'transfers#state-change', receivedAt: '2026-11-02T12:00:00Z',
        verification: 'setup_required', status: 'ignored', attempts: 0, lastError: null, isTest: true,
        transferActivity: { transferId: '9007199254740993', profileId: '123', occurredAt: '2026-11-02T11:00:00Z',
          previousState: 'incoming_payment_waiting', currentState: 'processing', activeCases: [] } },
      { id: 'transfer-issue', eventType: 'transfers#active-cases', receivedAt: '2026-11-02T12:01:00Z',
        verification: 'setup_required', status: 'ignored', attempts: 0, lastError: null, isTest: false,
        transferActivity: { transferId: '111', profileId: '123', occurredAt: null,
          previousState: null, currentState: null, activeCases: ['deposit_amount_less_invoice'] } },
    ];
    render(wrapper(<IntegrationsSettingsTab />));
    fireEvent.click(await screen.findByRole('button', { name: /Wise GBP/ }));
    await screen.findByText('Transfer 9007199254740993 / Profile 123');
    expect(screen.getByText('Test notification - not a real transfer or deposit')).toBeTruthy();
    expect(screen.getByText('incoming_payment_waiting -> processing')).toBeTruthy();
    expect(screen.getByText('Active cases: deposit_amount_less_invoice')).toBeTruthy();
    fireEvent.click(screen.getByRole('checkbox'));
    expect((screen.getByRole('button', { name: 'Bind subscription and activate' }) as HTMLButtonElement).disabled).toBe(true);

    setupIdentity = { deliveryId: 'transfer-issue', accountReference: 'profile:123:transfers#active-cases',
      subscriptionReference: 'f2264fe5-a0f5-4dab-a1b4-6faa87761425', currency: null };
    fireEvent.click(screen.getByRole('button', { name: 'Refresh connection' }));
    await screen.findByText(/transfer activity only; no amount, currency or funding account verified/);
    expect((screen.getByRole('button', { name: 'Bind subscription and activate' }) as HTMLButtonElement).disabled).toBe(true);
    fireEvent.click(screen.getByRole('checkbox'));
    fireEvent.click(screen.getByRole('button', { name: 'Bind subscription and activate' }));
    await screen.findByText(/Connection activated/);
    expect(JSON.parse(state.authFetch.mock.calls.find(c => String(c[0]).endsWith('/activate'))![1].body))
      .toEqual({ sourceDeliveryId: 'transfer-issue', revision: 'revision-1' });
  });

  it('lets a reviewer inspect the shared queue without any integration-management calls', async () => {
    manage = false;
    render(wrapper(<IncomingReview />, '1', false));
    await screen.findByText('Payee: Not supplied by provider');
    expect(screen.getByRole('link', { name: 'Review transaction 22' }).getAttribute('href'))
      .toBe('/org/workspace-1/banking/3/statement-import?lineId=22&status=all');
    expect(state.authFetch.mock.calls.some(c => String(c[0]).includes('/review/items?direction=out'))).toBe(true);
    expect(state.authFetch.mock.calls.some(c => String(c[0]).includes('/connections') || String(c[0]).includes('/providers'))).toBe(false);
    fireEvent.change(screen.getByLabelText('Review state'), { target: { value: 'excluded' } });
    await waitFor(() => expect(state.authFetch.mock.calls.some(c => String(c[0]).includes('state=excluded'))).toBe(true));
  });

  it('does not request counts, activity or management data without local view/manage permissions', async () => {
    render(wrapper(<IncomingActivity orgSlug="workspace-1" />, '1', false, false));
    await act(async () => {});
    expect(state.authFetch).not.toHaveBeenCalled();
    expect(screen.queryByRole('button')).toBeNull();
  });

  it('discards delayed responses from an organization that has been left', async () => {
    let resolveOld!: (value: unknown) => void;
    const old = new Promise(resolve => { resolveOld = resolve; });
    state.authFetch.mockImplementation((url: string, init?: RequestInit) =>
      url.endsWith('/capabilities') && state.companyId === '1' ? old : Promise.resolve(defaultFetch(url, init)));
    const view = render(wrapper(<IncomingActivity orgSlug="workspace-1" />));
    state.companyId = '2';
    view.rerender(wrapper(<IncomingActivity orgSlug="workspace-2" />, '2'));
    fireEvent.click(await screen.findByRole('button', { name: 'Incoming activity: 1 unread' }));
    await act(async () => { resolveOld(response({ companyId: 1, canManage: true, canReview: true })); });
    expect(screen.getByRole('link', { name: '2 transactions need review' }).getAttribute('href')).toContain('/org/workspace-2/');
  });

  it.each([{ additionalLineIds: [] }, { additionalLineIds: [23, 24] }])('saves only the focused draft with create permission, even with selected lines: $additionalLineIds', async ({ additionalLineIds }) => {
    const onSaved = vi.fn();
    const line = { id: 22, bankId: 3, amount: -50, postedDate: '2026-11-01', payee: null, memo: null } as BankStatementLine;
    render(<StatementLineExpenseDialog line={line} additionalLineIds={additionalLineIds} initialMode="draft" bankAccountName="GBP account"
      formatCurrency={amount => `GBP ${amount}`} onSaved={onSaved} onClose={() => {}} />);
    fireEvent.change(await screen.findByLabelText('Vendor'), { target: { value: '8' } });
    fireEvent.change(screen.getByLabelText('Description'), { target: { value: 'User supplied accounting description' } });
    expect(screen.getByRole('note').textContent).toContain('No approval, payment, posting or statement match');
    fireEvent.click(document.querySelector<HTMLButtonElement>('button[type="submit"]')!);
    await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(1));
    const writes = state.authFetch.mock.calls.filter(c => c[1]?.method === 'POST');
    expect(writes).toHaveLength(1);
    expect(writes[0][0]).toBe('/api/Banking/StatementLines/22/DraftExpense');
    expect(JSON.parse(writes[0][1].body).description).toBe('User supplied accounting description');
  });
});
