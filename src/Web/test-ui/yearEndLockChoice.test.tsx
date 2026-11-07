import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { YearEndCloseCard } from '../src/components/YearEndCloseCard';
const { fetcher } = vi.hoisted(() => ({ fetcher: vi.fn() }));
vi.mock('../src/auth/AuthContext', () => ({ useAuth: () => ({ authFetch: fetcher }) }));
afterEach(() => { cleanup(); fetcher.mockReset(); });
it('shows the independent lock change and requires acknowledgement before reopening', async () => {
  let submitted: any;
  fetcher.mockImplementation(async (url: string, init?: RequestInit) => {
    if (init?.method === 'POST') { submitted = JSON.parse(String(init.body)); return { ok: true, json: async () => ({}) }; }
    if (url.endsWith('/YearEnd/FY2025')) return { ok: true, json: async () => ({ fiscalYearCode: 'FY2025', startDate: '2025-01-01', endDate: '2025-12-31', status: 3, blocking: [], warnings: [], closingLines: [], canClose: false, currentBooksLockedThrough: '2026-03-31', proposedBooksLockedThrough: null, requiresLockAcknowledgement: true }) };
    return { ok: true, json: async () => [{ fiscalYearCode: 'FY2025', startDate: '2025-01-01', endDate: '2025-12-31', status: 3 }] };
  });
  render(<MemoryRouter><YearEndCloseCard canClose /></MemoryRouter>);
  fireEvent.click(await screen.findByRole('button', { name: 'Reopen' }));
  expect(await screen.findByText(/2026-03-31/)).toBeTruthy();
  expect(screen.getByText(/no lock/i)).toBeTruthy();
  fireEvent.change(screen.getByLabelText(/Reason/), { target: { value: 'Correct the accrual' } });
  const submit = screen.getByRole('button', { name: 'Reopen year' }) as HTMLButtonElement;
  expect(submit.disabled).toBe(true);
  fireEvent.click(screen.getByRole('checkbox', { name: /acknowledge/i }));
  fireEvent.click(submit);
  await waitFor(() => expect(submitted).toMatchObject({ acknowledgeIndependentLock: true, expectedLockThrough: '2026-03-31' }));
}, 20000);

it('allows an authorized user to prepare missing years without changing the list GET', async () => {
  let prepared = false;
  fetcher.mockImplementation(async (_url: string, init?: RequestInit) => {
    if (init?.method === 'POST') { prepared = true; return { ok: true, json: async () => ({}) }; }
    return { ok: true, json: async () => prepared ? [{ fiscalYearCode: 'FY2025', startDate: '2025-01-01', endDate: '2025-12-31', status: 2 }] : [] };
  });
  render(<MemoryRouter><YearEndCloseCard canClose /></MemoryRouter>);
  fireEvent.click(await screen.findByRole('button', { name: /prepare fiscal years/i }));
  await screen.findByText('FY2025');
  expect(prepared).toBe(true);
}, 20000);
