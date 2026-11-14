import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  startIdleSession,
  ACTIVITY_REFRESH_MS,
  EXPIRY_WARNING_MS,
  IDLE_WARNING_MS,
} from '../src/lib/idleSession.ts';

const __dirname = dirname(fileURLToPath(import.meta.url));
const MIN = 60_000;

// One fake clock shared by any number of tabs; timers only fire when the clock is advanced.
function createWorld(start = 1_000_000) {
  let now = start;
  let nextId = 1;
  const timers = new Map();
  const channels = new Set();
  const world = {
    get now() {
      return now;
    },
    advance(ms, { sleep = false } = {}) {
      const end = now + ms;
      if (sleep) {
        now = end; // the machine slept: no timer ran on time
        return;
      }
      for (;;) {
        const due = [...timers.entries()].filter(([, t]) => t.at <= end).sort((a, b) => a[1].at - b[1].at)[0];
        if (!due) break;
        timers.delete(due[0]);
        now = Math.max(now, due[1].at);
        due[1].fn();
      }
      now = end;
    },
    tab() {
      const listeners = new Map();
      const docListeners = new Map();
      const channel = {
        onmessage: null,
        sent: [],
        closed: false,
        postMessage(message) {
          this.sent.push(message);
          for (const other of channels) if (other !== channel && !other.closed) other.onmessage?.({ data: message });
        },
        close() {
          this.closed = true;
          channels.delete(channel);
        },
      };
      channels.add(channel);
      const target = (map) => ({
        addEventListener: (type, fn) => map.set(type, fn),
        removeEventListener: (type) => map.delete(type),
      });
      const doc = { ...target(docListeners), visibilityState: 'visible' };
      const env = {
        now: () => now,
        setTimeout: (fn, ms) => {
          const id = nextId++;
          timers.set(id, { at: now + ms, fn });
          return id;
        },
        clearTimeout: (id) => timers.delete(id),
        channel,
        window: target(listeners),
        document: doc,
      };
      const events = { warnings: [], signOuts: [], refreshes: 0 };
      return {
        env,
        events,
        channel,
        fire: (type) => listeners.get(type)?.(),
        listening: () => listeners.size,
        becomeVisible() {
          doc.visibilityState = 'visible';
          docListeners.get('visibilitychange')?.();
        },
        start(options = {}) {
          return startIdleSession(
            {
              idleMs: 15 * MIN,
              sessionExpiresAt: null,
              refreshedAt: now,
              onWarning: (w) => events.warnings.push(w),
              onRefreshDue: () => events.refreshes++,
              onSignOut: (reason, origin) => events.signOuts.push({ reason, origin }),
              ...options,
            },
            env,
          );
        },
      };
    },
  };
  return world;
}

const lastWarning = (tab) => tab.events.warnings.at(-1) ?? null;

test('warns a minute before the idle limit, then signs out and tells other tabs', () => {
  const world = createWorld();
  const a = world.tab();
  const b = world.tab();
  a.start();
  b.start();

  world.advance(15 * MIN - IDLE_WARNING_MS - 1);
  assert.equal(lastWarning(a), null);
  world.advance(1);
  assert.equal(lastWarning(a)?.kind, 'idle');

  world.advance(IDLE_WARNING_MS);
  assert.deepEqual(a.events.signOuts[0], { reason: 'idle', origin: true });
  // Tab B either timed out itself or heard about it; it never calls the server as the origin twice.
  assert.equal(b.events.signOuts.length, 1);
  assert.equal(a.listening(), 0, 'listeners removed after sign-out');
});

test('moving the mouse resets the timer before the warning appears', () => {
  const world = createWorld();
  const a = world.tab();
  a.start();

  world.advance(13 * MIN);
  a.fire('pointermove');
  world.advance(13 * MIN);
  a.fire('wheel');
  world.advance(13 * MIN);
  assert.equal(a.events.signOuts.length, 0);
  assert.equal(lastWarning(a), null);
});

test('once the warning is up, reaching for its buttons does not dismiss it; only "Stay signed in" does', () => {
  const world = createWorld();
  const a = world.tab();
  const session = a.start();

  world.advance(14 * MIN + 10_000);
  assert.equal(lastWarning(a)?.kind, 'idle');
  a.fire('pointermove');
  a.fire('keydown'); // e.g. Tab to reach "Sign out"
  a.fire('pointerdown');
  assert.equal(lastWarning(a)?.kind, 'idle', 'ambient input leaves the prompt open');

  session.markActive();
  assert.equal(lastWarning(a), null);
  world.advance(14 * MIN);
  assert.equal(a.events.signOuts.length, 0);
});

test('ambient input during the warning does not keep the session alive', () => {
  const world = createWorld();
  const a = world.tab();
  a.start();

  world.advance(14 * MIN + 10_000);
  a.fire('pointermove');
  world.advance(IDLE_WARNING_MS);
  assert.deepEqual(a.events.signOuts, [{ reason: 'idle', origin: true }]);
});

test('"Stay signed in" in one tab closes the warning in every tab', () => {
  const world = createWorld();
  const a = world.tab();
  const b = world.tab();
  const sa = a.start();
  b.start();

  world.advance(14 * MIN + 10_000);
  assert.equal(lastWarning(a)?.kind, 'idle');
  assert.equal(lastWarning(b)?.kind, 'idle');
  sa.markActive();
  assert.equal(lastWarning(a), null);
  assert.equal(lastWarning(b), null);
});

test('activity in another tab keeps every tab signed in', () => {
  const world = createWorld();
  const a = world.tab();
  const b = world.tab();
  a.start();
  b.start();

  for (let i = 0; i < 6; i++) {
    world.advance(10 * MIN);
    a.fire('keydown');
  }
  assert.equal(a.events.signOuts.length, 0);
  assert.equal(b.events.signOuts.length, 0);
});

test('a laptop waking after the limit signs out as soon as the tab is visible', () => {
  const world = createWorld();
  const a = world.tab();
  a.start();

  world.advance(3 * 60 * MIN, { sleep: true });
  assert.equal(a.events.signOuts.length, 0);
  a.becomeVisible();
  assert.deepEqual(a.events.signOuts, [{ reason: 'idle', origin: true }]);
});

test('asks for a session refresh at most every five minutes while active', () => {
  const world = createWorld();
  const a = world.tab();
  const b = world.tab();
  const sa = a.start({ idleMs: 60 * MIN });
  b.start({ idleMs: 60 * MIN });

  world.advance(ACTIVITY_REFRESH_MS - 1);
  a.fire('pointermove');
  assert.equal(a.events.refreshes, 0);
  world.advance(1);
  a.fire('pointermove');
  a.fire('pointermove');
  assert.equal(a.events.refreshes, 1);

  // Another tab's refresh counts for this one.
  sa.markRefreshed();
  world.advance(ACTIVITY_REFRESH_MS - 1);
  b.fire('pointermove');
  assert.equal(b.events.refreshes, 0);
});

test('the 12-hour limit warns two minutes ahead, ignores activity, and signs out as expired', () => {
  const world = createWorld();
  const a = world.tab();
  const expiresAt = world.now + 30 * MIN;
  const session = a.start({ sessionExpiresAt: expiresAt });

  for (let i = 0; i < 2; i++) {
    world.advance(13 * MIN);
    a.fire('pointermove');
  }
  world.advance(4 * MIN - EXPIRY_WARNING_MS); // 28 minutes in: two minutes before the hard limit
  assert.deepEqual(lastWarning(a), { kind: 'expiry', deadline: expiresAt });

  a.fire('pointermove');
  assert.equal(lastWarning(a)?.kind, 'expiry', 'activity does not extend the hard limit');
  session.dismissExpiryWarning();
  assert.equal(lastWarning(a), null);

  world.advance(EXPIRY_WARNING_MS);
  assert.deepEqual(a.events.signOuts, [{ reason: 'expired', origin: true }]);
});

test('a sign-out heard from another tab is not re-broadcast', () => {
  const world = createWorld();
  const a = world.tab();
  const b = world.tab();
  a.start();
  b.start();

  a.channel.postMessage({ type: 'signout', reason: 'idle' });
  assert.deepEqual(b.events.signOuts, [{ reason: 'idle', origin: false }]);
  assert.equal(b.channel.sent.length, 0);
});

test('cross-tab markRefreshed propagates updated idleMs and invokes onRemoteIdleMs', () => {
  const world = createWorld();
  const a = world.tab();
  const b = world.tab();
  const remoteUpdates = [];
  const sessionA = a.start({ idleMs: 15 * MIN });
  b.start({ idleMs: 15 * MIN, onRemoteIdleMs: (ms, at) => remoteUpdates.push({ ms, at }) });

  sessionA.markRefreshed(world.now, 120 * MIN);
  assert.deepEqual(remoteUpdates, [{ ms: 120 * MIN, at: world.now }]);

  // Advancing past the old 15m limit should not warn or sign out Tab B now that idleMs is 120m.
  world.advance(20 * MIN);
  assert.equal(b.events.signOuts.length, 0);
  assert.equal(lastWarning(b), null);
});

test('AuthContext uses the idle engine but keeps tokens out of shared channels', () => {
  const engine = readFileSync(resolve(__dirname, '../src/lib/idleSession.ts'), 'utf-8');
  // Messages carry timestamps and reasons only.
  assert.doesNotMatch(engine, /accessToken|refreshToken|Bearer/);
  const auth = readFileSync(resolve(__dirname, '../src/auth/AuthContext.tsx'), 'utf-8');
  assert.match(auth, /startIdleSession\(/);
  assert.match(auth, /\/api\/auth\/logout\?reason=/);
});
