import assert from 'node:assert/strict';
import { after, test } from 'node:test';
import { createServer } from 'vite';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

const server = await createServer({ server: { middlewareMode: true, hmr: false, ws: false }, appType: 'custom' });
after(() => server.close());

const { UsageHint } = await server.ssrLoadModule('/src/components/UsageHint.tsx');
const { PlanLimitDialogView } = await server.ssrLoadModule('/src/components/PlanLimitDialog.tsx');
const { IntentStep } = await server.ssrLoadModule('/src/pages/CreateOrg/steps/IntentStep.tsx');
const { primePlanCatalog } = await server.ssrLoadModule('/src/lib/usePlanCatalog.ts');
const { AuthProvider } = await server.ssrLoadModule('/src/auth/AuthContext.tsx');
const { announceEntitlementDenial, isEntitlementProblem, ENTITLEMENT_DENIED_EVENT } =
  await server.ssrLoadModule('/src/lib/entitlementEvents.ts');
const {
  evaluateCanStatementImport,
  FEATURE_BANKING_STATEMENT_IMPORT,
} = await server.ssrLoadModule('/src/lib/entitlements.ts');

const limited = (salesDocuments) => ({
  enabled: true, plan: 'invoicing', tier: 'free', limited: true, modules: ['sales', 'purchases'],
  selfServicePlans: ['invoicing', 'accounting'],
  limits: { salesDocuments, bills: { limit: 5, used: 0, period: 'month' } },
  upgrade: { available: false, contact: 'support@example.com' },
});

test('usage hint shows used of limit for limited workspaces only', () => {
  const html = renderToStaticMarkup(createElement(UsageHint, {
    entitlements: limited({ limit: 5, used: 3, period: 'month' }), resource: 'salesDocuments',
  }));
  assert.ok(html.includes('3 of 5 invoices &amp; quotes used this month'));
  assert.match(html, /role="progressbar"[^>]*aria-valuenow="3"/);
  assert.ok(!html.includes('limit reached'));

  const full = renderToStaticMarkup(createElement(UsageHint, {
    entitlements: limited({ limit: 5, used: 5, period: 'month' }), resource: 'salesDocuments',
  }));
  assert.ok(full.includes('Free plan limit reached'));

  const selfHost = { ...limited({ limit: null, used: 9, period: 'month' }), enabled: false, limited: false };
  assert.equal(renderToStaticMarkup(createElement(UsageHint, { entitlements: selfHost, resource: 'salesDocuments' })), '');
  assert.equal(renderToStaticMarkup(createElement(UsageHint, { entitlements: null, resource: 'bills' })), '');
});

test('plan limit dialog explains the denial and offers support when manager cannot upgrade', () => {
  const html = renderToStaticMarkup(createElement(PlanLimitDialogView, {
    problem: {
      code: 'QUOTA_EXCEEDED', detail: 'The Free plan includes 5 invoices and quotes per month.',
      limit: 5, used: 5, period: 'month', contact: 'support@example.com',
      canManageBilling: true, upgradeAvailable: false,
    },
    onClose: () => {},
  }));
  assert.match(html, /role="dialog"[^>]*aria-modal="true"/);
  assert.ok(html.includes('Free plan limit reached'));
  assert.ok(html.includes('5 of 5 used this month.'));
  assert.ok(html.includes('Paid plans are coming soon.'));
  assert.ok(html.includes('href="mailto:support@example.com"'));
  assert.ok(html.includes('View plan usage'));
});

test('subscription inactive dialog uses update billing CTA for billing managers', () => {
  assert.equal(isEntitlementProblem({ code: 'SUBSCRIPTION_INACTIVE' }), true);
  const html = renderToStaticMarkup(createElement(PlanLimitDialogView, {
    problem: {
      code: 'SUBSCRIPTION_INACTIVE',
      detail: "This workspace's subscription is inactive. Update billing to restore paid modules.",
      canManageBilling: true, upgradeAvailable: true,
    },
    onClose: () => {},
  }));
  assert.ok(html.includes('Subscription inactive'));
  assert.ok(html.includes('Update billing'));
  assert.ok(!html.includes('Paid plans are coming soon.'));
});

test('only plan denials are announced, without consuming the response', async () => {
  assert.equal(isEntitlementProblem({ code: 'QUOTA_EXCEEDED' }), true);
  assert.equal(isEntitlementProblem({ code: 'PERMISSION_DENIED' }), false);

  const seen = [];
  globalThis.window ??= new EventTarget();
  const listener = (event) => seen.push(event.detail.code);
  window.addEventListener(ENTITLEMENT_DENIED_EVENT, listener);
  try {
    const problem = (code) => new Response(JSON.stringify({ code }), { status: 403, headers: { 'content-type': 'application/problem+json' } });
    const denied = problem('PLAN_NOT_AVAILABLE');
    await announceEntitlementDenial(denied);
    await announceEntitlementDenial(problem('PERMISSION_DENIED'));
    await announceEntitlementDenial(new Response('{"code":"QUOTA_EXCEEDED"}', { status: 400, headers: { 'content-type': 'application/json' } }));
    assert.deepEqual(seen, ['PLAN_NOT_AVAILABLE']);
    assert.equal((await denied.json()).code, 'PLAN_NOT_AVAILABLE');
  } finally {
    window.removeEventListener(ENTITLEMENT_DENIED_EVENT, listener);
  }
});

test('create-workspace wizard hides plans the operator assigns', () => {
  const plan = (key, selfService) => ({ key, name: key, description: '', includedModules: [], requiredModules: [], selfService, monthlyPrice: 0 });
  const render = () => renderToStaticMarkup(createElement(AuthProvider, null,
    createElement(IntentStep, { plan: 'accounting', onChange: () => {} })));

  // IntentStep option values are fixed; the catalog only filters which stay visible.
  primePlanCatalog([plan('invoicing', true), plan('accounting', true), plan('enterprise', false)]);
  const restricted = render();
  assert.ok(restricted.includes('value="invoicing"') && restricted.includes('value="accounting"'));
  assert.ok(!restricted.includes('value="enterprise"'));

  primePlanCatalog([plan('invoicing', true), plan('accounting', true), plan('enterprise', true)]);
  assert.ok(render().includes('value="enterprise"'));
  primePlanCatalog(null);
});

test('evaluateCanStatementImport allows financials, feature key, or community', () => {
  const base = {
    ...limited({ limit: 5, used: 0, period: 'month' }),
    modules: ['sales', 'purchases'],
    features: [],
    // Avoid hosted plan-key tokens in the public tree (PlanPackagingLeakGuardTests).
    effectivePlan: 'starter',
    plan: 'starter',
    readOnlyModules: [],
    allowedTransitions: [],
    lifecycle: null,
    version: null,
  };
  assert.equal(evaluateCanStatementImport(base), false);
  assert.equal(
    evaluateCanStatementImport({ ...base, features: [FEATURE_BANKING_STATEMENT_IMPORT] }),
    true,
  );
  assert.equal(
    evaluateCanStatementImport({
      ...base,
      limited: false,
      effectivePlan: 'community',
      plan: 'community',
    }),
    true,
  );
  assert.equal(evaluateCanStatementImport(null, ['financials']), true);
});
