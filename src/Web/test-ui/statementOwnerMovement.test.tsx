import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { StatementLineOwnerMovementDialog } from '../src/components/StatementLineOwnerMovementDialog';
import type { BankStatementLine } from '../src/types/banking';

const fetcher = vi.fn();
vi.mock('../src/auth/AuthContext', () => ({ useAuth: () => ({ authFetch: fetcher }) }));
afterEach(() => { cleanup(); fetcher.mockReset(); });
const line = { id: 42, amount: -25, postedDate: '2026-06-15', payee: 'Personal transfer', isMatched: false, isExcluded: false } as BankStatementLine;
const action = { accountId: 10, accountCode: '30800', accountName: 'My drawings', label: 'Owner drawing' };

it('shows the account and hint, and sends only memo and remember for a single line', async () => {
  fetcher.mockResolvedValue(new Response('{}', { status: 200 }));
  const saved = vi.fn();
  render(<StatementLineOwnerMovementDialog lines={[line]} action={action} formatCurrency={n => `$${n}`} onClose={vi.fn()} onSaved={saved} />);
  expect(screen.getByText('Account: My drawings (30800)')).toBeTruthy();
  expect(screen.getByText(/doesn't reduce profit/)).toBeTruthy();
  fireEvent.change(screen.getByLabelText('Memo (optional)'), { target: { value: 'Personal withdrawal' } });
  fireEvent.click(screen.getByRole('checkbox'));
  fireEvent.click(screen.getByRole('button', { name: 'Record' }));
  await waitFor(() => expect(saved).toHaveBeenCalledOnce());
  const [url, options] = fetcher.mock.calls[0];
  expect(url).toBe('/api/Banking/StatementLines/42/RecordOwnerMovement');
  expect(JSON.parse(options.body)).toEqual({ memo: 'Personal withdrawal', rememberRule: true });
});

it('confirms a reconciled period and retries the entire bulk selection', async () => {
  fetcher.mockResolvedValueOnce(new Response(JSON.stringify({ code: 'RECONCILED_PERIOD_CONFIRM_REQUIRED', message: 'Already reconciled. Continue?' }), { status: 409 }))
    .mockResolvedValueOnce(new Response('{}', { status: 200 }));
  const saved = vi.fn();
  render(<StatementLineOwnerMovementDialog lines={[line, { ...line, id: 43 }]} action={action} formatCurrency={String} onClose={vi.fn()} onSaved={saved} />);
  fireEvent.click(screen.getByRole('button', { name: 'Record' }));
  const confirm = await screen.findByRole('button', { name: 'Post anyway' });
  expect(saved).not.toHaveBeenCalled();
  fireEvent.click(confirm);
  await waitFor(() => expect(saved).toHaveBeenCalledOnce());
  expect(fetcher.mock.calls[1][0]).toBe('/api/Banking/StatementLines/BulkRecordOwnerMovement?confirmReconciledPeriod=true');
  expect(JSON.parse(fetcher.mock.calls[1][1].body).lineIds).toEqual([42, 43]);
});
