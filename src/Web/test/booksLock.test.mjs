import test from 'node:test';
import assert from 'node:assert/strict';
import { formatBooksLockDate, isBooksLockReopen } from '../src/lib/booksLock.ts';

test('isBooksLockReopen is false when locking later without a previous lock', () => {
  assert.equal(isBooksLockReopen(null, '2026-12-31'), false);
  assert.equal(isBooksLockReopen(undefined, '2026-12-31'), false);
});

test('isBooksLockReopen is false when moving the lock later', () => {
  assert.equal(isBooksLockReopen('2026-06-30', '2026-12-31'), false);
});

test('isBooksLockReopen is true when moving the lock earlier', () => {
  assert.equal(isBooksLockReopen('2026-12-31', '2026-06-30'), true);
});

test('isBooksLockReopen is true when clearing the lock', () => {
  assert.equal(isBooksLockReopen('2026-12-31', null), true);
  assert.equal(isBooksLockReopen('2026-12-31', ''), true);
});

test('isBooksLockReopen is false when already unlocked and staying unlocked', () => {
  assert.equal(isBooksLockReopen(null, null), false);
  assert.equal(isBooksLockReopen(null, ''), false);
});

test('formatBooksLockDate shows unlocked for empty values', () => {
  assert.equal(formatBooksLockDate(null), 'unlocked');
  assert.equal(formatBooksLockDate(''), 'unlocked');
  assert.equal(formatBooksLockDate('2026-12-31'), '2026-12-31');
});
