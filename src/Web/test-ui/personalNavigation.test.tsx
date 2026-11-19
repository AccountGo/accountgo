import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { Sidebar } from '../src/components/Sidebar';
import { MobileBottomNav } from '../src/components/MobileBottomNav';
import GeneralLedger from '../src/pages/GeneralLedger';
import type { OrgDetails, UserProfile } from '../src/types';
vi.mock('../src/components/OrgSwitcher', () => ({ OrgSwitcher: () => null }));
vi.mock('../src/components/UserProfileSection', () => ({ UserProfileSection: () => null }));
vi.mock('../src/auth/AuthContext', () => ({
  useAuth: () => ({
    authFetch: vi.fn(async () => new Response(JSON.stringify({ page: { items: [], totalCount: 0 }, years: [] }), { status: 200 })),
  }),
}));
vi.mock('../src/components/Gating', () => ({
  useTenantContext: () => ({
    currentOrg: { slug: 'work', name: 'Work', role: 'Owner', isPersonalBooks: false, currency: 'USD' },
    hasPermission,
  }),
  PermissionDeniedCard: () => null,
}));
afterEach(cleanup);
const org = { slug: 'books', name: 'Books', role: 'Owner', isPersonalBooks: true } as OrgDetails;
const hasModule = () => true;
const hasPermission = () => true;
it('personal navigation uses household labels while keeping sales when enabled', () => {
  render(<MemoryRouter><Sidebar currentOrg={org} availableOrgs={[org]} user={{} as UserProfile} subscription={{ planId: 'invoicing' } as never} hasModule={hasModule} hasPermission={hasPermission} /></MemoryRouter>);
  for (const label of ['Bank accounts', 'Bills', 'Transactions', 'People', 'Settings', 'Invoices & Sales']) expect(screen.getAllByRole('link', { name: label }).length).toBeGreaterThan(0);
  const labels = screen.getAllByRole('link').map(link => link.textContent?.trim());
  expect(labels.indexOf('Bank accounts')).toBeLessThan(labels.indexOf('Bills'));
  expect(screen.queryByRole('link', { name: 'Products & services' })).toBeNull();
  expect(screen.queryByText(/upgrade/i)).toBeNull();
});
it('personal mobile navigation uses household labels', () => {
  render(<MemoryRouter><MobileBottomNav currentOrg={org} onOpenMenu={vi.fn()} hasModule={hasModule} hasPermission={hasPermission} /></MemoryRouter>);
  for (const label of ['Bank accounts', 'Bills', 'Transactions']) expect(screen.getAllByRole('link', { name: label }).length).toBeGreaterThan(0);
});
it('omits Customer Credits, Supplier Credits, and Year-end close from the main sidebar', () => {
  const businessOrg = { slug: 'work', name: 'Work', role: 'Owner', isPersonalBooks: false } as OrgDetails;
  render(<MemoryRouter><Sidebar currentOrg={businessOrg} availableOrgs={[businessOrg]} user={{} as UserProfile} hasModule={hasModule} hasPermission={hasPermission} /></MemoryRouter>);
  expect(screen.queryByRole('link', { name: 'Customer Credits' })).toBeNull();
  expect(screen.queryByRole('link', { name: 'Supplier Credits' })).toBeNull();
  expect(screen.queryByRole('link', { name: 'Year-end close' })).toBeNull();
});
it('exposes Year-end close inside General Ledger views nav', () => {
  render(<MemoryRouter><GeneralLedger /></MemoryRouter>);
  const ledgerNav = screen.getByRole('navigation', { name: 'Ledger views' });
  const yearEndLink = Array.from(ledgerNav.querySelectorAll('a')).find(a => a.textContent?.trim() === 'Year-end close');
  expect(yearEndLink?.getAttribute('href')).toBe('/org/work/financials/year-end');
});
