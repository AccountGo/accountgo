import test from 'node:test';
import assert from 'node:assert/strict';
import {
  canFinishReconciliation,
  filterVisibleLines,
  lineMatchesSearch,
  nextFocusIndex,
  tickTogglePayload,
} from '../src/lib/reconcileUi.ts';

test('nextFocusIndex wraps at both ends', () => {
  assert.equal(nextFocusIndex(5, 4, 1), 0);
  assert.equal(nextFocusIndex(5, 0, -1), 4);
  assert.equal(nextFocusIndex(5, 2, 1), 3);
});

test('tickTogglePayload adds or removes a single line', () => {
  assert.deepEqual(tickTogglePayload(new Set([1, 2]), 3), { add: [3], remove: [] });
  assert.deepEqual(tickTogglePayload(new Set([1, 2]), 2), { add: [], remove: [2] });
});

test('lineMatchesSearch matches amount and text', () => {
  const line = {
    lineId: 1,
    in: 12.5,
    out: 0,
    ticked: false,
    type: 'Receipt',
    reference: 'INV-9',
    payee: 'Acme',
  };
  assert.equal(lineMatchesSearch(line, '12.50'), true);
  assert.equal(lineMatchesSearch(line, 'acme'), true);
  assert.equal(lineMatchesSearch(line, 'inv-9'), true);
  assert.equal(lineMatchesSearch(line, 'zzzz'), false);
});

test('filterVisibleLines hides ticked when requested', () => {
  const lines = [
    { lineId: 1, in: 10, out: 0, ticked: true, type: 'A' },
    { lineId: 2, in: 0, out: 5, ticked: false, type: 'B' },
  ];
  assert.deepEqual(
    filterVisibleLines(lines, '', true).map((l) => l.lineId),
    [2],
  );
});

test('canFinishReconciliation requires an exact zero difference', () => {
  assert.equal(canFinishReconciliation(0), true);
  assert.equal(canFinishReconciliation(0.0), true);
  assert.equal(canFinishReconciliation(0.01), false);
  assert.equal(canFinishReconciliation(-0.01), false);
});

test('keyboard flow: arrow then space targets the next unticked line id', () => {
  const lines = [
    { lineId: 10, in: 1, out: 0, ticked: false, type: 'Deposit' },
    { lineId: 11, in: 2, out: 0, ticked: false, type: 'Deposit' },
    { lineId: 12, in: 0, out: 3, ticked: true, type: 'Payment' },
  ];
  const visible = filterVisibleLines(lines, '', true);
  assert.deepEqual(
    visible.map((l) => l.lineId),
    [10, 11],
  );
  const focused = nextFocusIndex(visible.length, 0, 1);
  assert.equal(visible[focused].lineId, 11);
  assert.deepEqual(tickTogglePayload(new Set(), visible[focused].lineId), { add: [11], remove: [] });
});
