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

it('undo confirms the reversal, sends the optional reason and retries after period confirmation', async () => {
  fetcher.mockResolvedValueOnce(new Response(JSON.stringify({ code: 'RECONCILED_PERIOD_CONFIRM_REQUIRED', message: 'Already reconciled. Continue?' }), { status: 409 }))
    .mockResolvedValueOnce(new Response('{}', { status: 200 }));
  const saved = vi.fn();
  render(<StatementLineOwnerMovementDialog lines={[{ ...line, isMatched: true, matchedOwnerAction: 'drawing' }]} action={action} undo formatCurrency={String} onClose={vi.fn()} onSaved={saved} />);
  expect(screen.getByText(/A reversing entry is posted and the line goes back to unmatched/)).toBeTruthy();
  fireEvent.change(screen.getByLabelText('Reason (optional)'), { target: { value: 'Correction' } });
  fireEvent.click(screen.getByRole('button', { name: 'Undo' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Post anyway' }));
  await waitFor(() => expect(saved).toHaveBeenCalledOnce());
  expect(fetcher.mock.calls[1][0]).toBe('/api/Banking/StatementLines/42/UndoOwnerMovement?confirmReconciledPeriod=true');
  expect(JSON.parse(fetcher.mock.calls[1][1].body)).toEqual({ reason: 'Correction' });
});

it('requires a partner and updates the account before recording', async () => {
  fetcher.mockResolvedValue(new Response('{}', { status: 200 }));
  const partners = [
    { partnerId: 1, name: 'Avery', drawing: { accountId: 21, accountCode: '30851', accountName: 'Drawings – Avery', label: 'Partner drawing' }, contribution: null },
    { partnerId: 2, name: 'Blake', drawing: { accountId: 22, accountCode: '30852', accountName: 'Drawings – Blake', label: 'Partner drawing' }, contribution: null },
  ];
  const saved = vi.fn();
  render(<StatementLineOwnerMovementDialog lines={[line]} action={partners[0].drawing!} partners={partners} formatCurrency={String} onClose={vi.fn()} onSaved={saved} />);
  const submit = screen.getByRole('button', { name: 'Record' }) as HTMLButtonElement;
  expect(screen.queryByText(/30851|30852/)).toBeNull();
  expect(submit.disabled).toBe(true);
  fireEvent.change(screen.getByLabelText('Partner'), { target: { value: '2' } });
  expect(screen.getByText('Account: Drawings – Blake (30852)')).toBeTruthy();
  expect(screen.getByText(/Money Blake took out/)).toBeTruthy();
  expect(submit.disabled).toBe(false);
  fireEvent.click(submit);
  await waitFor(() => expect(saved).toHaveBeenCalledOnce());
  expect(JSON.parse(fetcher.mock.calls[0][1].body).partnerId).toBe(2);
});
