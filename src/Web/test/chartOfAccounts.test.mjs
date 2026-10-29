import assert from 'node:assert/strict';
import { after, test } from 'node:test';
import { createServer } from 'vite';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter, Outlet, Route, Routes } from 'react-router-dom';

const server = await createServer({ server: { middlewareMode: true, hmr: false, ws: false }, appType: 'custom' });
after(() => server.close());

const { ChartOfAccounts } = await server.ssrLoadModule('/src/pages/ChartOfAccounts.tsx');
const { AuthProvider } = await server.ssrLoadModule('/src/auth/AuthContext.tsx');

function renderPage(permissions, path = '/org/test-org/financials/accounts', orgOverride = {}, chartStatusOverride = null) {
  return renderToStaticMarkup(
    createElement(
      AuthProvider,
      null,
      createElement(
        MemoryRouter,
        { initialEntries: [path] },
        createElement(
          Routes,
          null,
          createElement(
            Route,
            {
              path: '/org/:orgSlug',
              element: createElement(Outlet, {
                context: {
                  currentOrg: { slug: 'test-org', name: 'Test Org', currency: 'USD', role: 'Owner', ...orgOverride },
                  hasPermission: (permission) => permissions.includes(permission),
                },
              }),
            },
            createElement(Route, {
              path: 'financials/accounts',
              element: createElement(ChartOfAccounts, { initialChartStatus: chartStatusOverride }),
            })
          )
        )
      )
    )
  );
}

test('Chart of Accounts denies access when financials.accounts.view is missing', () => {
  const html = renderPage([]);
  assert.ok(html.includes('Access Denied') || html.includes('PermissionDenied') || html.includes('financials.accounts.view'));
  assert.ok(!html.includes('Chart of Accounts'));
});

test('Chart of Accounts renders view-only mode when user lacks financials.accounts.manage', () => {
  const html = renderPage(['financials.accounts.view']);
  assert.ok(html.includes('Chart of Accounts'));
  assert.ok(html.includes('Refresh accounts'));
  assert.ok(html.includes('Export'));
  // "Add Account" and "Import" should NOT be rendered
  assert.ok(!html.includes('Add Account'));
  assert.ok(!html.includes('Import'));
});

test('Chart of Accounts renders manage actions when user has financials.accounts.manage', () => {
  const html = renderPage(['financials.accounts.view', 'financials.accounts.manage']);
  assert.ok(html.includes('Chart of Accounts'));
  assert.ok(html.includes('Add Account'));
  assert.ok(html.includes('Import'));
  assert.ok(html.includes('Export'));
});

test('Chart of Accounts distinguishes owner role for structure management', () => {
  const ownerHtml = renderPage(['financials.accounts.view', 'financials.accounts.manage'], '/org/test-org/financials/accounts', { role: 'Owner' });
  assert.ok(ownerHtml.includes('Import'));
  assert.ok(ownerHtml.includes('Add Account'));

  const memberHtml = renderPage(['financials.accounts.view'], '/org/test-org/financials/accounts', { role: 'Member' });
  assert.ok(!memberHtml.includes('Import'));
  assert.ok(!memberHtml.includes('Add Account'));
});

test('Member with manage on empty books does not get Owner-only Replace Chart CTA', () => {
  const emptyBooksStatus = {
    isEmptyBooks: true,
    postedTransactionsCount: 0,
    unpostedDraftCount: 0,
    canReplace: true,
  };

  // Member with manage sees the empty-books banner, but NO "Replace Chart" button
  const memberHtml = renderPage(
    ['financials.accounts.view', 'financials.accounts.manage'],
    '/org/test-org/financials/accounts',
    { role: 'Member' },
    emptyBooksStatus
  );
  assert.ok(memberHtml.includes('Books not started'));
  assert.ok(!memberHtml.includes('Replace Chart'));

  // Owner with manage sees both the empty-books banner AND the "Replace Chart" button
  const ownerHtml = renderPage(
    ['financials.accounts.view', 'financials.accounts.manage'],
    '/org/test-org/financials/accounts',
    { role: 'Owner' },
    emptyBooksStatus
  );
  assert.ok(ownerHtml.includes('Books not started'));
  assert.ok(ownerHtml.includes('Replace Chart'));
});
