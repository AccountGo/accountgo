import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createServer } from 'vite';

const __dirname = dirname(fileURLToPath(import.meta.url));

async function loadRunner() {
  const server = await createServer({ server: { middlewareMode: true, hmr: false, ws: false }, appType: 'custom' });
  const mod = await server.ssrLoadModule('/src/lib/mfaSubmission.ts');
  return { server, runMfaSignIn: mod.runMfaSignIn };
}

test('passkey sign-in stays locked while the post-auth hand-off is still in flight', async () => {
  const { server, runMfaSignIn } = await loadRunner();
  try {
    const stages = [];
    let releaseHandOff;
    const handOff = new Promise((r) => {
      releaseHandOff = r;
    });

    const run = runMfaSignIn({
      verify: async (report) => {
        report('prompting');
        report('verifying');
        return { accessToken: 't' };
      },
      onSuccess: () => handOff, // slow workspace resolution / navigation
      setStage: (s) => stages.push(s),
      onError: () => assert.fail('should not error'),
      fallbackError: 'x',
    });

    await new Promise((r) => setTimeout(r, 10));
    // Server accepted the passkey but the hand-off has not finished: must NOT be back to idle.
    assert.equal(stages.at(-1), 'signing-in');

    releaseHandOff();
    assert.equal(await run, true);
    assert.ok(!stages.includes('idle'), 'a successful sign-in must never re-enable the button');
  } finally {
    await server.close();
  }
});

test('failures release the lock and surface the error, including post-auth hand-off errors', async () => {
  const { server, runMfaSignIn } = await loadRunner();
  try {
    for (const [verify, onSuccess, expected] of [
      [async () => { throw new Error('Passkey verification failed.'); }, async () => {}, 'Passkey verification failed.'],
      [async () => ({}), async () => { throw new Error('This invitation has expired.'); }, 'This invitation has expired.'],
      [async () => { throw {}; }, async () => {}, 'fallback'],
    ]) {
      const stages = [];
      let error = null;
      const ok = await runMfaSignIn({
        verify,
        onSuccess,
        setStage: (s) => stages.push(s),
        onError: (m) => {
          error = m;
        },
        fallbackError: 'fallback',
      });
      assert.equal(ok, false);
      assert.equal(error, expected);
      assert.equal(stages.at(-1), 'idle');
    }
  } finally {
    await server.close();
  }
});

test('MFA challenge awaits onSuccess and does not unconditionally reset the submitting state', () => {
  const src = readFileSync(resolve(__dirname, '../src/components/mfa/MfaChallenge.tsx'), 'utf-8');
  assert.match(src, /onSuccess: \(authResponse: AuthResponse\) => void \| Promise<void>/);
  assert.match(src, /runMfaSignIn/);
  assert.doesNotMatch(src, /finally\s*\{\s*setIsSubmitting\(false\)/);
});

test('login claims navigation ownership before completing an MFA session', () => {
  const src = readFileSync(resolve(__dirname, '../src/pages/Login.tsx'), 'utf-8');
  const handlers = src.match(/verificationOwnsNavigation\.current = true;\s*completeSession\(authResponse\);/g) || [];
  assert.equal(handlers.length, 2);
});
