// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

import assert from 'node:assert/strict';
import test, { beforeEach } from 'node:test';
import {
  dismissWriteFailedNotice,
  getServiceStatus,
  isTakingLong,
  nextProbeDelayMs,
  parseServiceProblem,
  reportNetworkFailure,
  reportServiceProblem,
  reportServiceRestored,
  resetServiceStatusForTesting,
  setServiceStatusCollapsed,
  subscribeServiceStatus,
} from '../src/lib/serviceStatus.ts';

beforeEach(() => {
  resetServiceStatusForTesting();
});

test('problem parsing: valid 503 problem json', () => {
  const result = parseServiceProblem(503, 'application/problem+json', {
    code: 'SERVICE_UPDATING',
    retryAfterSeconds: 8,
    detail: 'Installing update',
    until: '2026-11-09T14:00:00Z',
  });
  assert.deepEqual(result, {
    code: 'SERVICE_UPDATING',
    retryAfterSeconds: 8,
    message: 'Installing update',
    until: '2026-11-09T14:00:00Z',
  });
});

test('problem parsing: message property prioritized over detail', () => {
  const result = parseServiceProblem(503, 'application/json', {
    code: 'SERVICE_MAINTENANCE',
    message: 'Custom operator message',
    detail: 'Fallback detail',
  });
  assert.equal(result?.message, 'Custom operator message');
});

test('problem parsing: non-503 status returns null', () => {
  assert.equal(parseServiceProblem(200, 'application/json', { code: 'SERVICE_UPDATING' }), null);
  assert.equal(parseServiceProblem(404, 'application/json', { code: 'SERVICE_UPDATING' }), null);
  assert.equal(parseServiceProblem(500, 'application/json', { code: 'SERVICE_UPDATING' }), null);
});

test('problem parsing: unknown code returns null', () => {
  assert.equal(parseServiceProblem(503, 'application/json', { code: 'UNKNOWN_CODE' }), null);
  assert.equal(parseServiceProblem(503, 'application/json', { code: 123 }), null);
  assert.equal(parseServiceProblem(503, 'application/json', {}), null);
});

test('problem parsing: non-json content-type returns null', () => {
  assert.equal(
    parseServiceProblem(503, 'text/html', { code: 'SERVICE_STARTING' }),
    null
  );
});

test('problem parsing: bad or empty JSON body returns null', () => {
  assert.equal(parseServiceProblem(503, 'application/json', null), null);
  assert.equal(parseServiceProblem(503, 'application/json', 'raw string'), null);
});

test('problem parsing: invalid until date dropped', () => {
  const result = parseServiceProblem(503, 'application/json', {
    code: 'SERVICE_MAINTENANCE',
    until: 'not-a-valid-date',
  });
  assert.equal(result?.until, undefined);
});

test('GET vs write trigger rules: GET does not set writeFailed', () => {
  reportServiceProblem({ code: 'SERVICE_STARTING' }, { method: 'GET' });
  const status = getServiceStatus();
  assert.equal(status.state, 'unavailable');
  assert.equal(status.code, 'SERVICE_STARTING');
  assert.equal(status.writeFailed, false);
});

test('GET vs write trigger rules: write immediately sets writeFailed', () => {
  reportServiceProblem({ code: 'SERVICE_UPDATING' }, { method: 'POST' });
  const status = getServiceStatus();
  assert.equal(status.state, 'unavailable');
  assert.equal(status.code, 'SERVICE_UPDATING');
  assert.equal(status.writeFailed, true);
});

test('the 2-failures-in-15s rule: single network failure does not trigger unavailable', () => {
  reportNetworkFailure({ method: 'GET', now: 1000 });
  assert.equal(getServiceStatus().state, 'ok');
});

test('the 2-failures-in-15s rule: two failures within 15s trigger SERVICE_UNREACHABLE', () => {
  reportNetworkFailure({ method: 'GET', now: 1000 });
  assert.equal(getServiceStatus().state, 'ok');

  reportNetworkFailure({ method: 'GET', now: 12000 }); // 11s later (within 15s)
  const status = getServiceStatus();
  assert.equal(status.state, 'unavailable');
  assert.equal(status.code, 'SERVICE_UNREACHABLE');
});

test('the 2-failures-in-15s rule: failures older than 15s do not trigger', () => {
  reportNetworkFailure({ method: 'GET', now: 1000 });
  assert.equal(getServiceStatus().state, 'ok');

  // Second failure 16s later
  reportNetworkFailure({ method: 'GET', now: 17001 });
  assert.equal(getServiceStatus().state, 'ok');

  // Third failure 2s after second -> triggers!
  reportNetworkFailure({ method: 'GET', now: 19000 });
  assert.equal(getServiceStatus().state, 'unavailable');
});

test('write failure during network errors sets writeFailed flag', () => {
  reportNetworkFailure({ method: 'POST', now: 1000 });
  assert.equal(getServiceStatus().state, 'ok');

  reportNetworkFailure({ method: 'GET', now: 5000 });
  const status = getServiceStatus();
  assert.equal(status.state, 'unavailable');
  assert.equal(status.writeFailed, true);
});

test('offline suppression: does not report when navigator.onLine is false', () => {
  reportServiceProblem(
    { code: 'SERVICE_STARTING' },
    { method: 'GET', isOnline: false }
  );
  assert.equal(getServiceStatus().state, 'ok');

  reportNetworkFailure({ method: 'POST', isOnline: false, now: 1000 });
  reportNetworkFailure({ method: 'POST', isOnline: false, now: 2000 });
  assert.equal(getServiceStatus().state, 'ok');
});

test('writeFailed persists after service is restored and can be dismissed', () => {
  reportServiceProblem({ code: 'SERVICE_INTERRUPTED' }, { method: 'PUT' });
  assert.equal(getServiceStatus().state, 'unavailable');
  assert.equal(getServiceStatus().writeFailed, true);

  reportServiceRestored();
  assert.equal(getServiceStatus().state, 'ok');
  assert.equal(getServiceStatus().writeFailed, true); // Still set for post-recovery notice

  dismissWriteFailedNotice();
  assert.equal(getServiceStatus().writeFailed, false);
});

test('probe backoff table: nextProbeDelayMs', () => {
  // First 2 minutes (< 120_000 ms): clamped to 3-10 s
  assert.equal(nextProbeDelayMs(0, 1), 3000); // 1s clamped up to 3s
  assert.equal(nextProbeDelayMs(30_000, 5), 5000); // 5s matches
  assert.equal(nextProbeDelayMs(60_000, 12), 10000); // 12s clamped down to 10s
  assert.equal(nextProbeDelayMs(119_999, undefined), 5000); // default 5s

  // Between 2 min and 5 min (120_000 to 299_999 ms): 15s
  assert.equal(nextProbeDelayMs(120_000, 5), 15000);
  assert.equal(nextProbeDelayMs(180_000, 3), 15000);
  assert.equal(nextProbeDelayMs(299_999, 10), 15000);

  // After 5 min (>= 300_000 ms): 30s
  assert.equal(nextProbeDelayMs(300_000, 5), 30000);
  assert.equal(nextProbeDelayMs(600_000, 5), 30000);
});

test('isTakingLong returns true after 10 minutes', () => {
  assert.equal(isTakingLong(0), false);
  assert.equal(isTakingLong(9 * 60_000 + 59_000), false);
  assert.equal(isTakingLong(10 * 60_000), true);
  assert.equal(isTakingLong(12 * 60_000), true);
});

test('subscribers receive updates on state changes', () => {
  let callCount = 0;
  let lastState = null;
  const unsubscribe = subscribeServiceStatus((s) => {
    callCount++;
    lastState = s;
  });

  assert.equal(callCount, 1);
  assert.equal(lastState.state, 'ok');

  setServiceStatusCollapsed(true);
  assert.equal(callCount, 2);
  assert.equal(lastState.collapsed, true);

  unsubscribe();
  setServiceStatusCollapsed(false);
  assert.equal(callCount, 2); // No new calls after unsubscribe
});
