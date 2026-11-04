import assert from 'node:assert/strict';
import test from 'node:test';
import worker from '../worker.ts';

const appRequest = (path) => new Request(`https://app.accountgo.dev${path}`);

test('app robots response blocks crawling without using the SPA asset fallback', async () => {
  const response = await worker.fetch(appRequest('/robots.txt'), {
    ASSETS: { fetch() { throw new Error('robots must not serve the SPA shell'); } },
  }, {});
  assert.equal(response.status, 200);
  assert.match(response.headers.get('content-type'), /^text\/plain/);
  assert.match(await response.text(), /User-agent: \*\nDisallow: \/\n/);
});

test('SPA routes preserve the asset response and prohibit indexing', async () => {
  const response = await worker.fetch(appRequest('/create-org'), {
    ASSETS: { fetch: async () => new Response('<html>workspace</html>', {
      headers: { 'Content-Type': 'text/html', 'ETag': 'asset-version' },
    }) },
  }, {});
  assert.equal(response.status, 200);
  assert.equal(response.headers.get('X-Robots-Tag'), 'noindex, nofollow');
  assert.equal(response.headers.get('ETag'), 'asset-version');
  assert.equal(await response.text(), '<html>workspace</html>');
});

test('API proxy preserves backend indexing policy and response body', async () => {
  const response = await worker.fetch(appRequest('/api/example'), {
    API: { fetch: async () => new Response('{"ok":true}', {
      status: 202, headers: { 'Content-Type': 'application/json', 'X-Robots-Tag': 'noindex' },
    }) },
    SERVER_TIMING_ENABLED: 'false',
  }, {});
  assert.equal(response.status, 202);
  assert.equal(response.headers.get('X-Robots-Tag'), 'noindex');
  assert.deepEqual(await response.json(), { ok: true });
});
