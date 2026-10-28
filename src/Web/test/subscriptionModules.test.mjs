import assert from 'node:assert/strict';
import { after, test } from 'node:test';
import { createServer } from 'vite';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

const server = await createServer({ server: { middlewareMode: true, hmr: false, ws: false }, appType: 'custom' });
after(() => server.close());

const { SubscriptionSettingsTab } = await server.ssrLoadModule('/src/components/SubscriptionSettingsTab.tsx');
const { CapabilitiesProvider } = await server.ssrLoadModule('/src/lib/entitlements.ts');
const { primePlanCatalog } = await server.ssrLoadModule('/src/lib/usePlanCatalog.ts');
const { AuthProvider } = await server.ssrLoadModule('/src/auth/AuthContext.tsx');

// Shape of GET /api/Subscription/plans. The plan → module matrix itself is tested server-side
// (SubscriptionPlanTests, CompanyModulesTests); the SPA only renders what the server returns.
const CATALOG = [
  { key: 'invoicing', name: 'Invoicing', description: 'Invoices and bills.', includedModules: ['sales', 'purchases'], requiredModules: ['sales', 'purchases'], selfService: true, monthlyPrice: 15 },
  { key: 'accounting', name: 'Accounting', description: 'Full bookkeeping.', includedModules: ['sales', 'financials', 'purchases', 'reports'], requiredModules: ['sales'], selfService: true, monthlyPrice: 39 },
  { key: 'enterprise', name: 'Enterprise', description: 'Everything.', includedModules: ['sales', 'financials', 'purchases', 'reports', 'inventory', 'donations', 'audit'], requiredModules: ['sales'], selfService: true, monthlyPrice: 99 },
];

const render = (planId, activeModules) => renderToStaticMarkup(createElement(AuthProvider, null,
  createElement(CapabilitiesProvider, {
    orgSlug: 'test',
    bootstrapModules: activeModules,
    hasPermission: () => true,
  }, createElement(SubscriptionSettingsTab, {
    tenantContext: {
      subscription: { planId, planName: planId, status: 'active', activeModules },
      updateSubscription: async () => {},
    },
  }))));

test('subscription settings locks modules outside the current plan', () => {
  primePlanCatalog(CATALOG);
  const html = render('accounting', ['sales', 'financials', 'purchases', 'reports']);
  assert.match(html, /General Ledger &amp; Banking[\s\S]*?Financial Reports[\s\S]*?Inventory &amp; Items/);
  assert.match(html, /Inventory &amp; Items[\s\S]*?Upgrade/);
  assert.match(html, /Donations &amp; Non-Profit[\s\S]*?Upgrade/);
  assert.match(html, /Audit Trail &amp; Compliance[\s\S]*?Upgrade/);
  assert.match(html, /Inventory &amp; Items[\s\S]*?aria-disabled="true"/);
  assert.match(html, /General Ledger &amp; Banking[\s\S]*?aria-disabled="false"/);
  assert.ok(html.includes('of 4 plan modules active'));
});

test('required modules from the server catalog stay on even when client state omits them', () => {
  primePlanCatalog(CATALOG);
  const html = render('invoicing', ['sales']);
  assert.ok(html.includes('<strong>2</strong> of 2 plan modules active'));
  assert.match(html, /aria-label="Purchases &amp; Bills is always on"/);
});

test('plan cards come from the server catalog', () => {
  primePlanCatalog(CATALOG.filter((plan) => plan.key !== 'enterprise'));
  const html = render('accounting', ['sales', 'financials', 'purchases', 'reports']);
  assert.ok(html.includes('Switch to Invoicing'));
  assert.ok(!html.includes('Switch to Enterprise'));
  primePlanCatalog(null);
});
