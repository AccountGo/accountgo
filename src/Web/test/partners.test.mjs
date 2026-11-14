import test from 'node:test';
import assert from 'node:assert/strict';
import { localDateInputValue, percentageTotal, splitAmount, validatePartnerSetup } from '../src/lib/partners.ts';

test('localDateInputValue uses the local Gregorian calendar date for a date input', () => {
  assert.equal(localDateInputValue(new Date(2026, 10, 14, 23, 30)), '2026-11-14');
});

test('percentageTotal adds partner ratios without losing decimal precision', () => {
  assert.equal(percentageTotal([33.3333, 33.3333, 33.3334]), 100);
});

test('splitAmount allocates cents deterministically and exactly', () => {
  assert.deepEqual(splitAmount(1000.01, [{ id: 1, percent: 33.3333 }, { id: 2, percent: 33.3333 }, { id: 3, percent: 33.3334 }]), [333.34, 333.33, 333.34]);
});

test('validatePartnerSetup checks at least two partners, 100 percent and balance totals', () => {
  assert.equal(validatePartnerSetup([{ name: 'A', percent: 60, capital: 60, drawings: 0 }, { name: 'B', percent: 40, capital: 40, drawings: 0 }], 100, 0), null);
  assert.match(validatePartnerSetup([{ name: 'A', percent: 100, capital: 100, drawings: 0 }], 100, 0), /two partners/i);
});
