import assert from 'node:assert/strict';
import test from 'node:test';
import { addCalendarYears, comparativeRange } from '../src/lib/comparativeRange.ts';
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

const ymd = (d) =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

test('comparativeRange P&L calendar FY-to-date shifts one year', () => {
  assert.deepEqual(
    comparativeRange({ from: '2027-01-01', to: '2027-03-15' }, 'year', calendarYears, 'income', '2025-01-01'),
    { from: '2026-01-01', to: '2026-03-15' },
  );
});

test('comparativeRange P&L April–March year-to-date shifts one year', () => {
  assert.deepEqual(
    comparativeRange({ from: '2027-04-01', to: '2027-06-15' }, 'year', aprilMarchYears, 'income', '2025-04-01'),
    { from: '2026-04-01', to: '2026-06-15' },
  );
});

test('comparativeRange P&L month and custom spans shift one year', () => {
  assert.deepEqual(
    comparativeRange({ from: '2027-03-01', to: '2027-03-15' }, 'month', calendarYears, 'income', '2025-01-01'),
    { from: '2026-03-01', to: '2026-03-15' },
  );
  assert.deepEqual(
    comparativeRange({ from: '2027-02-10', to: '2027-03-20' }, 'custom', calendarYears, 'income', '2025-01-01'),
    { from: '2026-02-10', to: '2026-03-20' },
  );
});

test('addCalendarYears clamps leap day in both directions', () => {
  assert.equal(ymd(addCalendarYears(new Date(2028, 1, 29), -1)), '2027-02-28');
  assert.equal(ymd(addCalendarYears(new Date(2027, 1, 28), 1)), '2028-02-28');
  assert.deepEqual(
    comparativeRange({ from: '2028-02-01', to: '2028-02-29' }, 'custom', calendarYears, 'income', '2025-01-01'),
    { from: '2027-02-01', to: '2027-02-28' },
  );
});

test('comparativeRange Balance Sheet is day before containing FY start (lazy years)', () => {
  assert.deepEqual(
    comparativeRange({ from: '', to: '2027-03-15' }, 'custom', calendarYears, 'balanceSheet', '2025-01-01'),
    { from: '', to: '2026-12-31' },
  );
  assert.deepEqual(
    comparativeRange({ from: '', to: '2027-06-15' }, 'custom', aprilMarchYears, 'balanceSheet', '2025-04-01'),
    { from: '', to: '2027-03-31' },
  );
  // Prior FY need not be stored — only the year containing as-of.
  const onlyFy2027 = [
    { fiscalYearCode: 'FY2027', fiscalYearName: 'FY2027', startDate: '2027-01-01', endDate: '2027-12-31', isActive: true },
  ];
  assert.deepEqual(
    comparativeRange({ from: '', to: '2027-03-15' }, 'custom', onlyFy2027, 'balanceSheet', '2027-01-01'),
    { from: '', to: '2026-12-31' },
  );
  const onlyAprilMarch = [
    { fiscalYearCode: 'FY2027-28', fiscalYearName: 'FY2027-28', startDate: '2027-04-01', endDate: '2028-03-31', isActive: true },
  ];
  assert.deepEqual(
    comparativeRange({ from: '', to: '2027-06-30' }, 'custom', onlyAprilMarch, 'balanceSheet', '2027-04-01'),
    { from: '', to: '2027-03-31' },
  );
});

test('comparativeRange uses booksStart, not earliest stored FY', () => {
  const fy2025 = [
    { fiscalYearCode: 'FY2025', fiscalYearName: 'FY2025', startDate: '2025-01-01', endDate: '2025-12-31', isActive: true },
  ];
  assert.equal(
    comparativeRange({ from: '2025-01-01', to: '2025-03-15' }, 'year', fy2025, 'income', '2024-03-01'),
    null,
  );
  assert.deepEqual(
    comparativeRange({ from: '2025-01-01', to: '2025-03-15' }, 'year', fy2025, 'income', '2024-01-01'),
    { from: '2024-01-01', to: '2024-03-15' },
  );
  // Balance sheet: as-at on books start minus one day is allowed; earlier is not.
  assert.deepEqual(
    comparativeRange({ from: '', to: '2027-03-15' }, 'custom', [
      { fiscalYearCode: 'FY2027', fiscalYearName: 'FY2027', startDate: '2027-01-01', endDate: '2027-12-31', isActive: true },
    ], 'balanceSheet', '2027-01-01'),
    { from: '', to: '2026-12-31' },
  );
  assert.equal(
    comparativeRange({ from: '', to: '2027-03-15' }, 'custom', [
      { fiscalYearCode: 'FY2027', fiscalYearName: 'FY2027', startDate: '2027-01-01', endDate: '2027-12-31', isActive: true },
    ], 'balanceSheet', '2027-01-02'),
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
