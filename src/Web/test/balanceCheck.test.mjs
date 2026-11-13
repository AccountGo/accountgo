import test from 'node:test';
import assert from 'node:assert/strict';
import { canShowBalanceCheck, formatBalanceCheckMessage, initialBalanceCheckDate, parseBalanceInput } from '../src/lib/balanceCheck.ts';

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

test('parseBalanceInput formats and parses statement closing balance handoff values', () => {
  const closingBalance = 2450.75;
  const asInputString = closingBalance.toFixed(2);
  assert.equal(parseBalanceInput(asInputString), 2450.75);

  const negativeClosingBalance = -150.20;
  assert.equal(parseBalanceInput(negativeClosingBalance.toFixed(2)), -150.20);
});

test('initialBalanceCheckDate keeps the statement date, empties missing statement date, or defaults to today', () => {
  const today = '2026-11-12';
  // With an initial balance and date: uses the statement date
  assert.equal(initialBalanceCheckDate(100.5, '2026-10-15T00:00:00Z', today), '2026-10-15');
  assert.equal(initialBalanceCheckDate('100.50', '2026-10-15', today), '2026-10-15');

  // With an initial balance but no date: empty (required for user to enter)
  assert.equal(initialBalanceCheckDate(100.5, null, today), '');
  assert.equal(initialBalanceCheckDate(100.5, undefined, today), '');
  assert.equal(initialBalanceCheckDate(0, null, today), '');
  assert.equal(initialBalanceCheckDate(-50.25, null, today), '');

  // Manual check without initial balance: defaults to today
  assert.equal(initialBalanceCheckDate(null, null, today), today);
  assert.equal(initialBalanceCheckDate(undefined, undefined, today), today);
});

test('formatBalanceCheckMessage formats raw numeric difference with localized currency', () => {
  const formatUsd = (n) => `$${n.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;

  // When bank shows less
  const lessResult = {
    isMatch: false,
    difference: -1234.56,
    message: 'The bank shows 1234.56 less than your books. Look for a payment you have not recorded or a deposit that has not arrived yet.',
  };
  assert.equal(
    formatBalanceCheckMessage(lessResult, formatUsd),
    'The bank shows $1,234.56 less than your books. Look for a payment you have not recorded or a deposit that has not arrived yet.',
  );

  // When bank shows more
  const moreResult = {
    isMatch: false,
    difference: 1234.56,
    message: 'The bank shows 1234.56 more than your books. Look for a deposit you have not recorded or a payment recorded twice.',
  };
  assert.equal(
    formatBalanceCheckMessage(moreResult, formatUsd),
    'The bank shows $1,234.56 more than your books. Look for a deposit you have not recorded or a payment recorded twice.',
  );

  // When balances match
  const matchResult = {
    isMatch: true,
    difference: 0,
    message: 'Your books match the bank.',
  };
  assert.equal(formatBalanceCheckMessage(matchResult, formatUsd), 'Your books match the bank.');
});

