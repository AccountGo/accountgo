import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createServer } from 'vite';

const __dirname = dirname(fileURLToPath(import.meta.url));

test('AuthContext keeps accountgo-session-refresh lock and does not persist tokens', () => {
  const src = readFileSync(resolve(__dirname, '../src/auth/AuthContext.tsx'), 'utf-8');
  assert.match(src, /SESSION_REFRESH_LOCK_NAME\s*=\s*'accountgo-session-refresh'/);
  assert.match(src, /refreshPromiseRef/);
  assert.match(src, /withRefreshLock/);
  assert.match(src, /fetch\('\/api\/auth\/refresh'/);
  // Tokens stay in memory — no persistence APIs invoked for auth payloads.
  assert.doesNotMatch(src, /localStorage\.setItem/);
  assert.doesNotMatch(src, /sessionStorage\.setItem/);
  assert.doesNotMatch(src, /new BroadcastChannel/);
});

test('two contending callers sharing single-flight + lock produce exactly one fetch to /api/auth/refresh', async () => {
  const server = await createServer({ server: { middlewareMode: true, hmr: false, ws: false }, appType: 'custom' });
  try {
    const { withRefreshLock, SESSION_REFRESH_LOCK_NAME } = await server.ssrLoadModule('/src/auth/AuthContext.tsx');
    assert.equal(SESSION_REFRESH_LOCK_NAME, 'accountgo-session-refresh');

    let held = null;
    const waiters = [];
    const fakeLocks = {
      request(name, work) {
        assert.equal(name, 'accountgo-session-refresh');
        return new Promise((resolve, reject) => {
          const run = async () => {
            try {
              resolve(await work());
            } catch (err) {
              reject(err);
            } finally {
              held = null;
              const next = waiters.shift();
              if (next) {
                held = next;
                next();
              }
            }
          };
          if (held) waiters.push(run);
          else {
            held = run;
            run();
          }
        });
      },
    };

    Object.defineProperty(globalThis, 'navigator', {
      value: { locks: fakeLocks },
      configurable: true,
      writable: true,
    });

    let fetchCount = 0;
    const originalFetch = globalThis.fetch;
    globalThis.fetch = async (input) => {
      const url = typeof input === 'string' ? input : String(input?.url ?? input);
      if (url.includes('/api/auth/refresh')) {
        fetchCount += 1;
        await new Promise((r) => setTimeout(r, 20));
        return {
          ok: true,
          status: 200,
          json: async () => ({ accessToken: 'tok', expiresIn: 900, user: { id: 'u1' } }),
          headers: { get: () => null },
        };
      }
      throw new Error(`unexpected fetch: ${url}`);
    };

    // Same shape as AuthContext.performRefresh: one in-flight promise + withRefreshLock.
    let refreshPromise = null;
    const performRefresh = () => {
      if (refreshPromise) return refreshPromise;
      refreshPromise = withRefreshLock(async () => {
        try {
          const res = await fetch('/api/auth/refresh', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            credentials: 'include',
          });
          assert.equal(res.ok, true);
          const data = await res.json();
          return data.accessToken;
        } finally {
          refreshPromise = null;
        }
      });
      return refreshPromise;
    };

    const [a, b] = await Promise.all([performRefresh(), performRefresh()]);
    assert.equal(a, 'tok');
    assert.equal(b, 'tok');
    assert.equal(fetchCount, 1);

    globalThis.fetch = originalFetch;
    delete globalThis.navigator;
  } finally {
    await server.close();
  }
});
