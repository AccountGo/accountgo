import assert from 'node:assert/strict';
import test from 'node:test';
import { mergeUntouched, mergeUntouchedLines, ruleHintFor } from '../src/components/document-capture/fields.ts';

const line = (id, description, unitAmount = '10.00') => ({
  id,
  description,
  quantity: '1',
  unitAmount,
  sourceLineTotal: '',
  taxHint: '',
  discount: '',
  priceBasis: '',
  missingQuantityDefaulted: false,
});

test('untouched header fields take the improved value and touched ones keep what the user typed', () => {
  const baseline = { vendorName: 'Northstar', vendorInvoiceNo: '', date: '2026-11-12' };
  const current = { vendorName: 'Northstar', vendorInvoiceNo: 'MY-1', date: '2026-11-12' };
  const next = { vendorName: 'Northstar Supplies Ltd', vendorInvoiceNo: 'NS-20457', date: '2026-11-12' };

  assert.deepEqual(mergeUntouched(current, baseline, next), {
    vendorName: 'Northstar Supplies Ltd',
    vendorInvoiceNo: 'MY-1',
    date: '2026-11-12',
  });
});

test('a header field the user cleared counts as touched', () => {
  const merged = mergeUntouched({ vendorName: '' }, { vendorName: 'Northstar' }, { vendorName: 'Northstar Supplies Ltd' });
  assert.equal(merged.vendorName, '');
});

test('lines nobody edited are replaced by the improved lines', () => {
  const baseline = [line(1, 'Paper'), line(2, 'Toner')];
  const next = [line(7, 'A4 copier paper'), line(8, 'Toner cartridge')];
  assert.deepEqual(mergeUntouchedLines(baseline.map((l) => ({ ...l })), baseline, next), next);
});

test('once any line was edited only the untouched fields of an unchanged row count are improved', () => {
  const baseline = [line(1, 'Paper'), line(2, 'Toner')];
  const current = [line(1, 'Paper', '12.50'), line(2, 'Toner')];
  const next = [line(7, 'A4 copier paper', '10.00'), line(8, 'Toner cartridge', '10.00')];

  const merged = mergeUntouchedLines(current, baseline, next);

  assert.equal(merged[0].description, 'A4 copier paper');
  assert.equal(merged[0].unitAmount, '12.50');
  assert.equal(merged[1].description, 'Toner cartridge');
  assert.equal(merged[0].id, 1);
});

test('edited lines are left alone when the row count changed', () => {
  const baseline = [line(1, 'Paper'), line(2, 'Toner')];
  const current = [line(1, 'Paper'), line(2, 'Toner'), line(3, 'Added by me')];
  const next = [line(7, 'A4 copier paper'), line(8, 'Toner cartridge')];
  assert.deepEqual(mergeUntouchedLines(current, baseline, next), current);
  assert.deepEqual(mergeUntouchedLines([line(1, 'Paper')], baseline, next), [line(1, 'Paper')]);
});

test('a rule hint is offered only for a finished mapping and only when it differs from the form', () => {
  const done = { status: 'done', ruleHints: { vendorName: 'Northstar', 'lines[0].description': 'Paper' } };
  assert.equal(ruleHintFor(done, 'vendorName', 'Northstar Supplies Ltd'), 'Northstar');
  assert.equal(ruleHintFor(done, 'vendorName', 'Northstar'), null);
  assert.equal(ruleHintFor(done, 'date', '2026-11-12'), null);
  assert.equal(ruleHintFor({ status: 'pending', ruleHints: done.ruleHints }, 'vendorName', 'x'), null);
  assert.equal(ruleHintFor({ status: 'failed', ruleHints: {} }, 'vendorName', 'x'), null);
  assert.equal(ruleHintFor(null, 'vendorName', 'x'), null);
  assert.equal(ruleHintFor(undefined, 'vendorName', 'x'), null);
});
