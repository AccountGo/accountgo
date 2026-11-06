import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import { MemoryRouter, Outlet, Route, Routes } from 'react-router-dom';
import Banking from '../src/pages/Banking';
const fetcher = async () => ({ ok: true, json: async () => ({ accounts: [], unassignedCash: [] }) });
vi.mock('../src/auth/AuthContext', () => ({ useAuth: () => ({ authFetch: fetcher }) }));
vi.mock('../src/components/Gating', () => ({ useTenantContext: () => ({ hasModule: () => false, hasPermission: () => true }) }));
afterEach(cleanup);
it('uses the household bank account empty state', async () => {
  const context = { currentOrg: { slug: 'books', currency: 'USD', isPersonalBooks: true } };
  render(<MemoryRouter initialEntries={['/banking']}><Routes><Route element={<Outlet context={context} />}><Route path="/banking" element={<Banking />} /></Route></Routes></MemoryRouter>);
  expect(await screen.findByText('Add your first bank account')).toBeTruthy();
});
