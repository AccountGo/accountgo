import test from 'node:test';
import assert from 'node:assert/strict';
import {
  canEnableCloseButton,
  fiscalYearStatusLabel,
  normalizeFiscalYearCloseStatus,
} from '../src/lib/yearEndClose.ts';

test('fiscalYearStatusLabel maps numeric and string statuses', () => {
  assert.equal(fiscalYearStatusLabel(1), 'Open');
  assert.equal(fiscalYearStatusLabel(2), 'Ended — ready to close');
  assert.equal(fiscalYearStatusLabel(3), 'Closed');
  assert.equal(fiscalYearStatusLabel('Ended'), 'Ended — ready to close');
});

test('canEnableCloseButton requires permission, non-closed status, and no blockers', () => {
  assert.equal(
    canEnableCloseButton({ status: 2, blockingCount: 0, canClosePermission: true }),
    true,
  );
  assert.equal(
    canEnableCloseButton({ status: 2, blockingCount: 1, canClosePermission: true }),
    false,
  );
  assert.equal(
    canEnableCloseButton({ status: 3, blockingCount: 0, canClosePermission: true }),
    false,
  );
  assert.equal(
    canEnableCloseButton({ status: 2, blockingCount: 0, canClosePermission: false }),
    false,
  );
});

test('normalizeFiscalYearCloseStatus accepts enum names', () => {
  assert.equal(normalizeFiscalYearCloseStatus('Closed'), 3);
});
