import { afterEach, expect, test, vi } from 'vitest';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Outlet, Route, Routes } from 'react-router-dom';
import RecurringSchedules from '../src/pages/RecurringSchedules';
const { authFetch } = vi.hoisted(() => ({ authFetch: vi.fn() }));
vi.mock('../src/auth/AuthContext', () => ({ useAuth: () => ({ authFetch }) }));
vi.mock('../src/components/Gating', () => ({ usePermissions: () => ({ hasPermission: () => true }), ReadOnlyModuleBanner: () => null }));
afterEach(() => { cleanup(); vi.resetAllMocks(); });
test.each(['Issued', 'Failed', 'Aborted'])('due legacy schedule handles %s without premature navigation', async (outcome) => {
  const schedule = { id: 7, name: 'Retainer', customerName: 'Client', customerArchived: false,
    status: 1, cadence: 2, action: 2, interval: 1, occurrenceCount: 0, nextRunDate: '2026-11-15', isOverdue: true };
  let finishRun!: (value: Response) => void;
  authFetch.mockImplementation((url: string) => url.endsWith('/run')
    ? new Promise<Response>(resolve => { finishRun = resolve; })
    : Promise.resolve(new Response(JSON.stringify([schedule]))));
  render(<MemoryRouter initialEntries={['/org/demo/sales/recurring']}><Routes>
    <Route element={<Outlet context={{ currentOrg: { slug: 'demo' }, canWrite: () => true }} />}>
      <Route path="/org/:orgSlug/sales/recurring" element={<RecurringSchedules />} />
      <Route path="/org/:orgSlug/sales/recurring/:id" element={<p>Schedule history</p>} />
    </Route>
  </Routes></MemoryRouter>);
  const button = await screen.findByRole('button', { name: 'Run now: Retainer' });
  expect(screen.getByText('Every month')).toBeTruthy();
  fireEvent.click(button);
  expect(screen.queryByText('Schedule history')).toBeNull();
  expect((button as HTMLButtonElement).disabled).toBe(true);
  expect(authFetch).toHaveBeenCalledWith('/api/RecurringSchedules/7/run', {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{"runDate":"2026-11-15"}',
  });
  finishRun(new Response(JSON.stringify({ outcome, message: 'The schedule could not generate an invoice.' })));
  if (outcome === 'Issued') {
    expect(await screen.findByText('Schedule history')).toBeTruthy();
    expect(authFetch).toHaveBeenCalledTimes(2);
  } else {
    expect((await screen.findByRole('alert')).textContent).toContain('The schedule could not generate an invoice.');
    expect(screen.queryByText('Schedule history')).toBeNull();
    expect((await screen.findByRole('button', { name: 'Run now: Retainer' }) as HTMLButtonElement).disabled).toBe(false);
    expect(authFetch).toHaveBeenCalledTimes(3);
  }
});
