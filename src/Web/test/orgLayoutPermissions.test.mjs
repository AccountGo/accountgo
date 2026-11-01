import assert from 'node:assert/strict';
import { after, test } from 'node:test';
import { createServer } from 'vite';

const server = await createServer({ server: { middlewareMode: true, hmr: false, ws: false }, appType: 'custom' });
after(() => server.close());

const { evaluateHasPermission } = await server.ssrLoadModule('/src/layouts/OrgLayout.tsx');

test('evaluateHasPermission denies functional permissions when permissions list is empty regardless of Owner/Admin/Member role', () => {
  assert.equal(evaluateHasPermission([], 'financials.accounts.view'), false);
  assert.equal(evaluateHasPermission([], 'sales.invoices.view'), false);
  assert.equal(evaluateHasPermission([], ['sales.invoices.view', 'purchases.bills.view']), false);
  assert.equal(evaluateHasPermission(undefined, 'reports.view'), false);
});

test('evaluateHasPermission allows only permissions explicitly present in server-resolved permissions array', () => {
  const salesRepPerms = ['sales.customers.view', 'sales.invoices.view', 'sales.invoices.create'];
  assert.equal(evaluateHasPermission(salesRepPerms, 'sales.invoices.view'), true);
  assert.equal(evaluateHasPermission(salesRepPerms, ['financials.accounts.view', 'sales.invoices.create']), true);
  assert.equal(evaluateHasPermission(salesRepPerms, 'financials.accounts.view'), false);
  assert.equal(evaluateHasPermission(salesRepPerms, 'purchases.bills.view'), false);
});
