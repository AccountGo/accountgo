import test from 'node:test';
import assert from 'node:assert/strict';
import {
  formatStatementBalanceBanner,
  statementBalanceNotice,
} from '../src/lib/statementBalance.ts';

test('statementBalanceNotice provides clear copy for every reason code', () => {
  const codes = [
    'not_booked',
    'other_account',
    'multiple_accounts',
    'currency_mismatch',
    'currency_missing',
    'ambiguous_latest',
    'date_missing',
  ];

  for (const code of codes) {
    const notice = statementBalanceNotice(code);
    assert.ok(notice, `Code ${code} must have notice copy`);
    assert.ok(notice.length > 20, `Notice copy for ${code} should be informative`);
  }

  assert.equal(statementBalanceNotice(null), null);
  assert.equal(statementBalanceNotice(undefined), null);
  assert.equal(statementBalanceNotice(''), null);
  assert.equal(statementBalanceNotice('unknown_code'), null);
});

test('formatStatementBalanceBanner formats positive amount with date and account suffix', () => {
  const formatAmt = (n, ccy) => `${ccy ?? 'USD'} ${n.toFixed(2)}`;
  const offer = {
    balance: 1250.5,
    asOfDate: '2026-10-15T00:00:00Z',
    currency: 'EUR',
    account: '1234',
  };

  const banner = formatStatementBalanceBanner(offer, formatAmt);
  assert.equal(
    banner,
    'Statement closing balance: EUR 1250.50, as of 2026-10-15 for account ending 1234.',
  );
});

test('formatStatementBalanceBanner formats negative amount with (overdrawn), with and without date', () => {
  const formatAmt = (n, ccy) => `${ccy ?? 'USD'} ${n.toFixed(2)}`;

  const withDate = {
    balance: -80.25,
    asOfDate: '2026-10-15',
    currency: 'USD',
    account: '5678',
  };
  assert.equal(
    formatStatementBalanceBanner(withDate, formatAmt),
    'Statement closing balance: USD -80.25 (overdrawn), as of 2026-10-15 for account ending 5678.',
  );

  const withoutDate = {
    balance: -500.0,
    asOfDate: null,
    currency: 'EUR',
    account: null,
  };
  assert.equal(
    formatStatementBalanceBanner(withoutDate, formatAmt),
    'Statement closing balance: EUR -500.00 (overdrawn), no date in the file.',
  );
});
