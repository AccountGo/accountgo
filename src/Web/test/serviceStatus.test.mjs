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
  shouldRetry503,
  shouldSuppressOverlay,
  startServiceStatusProbe,
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

test('userWrite flag: only userWrite: true sets writeFailed', () => {
  // Non-GET without userWrite (e.g. performRefresh, sendOtp) does NOT set writeFailed
  reportServiceProblem({ code: 'SERVICE_STARTING' }, { method: 'POST' });
  let status = getServiceStatus();
  assert.equal(status.state, 'unavailable');
  assert.equal(status.writeFailed, false);

  resetServiceStatusForTesting();

  // User write (e.g. authFetch non-GET outside /api/auth/) sets writeFailed
  reportServiceProblem({ code: 'SERVICE_UPDATING' }, { method: 'POST', userWrite: true });
  status = getServiceStatus();
  assert.equal(status.state, 'unavailable');
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

test('network failure with userWrite sets writeFailed flag', () => {
  // Without userWrite: writeFailed is false
  reportNetworkFailure({ method: 'POST', now: 1000 });
  reportNetworkFailure({ method: 'POST', now: 5000 });
  assert.equal(getServiceStatus().state, 'unavailable');
  assert.equal(getServiceStatus().writeFailed, false);

  resetServiceStatusForTesting();

  // With userWrite: writeFailed is true
  reportNetworkFailure({ method: 'POST', userWrite: true, now: 1000 });
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

  reportNetworkFailure({ method: 'POST', userWrite: true, isOnline: false, now: 1000 });
  reportNetworkFailure({ method: 'POST', userWrite: true, isOnline: false, now: 2000 });
  assert.equal(getServiceStatus().state, 'ok');
});

test('writeFailed persists after service is restored and can be dismissed', () => {
  reportServiceProblem({ code: 'SERVICE_INTERRUPTED' }, { method: 'PUT', userWrite: true });
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

test('shouldRetry503: GET retries on any 503; non-GET retries only on safe startup/deploy codes', () => {
  // GET always retries
  assert.equal(shouldRetry503('GET', { code: 'SERVICE_STARTING' }), true);
  assert.equal(shouldRetry503('GET', { code: 'SERVICE_INTERRUPTED' }), true);
  assert.equal(shouldRetry503('GET', null), true);
  assert.equal(shouldRetry503('HEAD', null), true);

  // Safe non-GET retry codes (request never reached app)
  assert.equal(shouldRetry503('POST', { code: 'SERVICE_STARTING' }), true);
  assert.equal(shouldRetry503('POST', { code: 'SERVICE_UPDATING' }), true);
  assert.equal(shouldRetry503('PUT', { code: 'SERVICE_MAINTENANCE' }), true);
  assert.equal(shouldRetry503('DELETE', { code: 'SERVICE_MISCONFIGURED' }), true);

  // Unsafe non-GET codes: NEVER retry
  assert.equal(shouldRetry503('POST', { code: 'SERVICE_INTERRUPTED' }), false);
  assert.equal(shouldRetry503('POST', { code: 'SERVICE_UNREACHABLE' }), false);
  assert.equal(shouldRetry503('POST', null), false);
});

test('startServiceStatusProbe controller: three 503s keep scheduling, backoff moves 15s then 30s, 200 stops', async () => {
  let currentTime = 1000;
  let scheduledTasks = [];
  let timerCounter = 1;

  const fakeSetTimeout = (fn, ms) => {
    const id = timerCounter++;
    scheduledTasks.push({ id, fn, dueAt: currentTime + ms, delay: ms });
    return id;
  };

  const fakeClearTimeout = (id) => {
    scheduledTasks = scheduledTasks.filter((t) => t.id !== id);
  };

  const triggerNext = async (advanceTo) => {
    scheduledTasks.sort((a, b) => a.dueAt - b.dueAt);
    const task = scheduledTasks.shift();
    if (!task) return null;
    currentTime = advanceTo !== undefined ? advanceTo : task.dueAt;
    await task.fn();
    return task;
  };

  // Start in unavailable state
  reportServiceProblem({ code: 'SERVICE_STARTING', retryAfterSeconds: 5 }, { now: currentTime });

  let fetchCount = 0;
  let probeStatusToReturn = 503;
  const mockFetch = async () => {
    fetchCount++;
    if (probeStatusToReturn === 200) {
      return new Response(JSON.stringify({ status: 'ok', version: 'abc' }), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      });
    }
    return new Response(
      JSON.stringify({
        title: 'Service Unavailable',
        status: 503,
        code: 'SERVICE_STARTING',
        retryAfterSeconds: 5,
      }),
      {
        status: 503,
        headers: {
          'content-type': 'application/problem+json',
          'Retry-After': '5',
        },
      }
    );
  };

  const controller = startServiceStatusProbe({
    fetchImpl: mockFetch,
    setTimeout: fakeSetTimeout,
    clearTimeout: fakeClearTimeout,
    now: () => currentTime,
  });

  // Initial schedule was created: 5s delay
  assert.equal(scheduledTasks.length, 1);
  assert.equal(scheduledTasks[0].delay, 5000);

  // Check 1: fires 503 -> schedules next check in 5s
  const task1 = await triggerNext();
  assert.equal(task1.delay, 5000);
  assert.equal(fetchCount, 1);
  assert.equal(getServiceStatus().state, 'unavailable');
  assert.equal(scheduledTasks.length, 1);
  assert.equal(scheduledTasks[0].delay, 5000);

  // Check 2: fires 503 -> schedules next check in 5s
  const task2 = await triggerNext();
  assert.equal(task2.delay, 5000);
  assert.equal(fetchCount, 2);
  assert.equal(getServiceStatus().state, 'unavailable');
  assert.equal(scheduledTasks.length, 1);
  assert.equal(scheduledTasks[0].delay, 5000);

  // Check 3: fires 503 -> three consecutive 503 checks keep scheduling!
  const task3 = await triggerNext();
  assert.equal(task3.delay, 5000);
  assert.equal(fetchCount, 3);
  assert.equal(getServiceStatus().state, 'unavailable');

  // Advance clock past 2 minutes (120_000ms): delay moves to 15s
  const task4 = await triggerNext(1000 + 125_000);
  assert.equal(fetchCount, 4);
  assert.equal(scheduledTasks.length, 1);
  assert.equal(scheduledTasks[0].delay, 15000);

  // Advance clock past 5 minutes (300_000ms): delay moves to 30s
  const task5 = await triggerNext(1000 + 305_000);
  assert.equal(fetchCount, 5);
  assert.equal(scheduledTasks.length, 1);
  assert.equal(scheduledTasks[0].delay, 30000);

  // Next check returns 200 -> stops probe loop
  probeStatusToReturn = 200;
  await triggerNext();
  assert.equal(fetchCount, 6);
  assert.equal(getServiceStatus().state, 'ok');
  assert.equal(scheduledTasks.length, 0); // No more tasks scheduled

  controller.stop();
});

test('startServiceStatusProbe controller: 4xx counts as restored, 5xx without problem body counts as failure', async () => {
  let currentTime = 1000;
  let scheduledTasks = [];
  let timerCounter = 1;

  const fakeSetTimeout = (fn, ms) => {
    const id = timerCounter++;
    scheduledTasks.push({ id, fn, dueAt: currentTime + ms, delay: ms });
    return id;
  };
  const fakeClearTimeout = (id) => {
    scheduledTasks = scheduledTasks.filter((t) => t.id !== id);
  };
  const triggerNext = async () => {
    scheduledTasks.sort((a, b) => a.dueAt - b.dueAt);
    const task = scheduledTasks.shift();
    if (!task) return null;
    currentTime = task.dueAt;
    await task.fn();
    return task;
  };

  // 1. Test that 4xx counts as restored
  reportServiceProblem({ code: 'SERVICE_STARTING' }, { now: currentTime });
  assert.equal(getServiceStatus().state, 'unavailable');

  let controller = startServiceStatusProbe({
    fetchImpl: async () => new Response('Not Found', { status: 404 }),
    setTimeout: fakeSetTimeout,
    clearTimeout: fakeClearTimeout,
    now: () => currentTime,
  });

  await triggerNext();
  assert.equal(getServiceStatus().state, 'ok');
  assert.equal(scheduledTasks.length, 0);
  controller.stop();

  // 2. Test that 5xx without known problem body counts as failure
  reportServiceProblem({ code: 'SERVICE_STARTING' }, { now: currentTime });
  assert.equal(getServiceStatus().state, 'unavailable');

  controller = startServiceStatusProbe({
    fetchImpl: async () => new Response('Internal Server Error', { status: 500 }),
    setTimeout: fakeSetTimeout,
    clearTimeout: fakeClearTimeout,
    now: () => currentTime,
  });

  await triggerNext();
  assert.equal(getServiceStatus().state, 'unavailable');
  assert.equal(scheduledTasks.length, 1); // Keeps scheduling
  controller.stop();
});

test('shouldSuppressOverlay: boot (true, true) suppressed; mid-session refresh (false, true) shows overlay', () => {
  // Boot: isLoading && isStarting -> suppressed (boot card handles it)
  assert.equal(shouldSuppressOverlay({ isLoading: true, isStarting: true }), true);

  // Mid-session refresh during an outage: isLoading: false, isStarting: true -> overlay shows (not suppressed)
  assert.equal(shouldSuppressOverlay({ isLoading: false, isStarting: true }), false);

  // Loaded and running: normal operations -> not suppressed
  assert.equal(shouldSuppressOverlay({ isLoading: false, isStarting: false }), false);

  // Loading without starting flag -> not suppressed
  assert.equal(shouldSuppressOverlay({ isLoading: true, isStarting: false }), false);
});

