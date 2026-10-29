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
const { derivePlanCards } = await server.ssrLoadModule('/src/lib/planCards.ts');
const { AuthProvider } = await server.ssrLoadModule('/src/auth/AuthContext.tsx');

// Shape of GET /api/Subscription/plans. The plan → module matrix itself is tested server-side;
// the SPA only renders what the server returns. Fixture keys stay product-neutral.
const CATALOG = [
  { key: 'plan_a', name: 'Plan A', description: 'Plan A modules.', includedModules: ['sales', 'purchases'], requiredModules: ['sales', 'purchases'], selfService: true, monthlyPrice: 1 },
  { key: 'plan_b', name: 'Plan B', description: 'Plan B modules.', includedModules: ['sales', 'financials', 'purchases', 'reports'], requiredModules: ['sales'], selfService: true, monthlyPrice: 2 },
  { key: 'plan_c', name: 'Plan C', description: 'Plan C modules.', includedModules: ['sales', 'financials', 'purchases', 'reports', 'inventory', 'donations', 'audit'], requiredModules: ['sales'], selfService: true, monthlyPrice: 3 },
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
  const html = render('plan_b', ['sales', 'financials', 'purchases', 'reports']);
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
  const html = render('plan_a', ['sales']);
  assert.ok(html.includes('<strong>2</strong> of 2 plan modules active'));
  assert.match(html, /aria-label="Purchases &amp; Bills is always on"/);
});

test('plan cards come from the server catalog', () => {
  primePlanCatalog(CATALOG.filter((plan) => plan.key !== 'plan_c'));
  const html = render('plan_b', ['sales', 'financials', 'purchases', 'reports']);
  assert.ok(html.includes('Switch to Plan A'));
  assert.ok(!html.includes('Switch to Plan C'));
  primePlanCatalog(null);
});

test('derivePlanCards: community plan with no transitions shows one card and no buttons', () => {
  const cards = derivePlanCards('community', [], [
    { key: 'community', name: 'Community', description: 'All modules.', selfService: true, monthlyPrice: null },
  ]);
  assert.equal(cards.length, 1);
  assert.equal(cards[0].role, 'current');
  assert.equal(cards[0].actionLabel, '');
  assert.equal(cards[0].monthlyPrice, null);
});

test('derivePlanCards: fixture catalog plus transitions yields Upgrade / Switch labels', () => {
  const catalog = [
    { key: 'plan_a', name: 'Plan A', description: 'A', selfService: true, monthlyPrice: 0 },
    { key: 'plan_b', name: 'Plan B', description: 'B', selfService: true, monthlyPrice: 15 },
    { key: 'plan_c', name: 'Plan C', description: 'C', selfService: false, monthlyPrice: 99 },
  ];
  const cards = derivePlanCards('plan_a', ['plan_b', 'plan_c', 'missing'], catalog);
  assert.deepEqual(cards.map((card) => ({ key: card.key, role: card.role, actionLabel: card.actionLabel })), [
    { key: 'plan_a', role: 'current', actionLabel: '' },
    { key: 'plan_b', role: 'action', actionLabel: 'Upgrade' },
  ]);
  const switchCards = derivePlanCards('plan_b', ['plan_a'], catalog);
  assert.equal(switchCards[1].actionLabel, 'Switch');
});

test('derivePlanCards: monthlyPrice null shows no price on the card data', () => {
  const cards = derivePlanCards('plan_a', ['plan_b'], [
    { key: 'plan_a', name: 'Plan A', description: 'A', selfService: true, monthlyPrice: null },
    { key: 'plan_b', name: 'Plan B', description: 'B', selfService: true, monthlyPrice: null },
  ]);
  assert.equal(cards[0].monthlyPrice, null);
  assert.equal(cards[1].monthlyPrice, null);
  assert.equal(cards[1].actionLabel, 'Switch');
});
