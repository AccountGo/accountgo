import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Outlet, Route, Routes } from 'react-router-dom';
import { ChartOfAccounts } from '../src/pages/ChartOfAccounts';
const { fetcher } = vi.hoisted(() => ({ fetcher: vi.fn() }));
vi.mock('../src/auth/AuthContext', () => ({ useAuth: () => ({ authFetch: fetcher }) }));
vi.mock('../src/components/Gating', () => ({ useTenantContext: () => ({ hasPermission: () => true }), PermissionDeniedCard: () => null }));
vi.mock('../src/components/RecordHistoryPanel', () => ({ useRecordHistoryAccess: () => ({ canShow: false }), RecordHistoryPanel: () => null }));
afterEach(() => { cleanup(); fetcher.mockReset(); });
it('saves the year-end clearing classification chosen for a new account', async () => {
  let posted: any;
  fetcher.mockImplementation(async (url: string, init?: RequestInit) => {
    if (init?.method === 'POST') { posted = JSON.parse(String(init.body)); return { ok: true, json: async () => ({ id: 3 }) }; }
    if (url.includes('Accounts?')) return { ok: true, json: async () => ({ accounts: [] }) };
    if (url.endsWith('/Accounts')) return { ok: true, json: async () => [] };
    return { ok: false, json: async () => ({}) };
  });
  render(<MemoryRouter initialEntries={['/accounts']}><Routes><Route element={<Outlet context={{ currentOrg: { slug: 'books', currency: 'USD' }, hasPermission: () => true }} />}><Route path="/accounts" element={<ChartOfAccounts />} /></Route></Routes></MemoryRouter>);
  fireEvent.click(await screen.findByRole('button', { name: 'Add Account' }));
  fireEvent.change(screen.getByPlaceholderText('e.g. 10115'), { target: { value: '19001' } });
  fireEvent.change(screen.getByPlaceholderText('e.g. Payroll Checking Account'), { target: { value: 'Unapplied receipts' } });
  fireEvent.click(screen.getByRole('checkbox', { name: /review as suspense or clearing/i }));
  fireEvent.click(screen.getByRole('button', { name: 'Create Account' }));
  await waitFor(() => expect(posted?.isSuspenseOrClearing).toBe(true));
}, 20000);
