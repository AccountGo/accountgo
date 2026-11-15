import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as recurring from '../src/lib/recurring.ts';

test('legacy numeric responses normalize before status and permission decisions', () => {
  const s = recurring.normalizeRecurringSummary({ status: 1, cadence: 2, action: 2, isOverdue: true });
  assert.equal(s.status, 'Active');
  assert.equal(recurring.cadenceLabel(s.cadence, 1), 'Every month');
  assert.equal(s.action, 'AutoIssue');
  assert.equal(recurring.canRunDueSchedule(s, true, false), false);
  assert.equal(recurring.canRunDueSchedule({ ...s, isOverdue: true }, true, true), true);
  assert.equal(recurring.canRunDueSchedule({ ...s, isOverdue: true }, false, true), false);
  assert.equal(recurring.canRunDueSchedule({ ...s, status: 'Paused', isOverdue: true }, true, true), false);
});
test('detail and history normalize enums including zero-valued discounts', () => {
  const s = recurring.normalizeRecurringDetail({ status: 2, cadence: 3, action: 1, servicePeriodMode: 2, lines: [{ discountType: 0 }, { discountType: 1 }] });
  assert.equal(s.status, 'Paused');
  assert.equal(s.servicePeriodMode, 'PreviousMonth');
  assert.deepEqual(s.lines.map(l => l.discountType), ['Percentage', 'Amount']);
  assert.equal(recurring.normalizeRecurringOccurrence({ status: 3 }).status, 'DraftCreated');
  assert.equal(recurring.normalizeRecurringSummary({ status: 'Active', cadence: 'Monthly', action: 'AutoIssue' }).action, 'AutoIssue');
  assert.throws(() => recurring.normalizeRecurringSummary({ status: 99 }), /Unknown recurring/);
});
