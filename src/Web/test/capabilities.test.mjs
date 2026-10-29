import assert from 'node:assert/strict';
import { after, test } from 'node:test';
import { createServer } from 'vite';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

const server = await createServer({ server: { middlewareMode: true, hmr: false, ws: false }, appType: 'custom' });
after(() => server.close());

const {
  normalizeEntitlementsPayload,
  evaluateCanModule,
  evaluateCanWrite,
  evaluateCanFeature,
  evaluateCanCreate,
  evaluateCanCreateAction,
  CREATE_RESOURCE_MODULE,
} = await server.ssrLoadModule('/src/lib/entitlements.ts');

const {
  adoptEntitlementsVersion,
  observeEntitlementsVersionHeader,
  registerCapabilitiesReloader,
  resetEntitlementVersionMonitorForTests,
  getCachedEntitlementsVersionForTests,
} = await server.ssrLoadModule('/src/lib/entitlementVersion.ts');

const {
  PlanLimitDialogView,
  planLimitCta,
} = await server.ssrLoadModule('/src/components/PlanLimitDialog.tsx');

const { ENTITLEMENT_CODES } = await server.ssrLoadModule('/src/lib/entitlementEvents.ts');

const snapshot = (overrides = {}) => normalizeEntitlementsPayload({
  enabled: true,
  plan: 'plan_a',
  effectivePlan: 'plan_a',
  tier: 'free',
  limited: true,
  modules: ['sales', 'purchases', 'financials'],
  readOnlyModules: ['reports'],
  features: ['mfa'],
  selfServicePlans: ['plan_a'],
  allowedTransitions: ['plan_b'],
  limits: {
    salesDocuments: { limit: 5, used: 5, period: 'month', overLimit: false },
    bills: { limit: 5, used: 0, period: 'month', overLimit: false },
    seats: { limit: 1, used: 1, period: null, overLimit: false },
  },
  version: 'v1',
  upgrade: { available: false, contact: 'support@example.com' },
  ...overrides,
});

test('normalize treats missing features/readOnlyModules/allowedTransitions as []', () => {
  const normalized = normalizeEntitlementsPayload({
    enabled: true, plan: 'accounting', tier: 'free', limited: false, modules: ['sales'],
  });
  assert.deepEqual(normalized.features, []);
  assert.deepEqual(normalized.readOnlyModules, []);
  assert.deepEqual(normalized.allowedTransitions, []);
});

test('canModule uses bootstrap only before a snapshot exists', () => {
  assert.equal(evaluateCanModule(null, 'sales', ['sales']), true);
  assert.equal(evaluateCanModule(null, 'financials', ['sales']), false);
  const snap = snapshot({ modules: ['purchases'], readOnlyModules: ['sales'] });
  assert.equal(evaluateCanModule(snap, 'sales', ['financials']), true); // snapshot wins; bootstrap ignored
  assert.equal(evaluateCanModule(snap, 'financials', ['financials']), false);
});

test('canWrite is false for read-only modules; canFeature reads features[]', () => {
  const snap = snapshot();
  assert.equal(evaluateCanWrite(snap, 'sales'), true);
  assert.equal(evaluateCanWrite(snap, 'reports'), false);
  assert.equal(evaluateCanModule(snap, 'reports'), true);
  assert.equal(evaluateCanFeature(snap, 'mfa'), true);
  assert.equal(evaluateCanFeature(snap, 'ai'), false);
  assert.equal(evaluateCanFeature(null, 'mfa'), false); // features fail closed with no snapshot
});

test('canCreate: !limited || limit==null || (!overLimit && remaining > 0)', () => {
  assert.equal(evaluateCanCreate(snapshot({ limited: false }), 'salesDocuments'), true);
  assert.equal(evaluateCanCreate(snapshot({
    limits: { salesDocuments: { limit: null, used: 99, period: 'month', overLimit: false } },
  }), 'salesDocuments'), true);
  assert.equal(evaluateCanCreate(snapshot({
    limits: { salesDocuments: { limit: 5, used: 5, period: 'month', overLimit: false } },
  }), 'salesDocuments'), false);
  assert.equal(evaluateCanCreate(snapshot({
    limits: { salesDocuments: { limit: 5, used: 4, period: 'month', overLimit: false } },
  }), 'salesDocuments'), true);
  assert.equal(evaluateCanCreate(snapshot({
    limits: { salesDocuments: { limit: 5, used: 3, period: 'month', overLimit: true } },
  }), 'salesDocuments'), false);
});

test('create action requires permission ∧ canWrite(module) ∧ canCreate; seats have no module', () => {
  assert.equal(CREATE_RESOURCE_MODULE.salesDocuments, 'sales');
  assert.equal(CREATE_RESOURCE_MODULE.bills, 'purchases');
  assert.equal(CREATE_RESOURCE_MODULE.seats, undefined);

  const snap = snapshot({ readOnlyModules: ['sales'], modules: ['purchases', 'financials'] });
  assert.equal(evaluateCanCreateAction(snap, 'salesDocuments', true), false); // read-only sales
  assert.equal(evaluateCanCreateAction(snap, 'bills', true), true);
  assert.equal(evaluateCanCreateAction(snap, 'bills', false), false);
  assert.equal(evaluateCanCreateAction(snapshot({
    limits: { seats: { limit: 1, used: 1, period: null, overLimit: false } },
  }), 'seats', true), false);
  assert.equal(evaluateCanCreateAction(snapshot({
    limits: { seats: { limit: 3, used: 1, period: null, overLimit: false } },
  }), 'seats', true), true);
});

test('fail open with no snapshot: bootstrap modules + create enabled; features closed', () => {
  const bootstrap = ['sales', 'purchases', 'financials'];
  // (a) /api/entitlements 500 / never loaded → sidebar & RequiresModule still allow bootstrap modules
  assert.equal(evaluateCanModule(null, 'sales', bootstrap), true);
  assert.equal(evaluateCanWrite(null, 'sales', bootstrap), true);
  assert.equal(evaluateCanModule(null, 'inventory', bootstrap), false);
  // Invoices create button enabled for a user with permission
  assert.equal(evaluateCanCreate(null, 'salesDocuments'), true);
  assert.equal(evaluateCanCreateAction(null, 'salesDocuments', true, bootstrap), true);
  assert.equal(evaluateCanCreateAction(null, 'salesDocuments', false, bootstrap), false);
  assert.equal(evaluateCanFeature(null, 'mfa'), false);

  // (b) before the first response, create buttons are not disabled
  assert.equal(evaluateCanCreate(null, 'bills'), true);
  assert.equal(evaluateCanCreate(null, 'seats'), true);
});

test('after read-only snapshot, failed reload keeps writes disabled', () => {
  // (c) successful snapshot with sales read-only — a failed reload keeps this last good snapshot
  const lastGood = snapshot({
    modules: ['purchases', 'financials'],
    readOnlyModules: ['sales'],
  });
  assert.equal(evaluateCanModule(lastGood, 'sales', ['sales']), true);
  assert.equal(evaluateCanWrite(lastGood, 'sales', ['sales']), false);
  assert.equal(evaluateCanCreateAction(lastGood, 'salesDocuments', true, ['sales']), false);
});

test('version header: change → one reload; after org switch → no reload; no loop', async () => {
  resetEntitlementVersionMonitorForTests();
  let reloadCount = 0;
  registerCapabilitiesReloader(async () => {
    reloadCount += 1;
    return 'v2';
  });
  adoptEntitlementsVersion('acme', 'v1');

  observeEntitlementsVersionHeader('acme', 'acme', 'v2');
  await new Promise((r) => setTimeout(r, 0));
  assert.equal(reloadCount, 1);
  assert.equal(getCachedEntitlementsVersionForTests().version, 'v2');

  // Same version → no reload
  observeEntitlementsVersionHeader('acme', 'acme', 'v2');
  await new Promise((r) => setTimeout(r, 0));
  assert.equal(reloadCount, 1);

  // In-flight response for old org after switch → ignore
  adoptEntitlementsVersion('other', 'v9');
  observeEntitlementsVersionHeader('acme', 'other', 'v10');
  await new Promise((r) => setTimeout(r, 0));
  assert.equal(reloadCount, 1);
  assert.equal(getCachedEntitlementsVersionForTests().version, 'v9');

  // Triggering header already at new version; reload adopts it → no loop
  let second = 0;
  registerCapabilitiesReloader(async () => {
    second += 1;
    return 'v11';
  });
  adoptEntitlementsVersion('acme', 'v10');
  observeEntitlementsVersionHeader('acme', 'acme', 'v11');
  await new Promise((r) => setTimeout(r, 0));
  observeEntitlementsVersionHeader('acme', 'acme', 'v11');
  await new Promise((r) => setTimeout(r, 0));
  assert.equal(second, 1);

  resetEntitlementVersionMonitorForTests();
});

test('PlanLimitDialog CTA matrix: every ENTITLEMENT_CODES × canManageBilling × upgradeAvailable', () => {
  const href = '/org/acme/settings?tab=subscription';
  for (const code of ENTITLEMENT_CODES) {
    for (const canManageBilling of [true, false]) {
      for (const upgradeAvailable of [true, false]) {
        const cta = planLimitCta({ code, canManageBilling, upgradeAvailable }, href);
        if (!canManageBilling) {
          assert.equal(cta.label, 'Ask your workspace owner');
          assert.equal(cta.href, null);
          assert.equal(cta.primary, true);
        } else if (code === 'SUBSCRIPTION_INACTIVE') {
          assert.equal(cta.label, 'Update billing');
          assert.equal(cta.href, href);
          assert.equal(cta.primary, true);
        } else if (upgradeAvailable) {
          assert.equal(cta.label, 'Upgrade plan');
          assert.equal(cta.href, href);
          assert.equal(cta.primary, true);
        } else {
          assert.equal(cta.label, 'View plan usage');
          assert.equal(cta.href, href);
          assert.equal(cta.primary, false);
        }

        const html = renderToStaticMarkup(createElement(PlanLimitDialogView, {
          problem: { code, canManageBilling, upgradeAvailable, detail: `${code} detail` },
          onClose: () => {},
        }));
        assert.ok(html.includes(cta.label), `${code}/${canManageBilling}/${upgradeAvailable} missing CTA`);
      }
    }
  }
});
