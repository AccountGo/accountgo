import test from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'vite';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';

test('workspace selector links to a profile accessible without a workspace', async () => {
  const server = await createServer({ server: { middlewareMode: true, hmr: false, ws: false }, appType: 'custom' });
  try {
    const { SelectOrg } = await server.ssrLoadModule('/src/pages/SelectOrg.tsx');
    const { Profile } = await server.ssrLoadModule('/src/pages/Profile.tsx');
    const { AuthProvider } = await server.ssrLoadModule('/src/auth/AuthContext.tsx');
    const render = (page) => renderToStaticMarkup(createElement(AuthProvider, null,
      createElement(MemoryRouter, null, createElement(page))));
    const selector = render(SelectOrg);
    assert.match(selector, /href="\/profile"/);
    const profile = render(Profile);
    assert.match(profile, /Delete account/);
    assert.doesNotMatch(profile, /Assigned RBAC Roles/);
  } finally {
    await server.close();
  }
});
