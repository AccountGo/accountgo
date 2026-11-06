import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import { MemoryRouter, Outlet, Route, Routes } from 'react-router-dom';
import { Settings } from '../src/pages/Settings';

vi.mock('../src/auth/AuthContext', () => ({ useAuth: () => ({ authFetch: vi.fn(async () => ({ ok: false })) }) }));
vi.mock('../src/themes/ThemeContext', () => ({ useTheme: () => ({ setTheme: vi.fn(), setScheme: vi.fn(), resolvedScheme: 'dark' }) }));
vi.mock('../src/lib/integrations', () => ({ useIntegrations: () => ({ capabilities: null }) }));
vi.mock('../src/components/WorkspaceSetupCard', () => ({ WorkspaceSetupCard: () => null }));
vi.mock('../src/components/BusinessDetailsCard', () => ({ BusinessDetailsCard: () => null }));

afterEach(cleanup);
function show(sales: boolean) {
  const context = { currentOrg: { id: '1', name: 'Books', slug: 'books', role: 'Owner' }, user: { name: 'Owner', email: 'owner@example.test' }, subscription: { planId: 'test' }, hasModule: (name: string) => name === 'sales' && sales, hasPermission: () => false };
  render(<MemoryRouter initialEntries={['/settings']}><Routes><Route element={<Outlet context={context} />}><Route path="/settings" element={<Settings />} /></Route></Routes></MemoryRouter>);
}
it('shows sales settings only when the sales module exists', () => {
  show(false);
  expect(screen.queryByRole('tab', { name: 'Invoices' })).toBeNull();
  cleanup();
  show(true);
  expect(screen.getByRole('tab', { name: 'Invoices' })).toBeTruthy();
  expect(screen.getByRole('tab', { name: 'Email' })).toBeTruthy();
});
