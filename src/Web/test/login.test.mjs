import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createServer } from 'vite';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';

const __dirname = dirname(fileURLToPath(import.meta.url));

test('global page title in index.html is AccountGo - Invoicing & Accounting for Small Businesses', () => {
  const html = readFileSync(resolve(__dirname, '../index.html'), 'utf-8');
  assert.match(html, /<title>AccountGo - Invoicing & Accounting for Small Businesses<\/title>/);
});

test('Login page includes privacy and terms acceptance checkbox and excludes edge resilience note', async () => {
  const server = await createServer({ server: { middlewareMode: true, hmr: false, ws: false }, appType: 'custom' });
  try {
    const { Login } = await server.ssrLoadModule('/src/pages/Login.tsx');
    const { AuthProvider } = await server.ssrLoadModule('/src/auth/AuthContext.tsx');

    // Test signin mode
    const signinHtml = renderToStaticMarkup(
      createElement(
        AuthProvider,
        null,
        createElement(MemoryRouter, { initialEntries: ['/login'] }, createElement(Login, { initialMode: 'signin' }))
      )
    );

    assert.ok(signinHtml.includes('signin-accept-terms'));
    assert.match(signinHtml, /By signing in, you accept the.*Privacy Policy.*Terms of Use/i);
    assert.ok(!signinHtml.includes('Enterprise Multi-Tenant Isolation & Cloudflare Edge Resilience.'));

    // Test signup mode
    const signupHtml = renderToStaticMarkup(
      createElement(
        AuthProvider,
        null,
        createElement(MemoryRouter, { initialEntries: ['/signup'] }, createElement(Login, { initialMode: 'signup' }))
      )
    );

    assert.ok(signupHtml.includes('signup-accept-terms'));
    assert.doesNotMatch(signupHtml, /signup-first-name|signup-last-name|signup-org-name/);
    assert.match(signupHtml, /By signing up, you accept the.*Privacy Policy.*Terms of Use/i);
    assert.ok(!signupHtml.includes('Enterprise Multi-Tenant Isolation & Cloudflare Edge Resilience.'));
  } finally {
    await server.close();
  }
});

test('login offers Google sign-in when configured while retaining email OTP', async () => {
  const previous = process.env.VITE_GOOGLE_CLIENT_ID;
  process.env.VITE_GOOGLE_CLIENT_ID = 'test-client.apps.googleusercontent.com';
  const server = await createServer({ server: { middlewareMode: true, hmr: false, ws: false }, appType: 'custom' });
  try {
    const { Login } = await server.ssrLoadModule('/src/pages/Login.tsx');
    const { AuthProvider } = await server.ssrLoadModule('/src/auth/AuthContext.tsx');
    const html = renderToStaticMarkup(createElement(AuthProvider, null,
      createElement(MemoryRouter, { initialEntries: ['/login'] }, createElement(Login, { initialMode: 'signin' }))));
    assert.match(html, /Google/);
    assert.match(html, /signin-accept-terms/);
    assert.doesNotMatch(html, /Accept the Privacy Policy and Terms of Use below to continue/);
    const signup = renderToStaticMarkup(createElement(AuthProvider, null,
      createElement(MemoryRouter, { initialEntries: ['/signup'] }, createElement(Login, { initialMode: 'signup' }))));
    assert.match(signup, /Google/);
  } finally {
    await server.close();
    if (previous === undefined) delete process.env.VITE_GOOGLE_CLIENT_ID;
    else process.env.VITE_GOOGLE_CLIENT_ID = previous;
  }
});
