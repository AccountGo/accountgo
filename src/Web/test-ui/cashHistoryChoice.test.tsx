import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Outlet, Route, Routes } from 'react-router-dom';
import Banking from '../src/pages/Banking';
const { fetcher } = vi.hoisted(() => ({ fetcher: vi.fn() }));
vi.mock('../src/auth/AuthContext', () => ({ useAuth: () => ({ authFetch: fetcher }) }));
vi.mock('../src/components/Gating', () => ({ useTenantContext: () => ({ hasModule: () => false, hasPermission: () => true }) }));
afterEach(() => { cleanup(); fetcher.mockReset(); });
it('submits the explicit choice to leave cash history unassigned', async () => {
  let payload: any;
  fetcher.mockImplementation(async (url: string, init?: RequestInit) => {
    if (url.includes('CashHistory')) return { ok: true, json: async () => [
      { accountId: 1, description: 'Checking', receiptCount: 1, paymentCount: 0, otherLineCount: 0, balance: 100 },
      { accountId: 2, description: 'Cash', receiptCount: 1, paymentCount: 0, otherLineCount: 0, balance: 50 },
    ] };
    if (init?.method === 'POST') { payload = JSON.parse(String(init.body)); return { ok: true, json: async () => ({ id: 3 }) }; }
    return { ok: true, json: async () => ({ accounts: [], unassignedCash: [] }) };
  });
  render(<MemoryRouter initialEntries={['/banking']}><Routes><Route element={<Outlet context={{ currentOrg: { slug: 'books', currency: 'USD' } }} />}><Route path="/banking" element={<Banking />} /></Route></Routes></MemoryRouter>);
  fireEvent.click(await screen.findByRole('button', { name: 'Add bank account' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Continue without assigning' }));
  fireEvent.change(screen.getByLabelText(/Display name/i), { target: { value: 'New bank' } });
  fireEvent.change(screen.getByLabelText('Currency'), { target: { value: 'USD' } });
  fireEvent.click(screen.getByRole('button', { name: 'Add account' }));
  await waitFor(() => expect(payload?.leaveCashHistoryUnassigned).toBe(true));
}, 20000);

it('lets a later bank take over a remaining unassigned cash group', async () => {
  let payload: any;
  fetcher.mockImplementation(async (url: string, init?: RequestInit) => {
    if (url.includes('CashHistory')) return { ok: true, json: async () => [{ accountId: 2, description: 'Cash', receiptCount: 1, paymentCount: 0, otherLineCount: 0, balance: 50 }] };
    if (url.includes('accounting-policy')) return { ok: true, json: async () => ({ booksStartDate: '2026-01-01' }) };
    if (init?.method === 'POST') { payload = JSON.parse(String(init.body)); return { ok: true, json: async () => ({ id: 4 }) }; }
    return { ok: true, json: async () => ({ accounts: [{ id: 3, name: 'First bank', type: 1, currencyCode: 'USD', balance: 0 }], unassignedCash: [] }) };
  });
  render(<MemoryRouter initialEntries={['/banking']}><Routes><Route element={<Outlet context={{ currentOrg: { slug: 'books', currency: 'USD' } }} />}><Route path="/banking" element={<Banking />} /></Route></Routes></MemoryRouter>);
  fireEvent.click(await screen.findByRole('button', { name: 'Add bank account' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Assign to New account' }));
  fireEvent.change(screen.getByLabelText(/Display name/i), { target: { value: 'Cash bank' } });
  fireEvent.change(screen.getByLabelText('Currency'), { target: { value: 'USD' } });
  fireEvent.click(screen.getByRole('button', { name: 'Add account' }));
  await waitFor(() => expect(payload?.adoptAccountId).toBe(2));
}, 20000);
