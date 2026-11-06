import assert from 'node:assert/strict';
import { after, test } from 'node:test';
import { createServer } from 'vite';

const server = await createServer({ server: { middlewareMode: true, hmr: false, ws: false }, appType: 'custom' });
after(() => server.close());

const {
  recordHistoryUrl,
  activityListUrl,
  securityEventsUrl,
  buildQueryString,
  dateRangeToInstants,
  problemMessage,
  groupHistoryBySave,
  activityRecordLabel,
  personFilterOptions,
  isLineRow,
} = await server.ssrLoadModule('/src/lib/auditFeed.ts');

const row = (overrides) => ({
  id: 1, entityType: 'SalesInvoiceHeader', entityId: 10, action: 'Modified', changes: {},
  actorKind: 'user', actorName: 'Ana', correlationId: 'c1', occurredAt: '2026-11-03T08:00:00', ...overrides,
});

test('record History asks for the record and its children, newest first, with paging', () => {
  assert.equal(
    recordHistoryUrl('SalesInvoiceHeader', 10),
    '/api/organizations/current/activity?entityType=SalesInvoiceHeader&entityId=10&includeChildren=true&limit=50',
  );
  assert.ok(recordHistoryUrl('Bank', 4, 123).endsWith('&cursor=123'));
});

test('activity list and export URLs carry the same filters; export has no paging', () => {
  const filters = { actorUserId: 'u1', entityType: 'Bank' };
  assert.equal(
    activityListUrl(filters, 77),
    '/api/organizations/current/activity?entityType=Bank&actorUserId=u1&limit=50&cursor=77',
  );
  assert.equal(
    activityListUrl(filters, null, true),
    '/api/organizations/current/activity/export?entityType=Bank&actorUserId=u1',
  );
  assert.equal(activityListUrl({}, null), '/api/organizations/current/activity?limit=50');
});

test('security events URL uses type, person and date filters', () => {
  const url = securityEventsUrl({ type: 'auth.signin.failure', actorUserId: 'u1', fromDate: '2026-11-01', toDate: '2026-11-03' }, 9);
  assert.ok(url.startsWith('/api/organizations/current/security-events?type=auth.signin.failure&actorUserId=u1&from='));
  assert.ok(url.includes('&to='));
  assert.ok(url.endsWith('&limit=50&cursor=9'));
});

test('date range covers whole local days', () => {
  const { from, to } = dateRangeToInstants({ fromDate: '2026-11-01', toDate: '2026-11-03' });
  assert.equal(from, new Date(2026, 10, 1, 0, 0, 0, 0).toISOString());
  assert.equal(to, new Date(2026, 10, 3, 23, 59, 59, 999).toISOString());
  assert.deepEqual(dateRangeToInstants({}), { from: undefined, to: undefined });
  assert.deepEqual(dateRangeToInstants({ fromDate: 'nope' }), { from: undefined, to: undefined });
});

test('query strings drop empty values', () => {
  assert.equal(buildQueryString({ a: '', b: null, c: undefined }), '');
  assert.equal(buildQueryString({ a: 'x y', b: 2, c: true }), '?a=x+y&b=2&c=true');
});

test('the server message is shown as is', () => {
  assert.equal(problemMessage({ detail: 'Activity list is not available for this workspace.' }, 'x'), 'Activity list is not available for this workspace.');
  assert.equal(problemMessage({ message: 'Nope' }, 'x'), 'Nope');
  assert.equal(problemMessage({ title: 'Forbidden' }, 'x'), 'Forbidden');
  assert.equal(problemMessage(null, 'fallback'), 'fallback');
  assert.equal(problemMessage({ detail: '  ' }, 'fallback'), 'fallback');
});

test('line changes group under their save, headed "Line {id}", after the record itself', () => {
  const groups = groupHistoryBySave([
    row({ id: 5, entityType: 'SalesInvoiceLine', entityId: 31, parentEntityType: 'SalesInvoiceHeader', parentEntityId: 10 }),
    row({ id: 4, entityType: 'SalesInvoiceLine', entityId: 30, parentEntityType: 'SalesInvoiceHeader', parentEntityId: 10 }),
    row({ id: 3 }),
    row({ id: 2, correlationId: 'c0', occurredAt: '2026-11-02T08:00:00' }),
  ]);
  assert.equal(groups.length, 2);
  assert.deepEqual(groups[0].parts.map((part) => part.heading), [null, 'Line 30', 'Line 31']);
  assert.deepEqual(groups[0].parts[0].rows.map((item) => item.id), [3]);
  assert.deepEqual(groups[1].parts.map((part) => part.heading), [null]);
});

test('rows without a correlation id each stand alone, and groups never contain empty parts', () => {
  const groups = groupHistoryBySave([
    row({ id: 2, correlationId: '' }),
    row({ id: 1, correlationId: null }),
    row({ id: 0, correlationId: 'c9', entityType: 'SalesInvoiceLine', entityId: 7, parentEntityType: 'SalesInvoiceHeader', parentEntityId: 10 }),
  ]);
  assert.equal(groups.length, 3);
  assert.deepEqual(groups[2].parts.map((part) => part.heading), ['Line 7']);
});

test('rows from a later page merge into the same save', () => {
  const firstPage = [row({ id: 9 })];
  const secondPage = [row({ id: 8, entityType: 'SalesInvoiceLine', entityId: 2, parentEntityType: 'SalesInvoiceHeader', parentEntityId: 10 })];
  const groups = groupHistoryBySave([...firstPage, ...secondPage]);
  assert.equal(groups.length, 1);
  assert.deepEqual(groups[0].parts.map((part) => part.heading), [null, 'Line 2']);
});

test('line detection uses the parent columns or the line record types', () => {
  assert.equal(isLineRow({ entityType: 'SalesInvoiceHeader', parentEntityType: null }), false);
  assert.equal(isLineRow({ entityType: 'SalesInvoiceLine', parentEntityType: null }), true);
  assert.equal(isLineRow({ entityType: 'Whatever', parentEntityType: 'SalesInvoiceHeader' }), true);
});

test('activity record labels name the document for a line', () => {
  assert.equal(activityRecordLabel(row({}), false), 'Invoice #10');
  assert.equal(
    activityRecordLabel(row({ entityType: 'JournalEntryLine', entityId: 3, parentEntityType: 'JournalEntryHeader', parentEntityId: 8 }), true),
    'Transaction #8 · Line 3',
  );
  assert.equal(activityRecordLabel(row({ entityType: 'SalesInvoiceLine', entityId: 3 }), false), 'Invoice line #3');
});

test('person filter options use full names, falling back to email', () => {
  assert.deepEqual(
    personFilterOptions([
      { userId: 'u1', email: 'a@x.test', firstName: 'Ana', lastName: 'Cruz' },
      { userId: 'u2', email: 'b@x.test', firstName: '', lastName: '' },
      { userId: 'u3' },
    ]),
    [
      { value: 'u1', label: 'Ana Cruz' },
      { value: 'u2', label: 'b@x.test' },
      { value: 'u3', label: 'u3' },
    ],
  );
});
