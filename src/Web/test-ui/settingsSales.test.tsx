import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Outlet, Route, Routes } from 'react-router-dom';
import { Settings } from '../src/pages/Settings';

const { fetcher } = vi.hoisted(() => ({ fetcher: vi.fn(async (_url: string, _init?: RequestInit): Promise<any> => ({ ok: false })) }));
vi.mock('../src/auth/AuthContext', () => ({ useAuth: () => ({ authFetch: fetcher }) }));
vi.mock('../src/themes/ThemeContext', () => ({ useTheme: () => ({ setTheme: vi.fn(), setScheme: vi.fn(), resolvedScheme: 'dark' }) }));
vi.mock('../src/lib/integrations', () => ({ useIntegrations: () => ({ capabilities: null }) }));
vi.mock('../src/components/WorkspaceSetupCard', () => ({ WorkspaceSetupCard: () => null }));
vi.mock('../src/components/BusinessDetailsCard', () => ({ BusinessDetailsCard: () => null }));

afterEach(cleanup);
function show(sales: boolean, personal = false, path = "/settings", financials = false) {
  const context = { currentOrg: { id: '1', name: 'Books', slug: 'books', role: 'Owner', isPersonalBooks: personal }, user: { name: 'Owner', email: 'owner@example.test' }, subscription: { planId: 'test' }, updateCurrentOrg: vi.fn(), hasModule: (name: string) => (name === 'sales' && sales) || (name === 'financials' && financials), hasPermission: () => false };
  render(<MemoryRouter initialEntries={[path]}><Routes><Route element={<Outlet context={context} />}><Route path="/settings" element={<Settings />} /></Route></Routes></MemoryRouter>);
}
it('shows sales settings only when the sales module exists', () => {
  show(false);
  expect(screen.queryByRole('tab', { name: 'Invoices' })).toBeNull();
  cleanup();
  show(true);
  expect(screen.getByRole('tab', { name: 'Invoices' })).toBeTruthy();
  expect(screen.getByRole('tab', { name: 'Email' })).toBeTruthy();
});

it('personal settings hide business sections and fall back to Profile', () => {
  show(false, true, '/settings?tab=taxes');
  expect(screen.getByRole('heading', { name: 'Settings' })).toBeTruthy();
  expect(screen.getByText('Manage your personal books.')).toBeTruthy();
  expect(screen.getByRole('tablist', { name: 'Settings sections' })).toBeTruthy();
  expect(screen.getByRole('tab', { name: 'Profile' }).getAttribute('aria-selected')).toBe('true');
  expect(screen.queryByText('Legal Entity & Registration')).toBeNull();
  expect(screen.queryByLabelText('Short Name / Trading Acronym')).toBeNull();
  expect(screen.queryByLabelText('Internal Company Code')).toBeNull();
  expect(screen.queryByLabelText('CRA / Tax Registration Number')).toBeNull();
  expect(screen.queryByRole('tab', { name: 'Taxes' })).toBeNull();
  expect(screen.queryByRole('tab', { name: 'Whitelabel & Branding' })).toBeNull();
});

it('an adopted personal workspace keeps sales settings when the module exists', () => {
  show(true, true);
  expect(screen.getByRole('tab', { name: 'Invoices' })).toBeTruthy();
  expect(screen.getByRole('tab', { name: 'Email' })).toBeTruthy();
});

it('personal Books tab omits legacy financial configuration cards', () => {
  show(false, true, '/settings?tab=financials', true);
  expect(screen.getByRole('tab', { name: 'Books' })).toBeTruthy();
  expect(screen.getByRole('tab', { name: 'Roles & Permissions' })).toBeTruthy();
  expect(screen.getByRole('tab', { name: 'Plan' })).toBeTruthy();
  expect(screen.queryByRole('tab', { name: 'Journal Approval' })).toBeNull();
  expect(screen.queryByText('Active Fiscal Reporting Year')).toBeNull();
  expect(screen.queryByText('Open Banking')).toBeNull();
});

it('saving a personal profile preserves hidden registration fields and slug', async () => {
  fetcher.mockReset();
  fetcher.mockImplementation(async (url: string, init?: RequestInit) => {
    if (url === '/api/Administration/company' && init?.method === 'PUT') return { ok: true };
    if (url === '/api/organizations/current' && init?.method === 'PATCH') return { ok: true, json: async () => ({ id: '1', slug: 'books', name: 'Books' }) };
    return { ok: false };
  });
  show(false, true);
  fireEvent.click(screen.getByRole('button', { name: 'Save profile' }));
  await waitFor(() => expect(fetcher.mock.calls.some(([url, init]) => url === '/api/organizations/current' && init?.method === 'PATCH')).toBe(true));
  expect(fetcher.mock.calls.some(([url, init]) => url === '/api/Administration/company' && init?.method === 'PUT')).toBe(false);
  const request = fetcher.mock.calls.find(([url, init]) => url === '/api/organizations/current' && init?.method === 'PATCH')![1]!;
  expect(JSON.parse(String(request.body))).not.toHaveProperty('slug');
});
