import assert from 'node:assert/strict';
import test from 'node:test';
import {
  dismissalStorageKey,
  filterVisibleAnnouncements,
  hasServerBillingAnnouncement,
  mergeAnnouncementItems,
  mobileCollapse,
  readDismissedIds,
  sortAnnouncements,
  writeDismissal,
} from '../src/lib/announcementBarLogic.ts';

const items = [
  { id: 'i', severity: 'info', message: 'Info', dismissible: true },
  { id: 'c', severity: 'critical', message: 'Crit', dismissible: false },
  { id: 'w', severity: 'warning', message: 'Warn', dismissible: false },
];

test('announcements sort critical → warning → info', () => {
  assert.deepEqual(sortAnnouncements(items).map((a) => a.id), ['c', 'w', 'i']);
});

test('filterVisibleAnnouncements drops dismissed and expired items', () => {
  const now = Date.parse('2026-11-05T12:00:00Z');
  const list = filterVisibleAnnouncements(
    [
      ...items,
      { id: 'gone', severity: 'critical', message: 'x', dismissible: false, expiresAt: '2026-11-05T11:00:00Z' },
    ],
    new Set(['w']),
    now,
  );
  assert.deepEqual(list.map((a) => a.id), ['c', 'i']);
});

test('mobileCollapse shows first item and remaining count', () => {
  assert.deepEqual(mobileCollapse([]), { first: null, moreCount: 0 });
  const collapsed = mobileCollapse(sortAnnouncements(items));
  assert.equal(collapsed.first?.id, 'c');
  assert.equal(collapsed.moreCount, 2);
});

test('dismissal persistence survives storage that throws', () => {
  const throwing = {
    getItem() { throw new Error('blocked'); },
    setItem() { throw new Error('blocked'); },
  };
  assert.equal(readDismissedIds(throwing, 'u1', ['a']).size, 0);
  assert.doesNotThrow(() => writeDismissal(throwing, 'u1', 'a'));

  const memory = new Map();
  const storage = {
    getItem: (k) => (memory.has(k) ? memory.get(k) : null),
    setItem: (k, v) => { memory.set(k, String(v)); },
  };
  writeDismissal(storage, 'u1', 'a');
  assert.equal(storage.getItem(dismissalStorageKey('u1', 'a')), '1');
  assert.ok(readDismissedIds(storage, 'u1', ['a', 'b']).has('a'));
  assert.equal(readDismissedIds(storage, 'u1', ['a', 'b']).has('b'), false);
});

test('mergeAnnouncementItems skips client past-due when server sends billing: notices', () => {
  const clientPastDue = {
    id: 'client:past-due',
    severity: 'warning',
    message: 'Payment past due.',
    dismissible: false,
    clientOnly: true,
  };
  const withoutBilling = mergeAnnouncementItems(
    [{ id: 'info-1', severity: 'info', message: 'Hello', dismissible: true }],
    clientPastDue,
  );
  assert.deepEqual(withoutBilling.map((a) => a.id), ['info-1', 'client:past-due']);
  assert.equal(hasServerBillingAnnouncement(withoutBilling), false);

  const withBilling = mergeAnnouncementItems(
    [{ id: 'billing:past-due:42', severity: 'warning', message: 'Update by Nov 5, 2026', dismissible: false }],
    clientPastDue,
  );
  assert.deepEqual(withBilling.map((a) => a.id), ['billing:past-due:42']);
  assert.equal(hasServerBillingAnnouncement(withBilling), true);

  assert.deepEqual(mergeAnnouncementItems([], clientPastDue).map((a) => a.id), ['client:past-due']);
  assert.deepEqual(mergeAnnouncementItems([], null), []);
});
