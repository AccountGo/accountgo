import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { Sidebar } from '../src/components/Sidebar';
import { MobileBottomNav } from '../src/components/MobileBottomNav';
import type { OrgDetails, UserProfile } from '../src/types';
vi.mock('../src/components/OrgSwitcher', () => ({ OrgSwitcher: () => null }));
vi.mock('../src/components/UserProfileSection', () => ({ UserProfileSection: () => null }));
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
