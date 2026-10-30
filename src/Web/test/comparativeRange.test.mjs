import assert from 'node:assert/strict';
import test from 'node:test';
import { comparativeRange } from '../src/lib/comparativeRange.ts';
import { journalVoucherTypeLabel, SELECTABLE_JOURNAL_VOUCHER_TYPES } from '../src/lib/journalVoucherTypes.ts';

const calendarYears = [
  { fiscalYearCode: 'FY2025', fiscalYearName: 'FY2025', startDate: '2025-01-01', endDate: '2025-12-31', isActive: true },
  { fiscalYearCode: 'FY2026', fiscalYearName: 'FY2026', startDate: '2026-01-01', endDate: '2026-12-31', isActive: true },
  { fiscalYearCode: 'FY2027', fiscalYearName: 'FY2027', startDate: '2027-01-01', endDate: '2027-12-31', isActive: true },
];

const aprilMarchYears = [
  { fiscalYearCode: 'FY2025', fiscalYearName: 'FY2025', startDate: '2025-04-01', endDate: '2026-03-31', isActive: true },
  { fiscalYearCode: 'FY2026', fiscalYearName: 'FY2026', startDate: '2026-04-01', endDate: '2027-03-31', isActive: true },
  { fiscalYearCode: 'FY2027', fiscalYearName: 'FY2027', startDate: '2027-04-01', endDate: '2028-03-31', isActive: true },
];

test('comparativeRange P&L calendar FY-to-date shifts one year', () => {
  assert.deepEqual(
    comparativeRange({ from: '2027-01-01', to: '2027-03-15' }, 'year', calendarYears, 'income'),
    { from: '2026-01-01', to: '2026-03-15' },
  );
});

test('comparativeRange P&L April–March year-to-date shifts one year', () => {
  assert.deepEqual(
    comparativeRange({ from: '2027-04-01', to: '2027-06-15' }, 'year', aprilMarchYears, 'income'),
    { from: '2026-04-01', to: '2026-06-15' },
  );
});

test('comparativeRange P&L month and custom spans shift one year', () => {
  assert.deepEqual(
    comparativeRange({ from: '2027-03-01', to: '2027-03-15' }, 'month', calendarYears, 'income'),
    { from: '2026-03-01', to: '2026-03-15' },
  );
  assert.deepEqual(
    comparativeRange({ from: '2027-02-10', to: '2027-03-20' }, 'custom', calendarYears, 'income'),
    { from: '2026-02-10', to: '2026-03-20' },
  );
});

test('comparativeRange Balance Sheet is as at previous fiscal year end', () => {
  assert.deepEqual(
    comparativeRange({ from: '', to: '2027-03-15' }, 'custom', calendarYears, 'balanceSheet'),
    { from: '', to: '2026-12-31' },
  );
  assert.deepEqual(
    comparativeRange({ from: '', to: '2027-06-15' }, 'custom', aprilMarchYears, 'balanceSheet'),
    { from: '', to: '2027-03-31' },
  );
});

test('comparativeRange returns null when prior period starts before books start', () => {
  const booksFrom2027 = [
    { fiscalYearCode: 'FY2027', fiscalYearName: 'FY2027', startDate: '2027-01-01', endDate: '2027-12-31', isActive: true },
  ];
  assert.equal(
    comparativeRange({ from: '2027-01-01', to: '2027-03-15' }, 'year', booksFrom2027, 'income'),
    null,
  );
  assert.equal(
    comparativeRange({ from: '', to: '2027-03-15' }, 'custom', booksFrom2027, 'balanceSheet'),
    null,
  );
});

test('journal voucher labels map by enum value and Closing entries is not selectable', () => {
  assert.equal(journalVoucherTypeLabel(1), 'Opening balances');
  assert.equal(journalVoucherTypeLabel(2), 'Closing entries');
  assert.equal(journalVoucherTypeLabel(7), 'Prior-period adjustment');
  assert.ok(!SELECTABLE_JOURNAL_VOUCHER_TYPES.some(t => t.value === 2));
  assert.ok(SELECTABLE_JOURNAL_VOUCHER_TYPES.some(t => t.value === 7));
});
