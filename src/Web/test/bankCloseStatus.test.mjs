import test from 'node:test';
import assert from 'node:assert/strict';
import { closeStatusMessage, shouldShowCloseStatus, unreconciledBanks } from '../src/lib/bankCloseStatus.ts';

const status = (overrides = {}) => ({
  periodEnd: '2026-09-30T00:00:00Z',
  periodLabel: 'September 2026',
  reconciledCount: 1,
  totalCount: 2,
  isComplete: false,
  banks: [
    { bankId: 1, name: 'Checking', isReconciled: true },
    { bankId: 2, name: 'Savings', isReconciled: false },
  ],
  ...overrides,
});

test('closeStatusMessage names the month and the counts', () => {
  assert.equal(closeStatusMessage(status()), 'Bank accounts reconciled through September 2026: 1 of 2');
});

test('shouldShowCloseStatus hides the widget without active banks', () => {
  assert.equal(shouldShowCloseStatus(null), false);
  assert.equal(shouldShowCloseStatus(undefined), false);
  assert.equal(shouldShowCloseStatus(status({ totalCount: 0, banks: [] })), false);
  assert.equal(shouldShowCloseStatus(status()), true);
});

test('unreconciledBanks lists only the banks still open', () => {
  assert.deepEqual(unreconciledBanks(status()).map((b) => b.bankId), [2]);
});
