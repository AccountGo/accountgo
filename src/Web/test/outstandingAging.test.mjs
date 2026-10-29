import test from 'node:test';
import assert from 'node:assert/strict';
import { rowsWithOutstanding, staleMessage, staleTotal } from '../src/lib/outstandingAging.ts';

const row = (id, totalCount, staleCount) => ({
  account: { id },
  aging: { totalCount, staleCount },
});

test('rowsWithOutstanding drops accounts with nothing outstanding', () => {
  const rows = [row(1, 0, 0), row(2, 3, 1)];
  assert.deepEqual(rowsWithOutstanding(rows).map((r) => r.account.id), [2]);
});

test('staleTotal sums the 90+ counts', () => {
  assert.equal(staleTotal([row(1, 2, 1), row(2, 5, 4)]), 5);
  assert.equal(staleTotal([]), 0);
});

test('staleMessage is empty with no stale items and pluralises otherwise', () => {
  assert.equal(staleMessage(0), '');
  assert.match(staleMessage(1), /^1 item is outstanding/);
  assert.match(staleMessage(3), /^3 items are outstanding/);
});
