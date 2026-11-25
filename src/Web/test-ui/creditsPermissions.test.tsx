import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { CreditsAndSettlements } from '../src/pages/CreditsAndSettlements';
import { clearAccessDirectoryCache } from '../src/lib/accessDirectory';

const { authFetch, granted, outlet } = vi.hoisted(() => ({
  authFetch: vi.fn(),
  granted: { keys: new Set<string>() },
  outlet: { currentOrg: { id: '1', name: 'Work', slug: 'work', entityType: 'SoleProprietor', currency: 'USD' }, hasModule: () => true, canWrite: () => true },
}));

vi.mock('../src/auth/AuthContext', () => ({
  useAuth: () => ({ authFetch, user: { id: 'me', email: 'me@example.com' } }),
  getOrgSlugFromLocation: () => 'work',
}));
vi.mock('../src/components/Gating', () => ({
  usePermissions: () => ({ hasPermission: (key: string | string[]) => (Array.isArray(key) ? key : [key]).some((k) => granted.keys.has(k)) }),
  useTenantContext: () => outlet,
  ReadOnlyModuleBanner: () => null,
}));
vi.mock('../src/lib/entitlements', () => ({ useCapabilities: () => ({ canModule: () => true, canWrite: () => true, canCreate: () => true }) }));
vi.mock('react-router-dom', async () => ({ ...(await vi.importActual<typeof import('react-router-dom')>('react-router-dom')), useOutletContext: () => outlet }));

const mappings = {
  readyForSalesCredits: true, readyForSupplierCredits: true, readyForAdvances: true, readyForWriteOffs: true, readyForReturns: true,
  roles: [{ roleKey: 'CustomerCreditLiability', roleLabel: 'Customer Credits Liability', configuredAccountId: '201', configuredAccountCode: '20410', configuredAccountName: 'Customer Credits Liability', suggestedAccountId: '201', suggestedAccountCode: '20410', suggestedAccountName: 'Customer Credits Liability', isArchived: false, isMissing: false }],
};
const credit = (id: string, statusLabel: string, available = 150) => ({
  id, no: `CCN-${id}`, customerId: '5', customerName: 'Acme Corp', date: '2026-11-10T00:00:00Z', totalAmount: 150, appliedAmount: 0,
  refundedAmount: 0, availableAmount: available, settlementState: 'Available', status: 3, statusLabel, reason: 'x', version: 1,
});
const catalog = [
  { id: 'r_agent', name: 'Sales Representative', displayName: 'Sales Representative', permissions: ['sales.credits.create', 'sales.credits.view'] },
  { id: 'r_acct', name: 'Accountant', displayName: 'Accountant', permissions: ['sales.credits.create', 'sales.credits.post', 'sales.credits.reverse', 'financials.settlements.apply', 'sales.refunds.create', 'sales.refunds.post', 'sales.write_offs.create', 'sales.write_offs.post', 'sales.returns.post', 'financials.accounts.manage', 'sales.credits.view'] },
];

const ALL = [
  'sales.credits.create', 'sales.credits.post', 'sales.credits.reverse', 'financials.settlements.apply', 'sales.refunds.create', 'sales.refunds.post',
  'sales.write_offs.create', 'sales.write_offs.post', 'sales.returns.post', 'financials.accounts.manage',
];

const renderSales = () => render(
  <MemoryRouter initialEntries={['/org/work/sales/credits']}>
    <Routes><Route path="/org/:slug/sales/credits" element={<CreditsAndSettlements side="sales" />} /></Routes>
  </MemoryRouter>,
);

beforeEach(() => {
  clearAccessDirectoryCache();
  granted.keys = new Set(ALL);
  authFetch.mockReset();
  authFetch.mockImplementation(async (url: string) => {
    if (url === '/api/sales/credits') return new Response(JSON.stringify([credit('1', 'Draft'), credit('2', 'Posted')]), { status: 200 });
    if (url === '/api/sales/credits/mappings/preview') return new Response(JSON.stringify(mappings), { status: 200 });
    if (String(url).endsWith('/security-roles')) return new Response(JSON.stringify(catalog), { status: 200 });
    if (String(url).endsWith('/members')) return new Response(JSON.stringify([{ userId: 'me', firstName: 'Max', lastName: 'Member', role: 'Member', status: 'Active' }]), { status: 200 });
    if (String(url).endsWith('/access-requests')) return new Response('[]', { status: 200 });
    return new Response('[]', { status: 200 });
  });
});
afterEach(() => { cleanup(); vi.clearAllMocks(); });

describe('credits and settlements show only what the member may do', () => {
  it('a member with every credit permission sees all the header and row actions and the mappings save', async () => {
    renderSales();

    expect(await screen.findByText('CCN-1')).toBeTruthy();
    for (const label of ['New customer credit', 'Write off invoice', 'Customer return']) expect(screen.getByRole('button', { name: label })).toBeTruthy();
    for (const label of ['Submit', 'Post', 'Apply', 'Record Refund', 'Reverse']) expect(screen.getAllByRole('button', { name: new RegExp(label) }).length).toBeGreaterThan(0);
    expect(screen.getByRole('button', { name: /Save Mappings/ })).toBeTruthy();
    expect(screen.queryByText(/View only/)).toBeNull();
  });

  it('a view-only member sees no creation buttons, no row actions and no mappings save, only a plain line naming the roles', async () => {
    granted.keys = new Set(); // e.g. Auditor / Viewer
    renderSales();

    expect(await screen.findByText('CCN-1')).toBeTruthy();
    for (const label of ['New customer credit', 'Write off invoice', 'Customer return']) expect(screen.queryByRole('button', { name: label })).toBeNull();
    for (const label of ['Submit', 'Post', 'Apply', 'Record Refund', 'Reverse']) expect(screen.queryByRole('button', { name: new RegExp(`^${label}`) })).toBeNull();
    expect(screen.queryByRole('button', { name: /Save Mappings/ })).toBeNull();
    // The preview stays readable.
    expect(screen.getByText('Control-Account Mapping Readiness')).toBeTruthy();
    // Each row says why it has no actions, with the roles from the live catalog (not a tooltip on a disabled button).
    // The draft row's actions (create, post) are held by two roles; the posted row's (apply, refund, reverse) by one.
    await waitFor(() => expect(screen.getAllByText(/^View only\. Changing credits needs /)).toHaveLength(2));
    expect(screen.getByText('View only. Changing credits needs Sales Representative or Accountant.')).toBeTruthy();
    expect(screen.getByText('View only. Changing credits needs Accountant.')).toBeTruthy();
    expect(screen.queryAllByRole('button').filter((b) => (b as HTMLButtonElement).disabled)).toHaveLength(0);
  });

  it('shows just the actions the member holds: a poster who cannot create sees Post but not Submit, and no hint', async () => {
    granted.keys = new Set(['sales.credits.post']);
    renderSales();

    await screen.findByText('CCN-1');
    const draftRow = screen.getByText('CCN-1').closest('tr') as HTMLElement;
    expect(within(draftRow).getByRole('button', { name: /Post/ })).toBeTruthy();
    expect(within(draftRow).queryByRole('button', { name: 'Submit' })).toBeNull();
    expect(within(draftRow).queryByText(/View only/)).toBeNull();
    expect(screen.queryByRole('button', { name: 'New customer credit' })).toBeNull();
  });

  it('a refund needs both create and post, since it is drafted, submitted and posted', async () => {
    granted.keys = new Set(['sales.refunds.create', 'sales.credits.view']);
    renderSales();

    await screen.findByText('CCN-2');
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Record Refund' })).toBeNull());
  });

  it('a posted credit with nothing left to apply still gets a hint when the member cannot reverse it', async () => {
    authFetch.mockImplementation(async (url: string) => {
      if (url === '/api/sales/credits') return new Response(JSON.stringify([credit('3', 'Posted', 0)]), { status: 200 });
      if (url === '/api/sales/credits/mappings/preview') return new Response(JSON.stringify(mappings), { status: 200 });
      if (String(url).endsWith('/security-roles')) return new Response(JSON.stringify(catalog), { status: 200 });
      if (String(url).endsWith('/members')) return new Response('[]', { status: 200 });
      return new Response('{}', { status: 200 });
    });
    granted.keys = new Set();
    renderSales();

    await screen.findByText('CCN-3');
    expect(await screen.findByText(/View only\. Changing credits needs/)).toBeTruthy();
  });
});
