import assert from 'node:assert/strict';
import { after, test } from 'node:test';
import { createServer } from 'vite';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

const server = await createServer({ server: { middlewareMode: true, hmr: false, ws: false }, appType: 'custom' });
after(() => server.close());

const {
  PLAN_INCLUDED_MODULES,
  isModuleIncludedInPlan,
  sanitizeModulesForPlan,
  SubscriptionSettingsTab,
} = await server.ssrLoadModule('/src/components/SubscriptionSettingsTab.tsx');

test('modules are ordered by subscription tier and plan inclusion matches the matrix', () => {
  assert.deepEqual(PLAN_INCLUDED_MODULES.invoicing, ['sales']);
  assert.deepEqual(PLAN_INCLUDED_MODULES.accounting, ['sales', 'financials', 'purchases', 'reports']);
  assert.deepEqual(PLAN_INCLUDED_MODULES.enterprise, [
    'sales', 'financials', 'purchases', 'reports', 'inventory', 'donations', 'audit',
  ]);
  assert.equal(isModuleIncludedInPlan('accounting', 'reports'), true);
  assert.equal(isModuleIncludedInPlan('accounting', 'inventory'), false);
  assert.equal(isModuleIncludedInPlan('invoicing', 'financials'), false);
  assert.equal(isModuleIncludedInPlan('enterprise', 'audit'), true);
});

test('sanitizeModulesForPlan drops modules outside the selected tier and keeps sales', () => {
  assert.deepEqual(
    sanitizeModulesForPlan('accounting', ['sales', 'financials', 'inventory', 'audit']),
    ['sales', 'financials'],
  );
  assert.deepEqual(sanitizeModulesForPlan('invoicing', ['financials']), ['sales']);
  assert.deepEqual(
    sanitizeModulesForPlan('enterprise', ['reports', 'donations']),
    ['sales', 'reports', 'donations'],
  );
});

test('subscription settings locks modules outside the current plan', () => {
  const html = renderToStaticMarkup(createElement(SubscriptionSettingsTab, {
    tenantContext: {
      subscription: {
        planId: 'accounting',
        planName: 'Accounting',
        status: 'active',
        activeModules: ['sales', 'financials', 'purchases', 'reports'],
      },
      updateSubscription: async () => {},
    },
  }));
  assert.match(html, /General Ledger &amp; Banking[\s\S]*?Financial Reports[\s\S]*?Inventory &amp; Items/);
  assert.match(html, /Inventory &amp; Items[\s\S]*?Upgrade/);
  assert.match(html, /Donations &amp; Non-Profit[\s\S]*?Upgrade/);
  assert.match(html, /Audit Trail &amp; Compliance[\s\S]*?Upgrade/);
  assert.match(html, /Inventory &amp; Items[\s\S]*?aria-disabled="true"/);
  assert.match(html, /General Ledger &amp; Banking[\s\S]*?aria-disabled="false"/);
  assert.ok(html.includes('of 4 plan modules active'));
});
