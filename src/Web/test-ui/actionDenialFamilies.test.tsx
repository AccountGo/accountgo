import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import React from 'react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthProvider } from '../src/auth/AuthContext';
import { ActionDenialRegion } from '../src/components/ActionDenialRegion';
import { BankAccountFormDialog } from '../src/components/BankAccountFormDialog';
import { PartyFormDialog } from '../src/components/PartyFormDialog';
import { clearAccessDirectoryCache } from '../src/lib/accessDirectory';

// One real component per page family, under the real authFetch (via AuthProvider), with the server refusing the action:
// the member gets the quiet notice (not a plan dialog, not an extra modal, not a raw permission key).
const { outlet } = vi.hoisted(() => ({
  outlet: { currentOrg: { id: '1', name: 'Acme', slug: 'acme', role: 'Owner' }, hasModule: () => true, hasPermission: () => true },
}));
vi.mock('react-router-dom', async () => ({
  ...(await vi.importActual<typeof import('react-router-dom')>('react-router-dom')),
  useOutletContext: () => outlet,
  useSearchParams: () => [new URLSearchParams(), vi.fn()],
}));
vi.mock('../src/lib/entitlements', () => ({ useCapabilities: () => ({ canCreate: () => true, canModule: () => true, canWrite: () => true, entitlements: null }) }));

const catalog = [
  { id: 'role_buyer', name: 'Purchasing Agent', displayName: 'Purchasing Agent', permissions: ['purchases.vendors.manage'] },
  { id: 'role_sales', name: 'Sales Representative', displayName: 'Sales Representative', permissions: ['sales.customers.manage'] },
  { id: 'role_acct', name: 'Accountant', displayName: 'Accountant', permissions: ['banking.accounts.manage', 'purchases.vendors.manage', 'sales.customers.manage'] },
];

const denial = (permission: string) =>
  new Response(JSON.stringify({
    type: 'https://accountgo.ph/errors/permission-denied', title: 'Access Denied', status: 403,
    detail: `You do not have the required permission '${permission}' to perform this action.`,
    code: 'PERMISSION_DENIED', requiredPermission: permission, policyId: 'POL-X',
  }), { status: 403, headers: { 'Content-Type': 'application/problem+json' } });

let writeUrl = '';
let writePermission = '';
let writeMethod = 'POST';
const writes: string[] = [];

function installServer() {
  writes.length = 0;
  globalThis.fetch = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input);
    const method = (init?.method ?? 'GET').toUpperCase();
    if (url.includes('/api/auth/')) return new Response(JSON.stringify({ title: 'Unauthorized' }), { status: 401, headers: { 'Content-Type': 'application/json' } });
    if (method !== 'GET' && url.startsWith(writeUrl) && method === writeMethod) { writes.push(url); return denial(writePermission); }
    if (url.endsWith('/members')) return new Response(JSON.stringify([{ userId: 'me', firstName: 'Olive', lastName: 'Owner', role: 'Owner', status: 'Active' }]), { status: 200, headers: { 'Content-Type': 'application/json' } });
    if (url.endsWith('/security-roles')) return new Response(JSON.stringify(catalog), { status: 200, headers: { 'Content-Type': 'application/json' } });
    if (url === '/api/organizations/current') return new Response(JSON.stringify({ role: 'Owner', permissions: [] }), { status: 200, headers: { 'Content-Type': 'application/json' } });
    return new Response('[]', { status: 200, headers: { 'Content-Type': 'application/json' } });
  }) as unknown as typeof fetch;
}

const Harness: React.FC<{ children: React.ReactNode }> = ({ children }) => (
  <AuthProvider>
    <MemoryRouter initialEntries={['/org/acme/page']}>
      <ActionDenialRegion currentOrg={{ id: '1', slug: 'acme', role: 'Owner' }} />
      {children}
    </MemoryRouter>
  </AuthProvider>
);

/** Text a person reads: everything except the collapsed Details. */
const readableText = () => {
  const body = document.body.cloneNode(true) as HTMLElement;
  body.querySelectorAll('details').forEach((node) => node.remove());
  return body.textContent ?? '';
};
const RAW_KEY = /\b(?:sales|purchases|financials|banking|inventory|admin|reports)\.[a-z_]+(?:\.[a-z_]+)?\b/;

async function expectQuietNotice(sentence: RegExp) {
  const notice = await screen.findByTestId('action-denial-notice');
  // The roles come from the live catalog a moment after the notice appears.
  await waitFor(() => expect(screen.getByTestId('action-denial-notice').textContent).toMatch(sentence));
  expect(screen.getByTestId('action-denial-region').contains(notice)).toBe(true);
  // No plan dialog, and no dialog beyond the form's own modal.
  expect(screen.queryByText(/plan limit|upgrade/i)).toBeNull();
  expect(document.querySelectorAll('[role="dialog"]').length).toBeLessThanOrEqual(1);
  expect(readableText()).not.toMatch(RAW_KEY);
  expect(readableText()).not.toMatch(/\b403\b|POL-X/);
}

const submit = (container: HTMLElement) => fireEvent.click(container.querySelector('button[type="submit"]') as HTMLButtonElement);

beforeEach(() => { clearAccessDirectoryCache(); writeMethod = 'POST'; });
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

describe('a server permission denial on a real action shows the notice', () => {
  it('purchases: adding a vendor', async () => {
    writeUrl = '/api/Purchasing/SaveVendor';
    writePermission = 'purchases.vendors.manage';
    installServer();
    const { container } = render(<Harness><PartyFormDialog role="vendor" initialName="Acme Supply" paymentTerms={[]} onClose={() => {}} onSaved={() => {}} /></Harness>);

    submit(container.ownerDocument.body);

    await expectQuietNotice(/To change Vendors you need Purchasing Agent or Accountant\./);
    expect(writes).toHaveLength(1);
  });

  it('sales: adding a client', async () => {
    writeUrl = '/api/Sales/SaveClient';
    writePermission = 'sales.customers.manage';
    installServer();
    const { container } = render(<Harness><PartyFormDialog role="client" initialName="Acme Buyer" paymentTerms={[]} onClose={() => {}} onSaved={() => {}} /></Harness>);

    submit(container.ownerDocument.body);

    await expectQuietNotice(/To change Clients you need Sales Representative or Accountant\./);
    expect(writes).toHaveLength(1);
  });

  it('banking: creating a bank account', async () => {
    writeUrl = '/api/Banking/Accounts';
    writePermission = 'banking.accounts.manage';
    installServer();
    const { container } = render(<Harness><BankAccountFormDialog hasFinancials onSaved={() => {}} onClose={() => {}} /></Harness>);
    fireEvent.change(screen.getByLabelText(/Display name/), { target: { value: 'Main checking' } });
    fireEvent.change(screen.getByLabelText(/Currency/i), { target: { value: 'USD' } });

    submit(container.ownerDocument.body);

    await expectQuietNotice(/To change Bank accounts you need Accountant\./);
    expect(writes).toHaveLength(1);
  });
});
