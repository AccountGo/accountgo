import test from 'node:test';
import assert from 'node:assert/strict';
import { parseReconciledPeriodConflict, withConfirmReconciledPeriod } from '../src/lib/reconciledPeriod.ts';

test('withConfirmReconciledPeriod only appends the flag once confirmed', () => {
  assert.equal(withConfirmReconciledPeriod('/api/Banking/Transactions', false), '/api/Banking/Transactions');
  assert.equal(withConfirmReconciledPeriod('/api/Banking/Transactions', true), '/api/Banking/Transactions?confirmReconciledPeriod=true');
  assert.equal(withConfirmReconciledPeriod('/api/X?a=1', true), '/api/X?a=1&confirmReconciledPeriod=true');
});

test('parseReconciledPeriodConflict recognises the 409 confirmation code', () => {
  const prompt = parseReconciledPeriodConflict(409, {
    code: 'RECONCILED_PERIOD_CONFIRM_REQUIRED',
    message: 'Checking is reconciled through 2026-09-30.',
    bankName: 'Checking',
    statementEndDate: '2026-09-30',
    postingDate: '2026-09-15',
  });
  assert.equal(prompt?.message, 'Checking is reconciled through 2026-09-30.');
  assert.equal(prompt?.bankName, 'Checking');
  assert.equal(prompt?.postingDate, '2026-09-15');
});

test('parseReconciledPeriodConflict falls back to detail and ignores other conflicts', () => {
  assert.equal(
    parseReconciledPeriodConflict(409, { code: 'RECONCILED_PERIOD_CONFIRM_REQUIRED', detail: 'Detail text' })?.message,
    'Detail text',
  );
  assert.equal(parseReconciledPeriodConflict(409, { code: 'RECONCILED' }), null);
  assert.equal(parseReconciledPeriodConflict(400, { code: 'RECONCILED_PERIOD_CONFIRM_REQUIRED' }), null);
  assert.equal(parseReconciledPeriodConflict(409, null), null);
});
