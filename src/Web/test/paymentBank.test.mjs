import test from 'node:test';
import assert from 'node:assert/strict';
import { paymentBankChoice } from '../src/lib/paymentBank.ts';

const banks = [
  { id: 1, name: 'Checking', accountNumberMasked: '••1234' },
  { id: 2, name: 'Savings', accountNumberMasked: null },
];

test('starts with the free-text option and lists active banks', () => {
  const options = paymentBankChoice(banks, null);
  assert.deepEqual(options.map((o) => o.value), ['', '1', '2']);
  assert.equal(options[1].label, 'Checking (••1234)');
  assert.equal(options[2].label, 'Savings');
});

test('keeps a saved but unavailable bank visible and flagged', () => {
  const options = paymentBankChoice(banks, 9);
  assert.equal(options.at(-1).value, '9');
  assert.match(options.at(-1).label, /unavailable/i);
});

test('does not add a stray option when the saved bank is still active', () => {
  assert.equal(paymentBankChoice(banks, 2).length, 3);
});

test('ignores cash accounts that have no bank record', () => {
  const options = paymentBankChoice([...banks, { id: 0, name: '1010 Cash', accountNumberMasked: null }], null);
  assert.deepEqual(options.map((o) => o.value), ['', '1', '2']);
});

test('does not flag the saved bank when the picker could not be loaded', () => {
  const options = paymentBankChoice([], 5, false);
  assert.equal(options.at(-1).label, 'Selected bank account');
});
