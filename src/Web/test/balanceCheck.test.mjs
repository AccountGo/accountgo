import test from 'node:test';
import assert from 'node:assert/strict';
import { canShowBalanceCheck, parseBalanceInput } from '../src/lib/balanceCheck.ts';

test('parseBalanceInput accepts plain, grouped, negative, and bracketed numbers', () => {
  assert.equal(parseBalanceInput('1250.50'), 1250.5);
  assert.equal(parseBalanceInput(' 1,250.50 '), 1250.5);
  assert.equal(parseBalanceInput('-80'), -80);
  assert.equal(parseBalanceInput('(80.25)'), -80.25);
});

test('parseBalanceInput rejects blanks and text', () => {
  assert.equal(parseBalanceInput(''), null);
  assert.equal(parseBalanceInput('   '), null);
  assert.equal(parseBalanceInput('abc'), null);
  assert.equal(parseBalanceInput('12abc'), null);
});

test('the balance check is hidden when Accounting is on, without permission, or for archived accounts', () => {
  assert.equal(canShowBalanceCheck({ hasFinancials: false, canView: true, archived: false }), true);
  assert.equal(canShowBalanceCheck({ hasFinancials: true, canView: true, archived: false }), false);
  assert.equal(canShowBalanceCheck({ hasFinancials: false, canView: false, archived: false }), false);
  assert.equal(canShowBalanceCheck({ hasFinancials: false, canView: true, archived: true }), false);
});
