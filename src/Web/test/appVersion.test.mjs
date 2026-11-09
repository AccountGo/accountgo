// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

import assert from 'node:assert/strict';
import test, { beforeEach } from 'node:test';
import {
  POLL_INTERVAL_MS,
  deferUpdate,
  getDeferredUpdate,
  isNewerDeploy,
  parseVersionJson,
  readCurrentBuildId,
  resetDeferredUpdateForTesting,
  shouldOpenUpdateDialog,
  startVersionWatch,
} from '../src/lib/appVersion.ts';

beforeEach(() => {
  resetDeferredUpdateForTesting();
});

test('readCurrentBuildId: returns string when undeclared in node', () => {
  assert.equal(typeof readCurrentBuildId(), 'string');
});

test('parseVersionJson: valid, missing, empty, non-string, non-object', () => {
  assert.equal(parseVersionJson({ buildId: 'abc-123' }), 'abc-123');
  assert.equal(parseVersionJson({ buildId: '  trimmed-id  ' }), 'trimmed-id');
  assert.equal(parseVersionJson({ buildId: '' }), null);
  assert.equal(parseVersionJson({ buildId: '   ' }), null);
  assert.equal(parseVersionJson({}), null);
  assert.equal(parseVersionJson({ buildId: 12345 }), null);
  assert.equal(parseVersionJson(null), null);
  assert.equal(parseVersionJson(undefined), null);
  assert.equal(parseVersionJson('not-an-object'), null);
  assert.equal(parseVersionJson([]), null);
});

test('isNewerDeploy: table of comparisons', () => {
  assert.equal(isNewerDeploy('v2', 'v1'), true);
  assert.equal(isNewerDeploy('v1', 'v1'), false);
  assert.equal(isNewerDeploy('', 'v1'), false);
  assert.equal(isNewerDeploy('v2', ''), false);
  assert.equal(isNewerDeploy('', ''), false);
  assert.equal(isNewerDeploy('   ', 'v1'), false);
  assert.equal(isNewerDeploy('v1', '   '), false);
  assert.equal(isNewerDeploy('  v1  ', 'v1'), false);
});

test('startVersionWatch: same id → no update; new id → onUpdate once; same new id again → no repeat; newer id later → second onUpdate', async () => {
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

  let deployedId = 'build-1';
  const updates = [];

  const watch = startVersionWatch({
    fetchImpl: async () => {
      return new Response(JSON.stringify({ buildId: deployedId }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    },
    setTimeout: fakeSetTimeout,
    clearTimeout: fakeClearTimeout,
    now: () => currentTime,
    currentBuildId: 'build-1',
    onUpdate: (id) => updates.push(id),
  });

  // Initial schedule should be set for POLL_INTERVAL_MS (60s)
  assert.equal(scheduledTasks.length, 1);
  assert.equal(scheduledTasks[0].delay, POLL_INTERVAL_MS);

  // Poll 1: deployed is still build-1 -> no update
  await triggerNext();
  assert.deepEqual(updates, []);
  assert.equal(scheduledTasks.length, 1);

  // Deployed updates to build-2
  deployedId = 'build-2';
  await triggerNext();
  assert.deepEqual(updates, ['build-2']);

  // Poll 3: deployed is still build-2 -> no repeat
  await triggerNext();
  assert.deepEqual(updates, ['build-2']);

  // Deployed updates to build-3 -> second onUpdate
  deployedId = 'build-3';
  await triggerNext();
  assert.deepEqual(updates, ['build-2', 'build-3']);

  watch.stop();
  assert.equal(scheduledTasks.length, 0);
});

test('startVersionWatch: hidden tab → no polling; visible → immediate check and resumes', async () => {
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

  const docListeners = new Map();
  const fakeDoc = {
    visibilityState: 'hidden',
    addEventListener: (type, fn) => {
      if (!docListeners.has(type)) docListeners.set(type, new Set());
      docListeners.get(type).add(fn);
    },
    removeEventListener: (type, fn) => {
      docListeners.get(type)?.delete(fn);
    },
    trigger: (type) => {
      for (const fn of docListeners.get(type) || []) fn();
    },
  };

  let fetchCount = 0;
  const updates = [];

  const watch = startVersionWatch({
    fetchImpl: async () => {
      fetchCount++;
      return new Response(JSON.stringify({ buildId: 'build-2' }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    },
    setTimeout: fakeSetTimeout,
    clearTimeout: fakeClearTimeout,
    now: () => currentTime,
    document: fakeDoc,
    currentBuildId: 'build-1',
    onUpdate: (id) => updates.push(id),
  });

  // When started hidden, nothing is scheduled
  assert.equal(scheduledTasks.length, 0);
  assert.equal(fetchCount, 0);

  // Tab becomes visible -> triggers check at once and schedules next poll
  fakeDoc.visibilityState = 'visible';
  fakeDoc.trigger('visibilitychange');

  // Allow promise to settle
  await new Promise((r) => setImmediate(r));
  assert.equal(fetchCount, 1);
  assert.deepEqual(updates, ['build-2']);
  assert.equal(scheduledTasks.length, 1);

  // Tab becomes hidden -> clears pending timer
  fakeDoc.visibilityState = 'hidden';
  fakeDoc.trigger('visibilitychange');
  assert.equal(scheduledTasks.length, 0);

  watch.stop();
});

test('startVersionWatch: focus, online and accountgo:service-restored trigger immediate check', async () => {
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

  const winListeners = new Map();
  const fakeWin = {
    addEventListener: (type, fn) => {
      if (!winListeners.has(type)) winListeners.set(type, new Set());
      winListeners.get(type).add(fn);
    },
    removeEventListener: (type, fn) => {
      winListeners.get(type)?.delete(fn);
    },
    trigger: (type, event) => {
      for (const fn of winListeners.get(type) || []) fn(event);
    },
  };

  let fetchCount = 0;
  const watch = startVersionWatch({
    fetchImpl: async () => {
      fetchCount++;
      return new Response(JSON.stringify({ buildId: 'build-1' }), { status: 200 });
    },
    setTimeout: fakeSetTimeout,
    clearTimeout: fakeClearTimeout,
    now: () => currentTime,
    window: fakeWin,
    currentBuildId: 'build-1',
    onUpdate: () => {},
  });

  assert.equal(fetchCount, 0);

  // Window focus
  fakeWin.trigger('focus');
  await new Promise((r) => setImmediate(r));
  assert.equal(fetchCount, 1);

  // Window online
  fakeWin.trigger('online');
  await new Promise((r) => setImmediate(r));
  assert.equal(fetchCount, 2);

  // accountgo:service-restored
  fakeWin.trigger('accountgo:service-restored');
  await new Promise((r) => setImmediate(r));
  assert.equal(fetchCount, 3);

  watch.stop();
});

test('startVersionWatch: vite:preloadError triggers update and calls preventDefault', async () => {
  const winListeners = new Map();
  const fakeWin = {
    addEventListener: (type, fn) => {
      if (!winListeners.has(type)) winListeners.set(type, new Set());
      winListeners.get(type).add(fn);
    },
    removeEventListener: (type, fn) => {
      winListeners.get(type)?.delete(fn);
    },
    trigger: (type, event) => {
      for (const fn of winListeners.get(type) || []) fn(event);
    },
  };

  const updates = [];
  const watch = startVersionWatch({
    fetchImpl: async () => new Response('{}', { status: 200 }),
    setTimeout: () => 1,
    clearTimeout: () => {},
    window: fakeWin,
    currentBuildId: 'build-1',
    onUpdate: (id) => updates.push(id),
  });

  let defaultPrevented = false;
  fakeWin.trigger('vite:preloadError', {
    preventDefault: () => {
      defaultPrevented = true;
    },
  });

  assert.equal(defaultPrevented, true);
  assert.deepEqual(updates, ['preload-error']);

  // Second preloadError does not repeat
  fakeWin.trigger('vite:preloadError', { preventDefault: () => {} });
  assert.deepEqual(updates, ['preload-error']);

  watch.stop();
});

test('startVersionWatch: fetch throws, 404, bad JSON are ignored and polling continues', async () => {
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

  let responseType = 'throw';
  const updates = [];

  const watch = startVersionWatch({
    fetchImpl: async () => {
      if (responseType === 'throw') throw new Error('network down');
      if (responseType === '404') return new Response('Not Found', { status: 404 });
      if (responseType === 'bad-json') {
        return new Response('<html>not json</html>', {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        });
      }
      return new Response(JSON.stringify({ buildId: 'build-2' }), { status: 200 });
    },
    setTimeout: fakeSetTimeout,
    clearTimeout: fakeClearTimeout,
    now: () => currentTime,
    currentBuildId: 'build-1',
    onUpdate: (id) => updates.push(id),
  });

  // 1. Throws network error
  await triggerNext();
  assert.deepEqual(updates, []);
  assert.equal(scheduledTasks.length, 1);

  // 2. 404 response
  responseType = '404';
  await triggerNext();
  assert.deepEqual(updates, []);
  assert.equal(scheduledTasks.length, 1);

  // 3. Bad JSON
  responseType = 'bad-json';
  await triggerNext();
  assert.deepEqual(updates, []);
  assert.equal(scheduledTasks.length, 1);

  // 4. Success -> onUpdate
  responseType = 'success';
  await triggerNext();
  assert.deepEqual(updates, ['build-2']);

  watch.stop();
  assert.equal(scheduledTasks.length, 0);
});

test('startVersionWatch: stop() clears timers and listeners', () => {
  let timerCleared = false;
  const docListeners = new Map();
  const fakeDoc = {
    visibilityState: 'visible',
    addEventListener: (type, fn) => {
      if (!docListeners.has(type)) docListeners.set(type, new Set());
      docListeners.get(type).add(fn);
    },
    removeEventListener: (type, fn) => {
      docListeners.get(type)?.delete(fn);
    },
  };

  const watch = startVersionWatch({
    fetchImpl: async () => new Response('{}', { status: 200 }),
    setTimeout: () => 42,
    clearTimeout: (id) => {
      if (id === 42) timerCleared = true;
    },
    document: fakeDoc,
    currentBuildId: 'build-1',
    onUpdate: () => {},
  });

  assert.equal(docListeners.get('visibilitychange')?.size, 1);
  watch.stop();

  assert.equal(timerCleared, true);
  assert.equal(docListeners.get('visibilitychange')?.size, 0);
});

test('deferred-update store and shouldOpenUpdateDialog decision function', () => {
  // Initially no deferred update
  assert.equal(getDeferredUpdate(), null);

  // Base case: pendingId present, no deferredId, service ok, not boot suppressed -> open
  assert.equal(
    shouldOpenUpdateDialog({
      pendingId: 'build-2',
      deferredId: null,
      serviceState: 'ok',
      bootSuppressed: false,
    }),
    true
  );

  // Service unavailable -> do not open
  assert.equal(
    shouldOpenUpdateDialog({
      pendingId: 'build-2',
      deferredId: null,
      serviceState: 'unavailable',
      bootSuppressed: false,
    }),
    false
  );

  // Boot suppressed -> do not open
  assert.equal(
    shouldOpenUpdateDialog({
      pendingId: 'build-2',
      deferredId: null,
      serviceState: 'ok',
      bootSuppressed: true,
    }),
    false
  );

  // No pendingId -> do not open
  assert.equal(
    shouldOpenUpdateDialog({
      pendingId: null,
      deferredId: null,
      serviceState: 'ok',
      bootSuppressed: false,
    }),
    false
  );

  // Defer build-2
  deferUpdate('build-2');
  assert.equal(getDeferredUpdate(), 'build-2');

  // Same deferred id -> do not open
  assert.equal(
    shouldOpenUpdateDialog({
      pendingId: 'build-2',
      deferredId: getDeferredUpdate(),
      serviceState: 'ok',
      bootSuppressed: false,
    }),
    false
  );

  // Newer id (build-3) arrives -> opens again!
  assert.equal(
    shouldOpenUpdateDialog({
      pendingId: 'build-3',
      deferredId: getDeferredUpdate(),
      serviceState: 'ok',
      bootSuppressed: false,
    }),
    true
  );
});
