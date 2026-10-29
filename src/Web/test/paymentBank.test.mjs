import test from 'node:test';
import assert from 'node:assert/strict';
import { carriedOverPaymentInstructions, paymentBankChoice, paymentInstructionsPayload } from '../src/lib/paymentBank.ts';

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

test('sends typed text, an empty string for a deliberate clear, and nothing when never touched', () => {
  assert.equal(paymentInstructionsPayload('  Wire to us ', false), 'Wire to us');
  assert.equal(paymentInstructionsPayload('', true), '');
  assert.equal(paymentInstructionsPayload('   ', true), '');
  assert.equal(paymentInstructionsPayload('', false), undefined);
});

test('duplicating does not carry over bank text frozen at issue', () => {
  assert.equal(carriedOverPaymentInstructions('Pay by bank transfer to:\nBank: X\nReference: INV-1'), '');
  assert.equal(carriedOverPaymentInstructions('Wire to our Singapore account'), 'Wire to our Singapore account');
  assert.equal(carriedOverPaymentInstructions(null), '');
  assert.equal(carriedOverPaymentInstructions(''), '');
});
