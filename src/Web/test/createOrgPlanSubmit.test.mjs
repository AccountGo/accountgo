import assert from 'node:assert/strict';
import test from 'node:test';
import {
  buildIntentPlanOptions,
  freeSlotBlockedMessage,
  freeSlotPlanError,
  isFreeSlotUsed,
  isPaidWizardPlan,
} from '../src/lib/createOrgPlanOptions.ts';
import {
  buildCreateReturnUrl,
  interpretCreateOrgResponse,
} from '../src/lib/createOrgSubmit.ts';

/** Neutral fixture catalog — no hosted plan keys or packaging tokens. */
const catalog = [
  {
    key: 'invoicing',
    name: 'Invoicing Free',
    product: 'invoicing',
    description: 'Free invoicing',
    monthlyPrice: 0,
    annualPrice: 0,
    selfService: true,
  },
  {
    key: 'accounting',
    name: 'Accounting Free',
    product: 'accounting',
    description: 'Free accounting',
    monthlyPrice: 0,
    annualPrice: 0,
    selfService: true,
  },
  {
    key: 'fixture_paid_a',
    name: 'Invoicing Plus',
    product: 'invoicing',
    description: 'Paid invoicing',
    monthlyPrice: 12,
    annualPrice: 120,
    selfService: true,
  },
  {
    key: 'fixture_paid_b',
    name: 'Accounting Plus',
    product: 'accounting',
    description: 'Paid accounting',
    monthlyPrice: 29,
    annualPrice: 290,
    selfService: true,
  },
  {
    key: 'fixture_assigned',
    name: 'Assigned Only',
    product: 'accounting',
    description: 'Operator-assigned',
    monthlyPrice: null,
    annualPrice: null,
    selfService: false,
  },
];

test('plan-step options are empty until the catalog loads', () => {
  assert.deepEqual(buildIntentPlanOptions(null, { used: false, freeWorkspaceName: null, freeWorkspaceSlug: null }), []);
});

test('plan-step hides non-self-service plans and takes prices from the catalog', () => {
  const options = buildIntentPlanOptions(catalog, {
    used: false,
    freeWorkspaceName: null,
    freeWorkspaceSlug: null,
  });
  assert.equal(options.some((o) => o.value === 'fixture_assigned'), false);
  assert.equal(options.some((o) => o.title === 'Assigned Only'), false);
  const paid = options.filter((o) => !o.free);
  assert.deepEqual(
    paid.map((o) => ({ value: o.value, badge: o.badge })),
    [
      { value: 'fixture_paid_a', badge: '$12/mo' },
      { value: 'fixture_paid_b', badge: '$29/mo' },
    ],
  );
  assert.ok(options.every((o) => o.free || o.value.startsWith('fixture_paid_')));
});

test('plan-step disables free cards when the free slot is used', () => {
  assert.equal(isFreeSlotUsed(1, 1), true);
  assert.equal(isFreeSlotUsed(0, 1), false);
  assert.equal(isFreeSlotUsed(2, null), false);

  const options = buildIntentPlanOptions(catalog, {
    used: true,
    freeWorkspaceName: 'Studio One',
    freeWorkspaceSlug: 'studio-one',
  });
  for (const opt of options.filter((o) => o.free)) {
    assert.equal(opt.disabled, true);
  }
  for (const opt of options.filter((o) => !o.free)) {
    assert.equal(opt.disabled, false);
  }
  assert.match(freeSlotBlockedMessage('Studio One'), /Studio One/);
  assert.equal(isPaidWizardPlan('invoicing', catalog), false);
  assert.equal(isPaidWizardPlan('fixture_paid_a', catalog), true);
});

test('plan step blocks Next on a free choice once the free slot is used', () => {
  const used = { used: true, freeWorkspaceName: 'Studio One', freeWorkspaceSlug: 'studio-one' };
  const open = { used: false, freeWorkspaceName: null, freeWorkspaceSlug: null };
  assert.equal(freeSlotPlanError('invoicing', catalog, used), 'Choose a paid plan to continue.');
  assert.equal(freeSlotPlanError('fixture_paid_a', catalog, used), null);
  assert.equal(freeSlotPlanError('invoicing', catalog, open), null);
  assert.equal(freeSlotPlanError('invoicing', null, used), null);
  assert.doesNotMatch(freeSlotBlockedMessage('Studio One', true), /upgrade/i);
});

test('review-step redirects when checkoutUrl is present', () => {
  const outcome = interpretCreateOrgResponse(
    201,
    { slug: 'acme', checkoutUrl: 'https://pay.example/session/abc', activationState: 'pending_checkout' },
    'acme',
  );
  assert.deepEqual(outcome, { kind: 'redirect-checkout', url: 'https://pay.example/session/abc' });
});

test('review-step goes to pending when checkoutError is present', () => {
  const outcome = interpretCreateOrgResponse(
    201,
    { slug: 'acme', activationState: 'pending_checkout', checkoutError: 'Checkout unavailable' },
    'acme',
  );
  assert.deepEqual(outcome, { kind: 'pending', slug: 'acme', checkoutError: 'Checkout unavailable' });
});

test('review-step returns to plan step on owned_workspaces quota denial', () => {
  const outcome = interpretCreateOrgResponse(
    403,
    {
      code: 'QUOTA_EXCEEDED',
      resource: 'owned_workspaces',
      detail: 'You already own a free workspace.',
    },
    'acme',
  );
  assert.deepEqual(outcome, {
    kind: 'quota-free-slot',
    detail: 'You already own a free workspace.',
  });
});

test('create returnUrl carries the billing interval and leaves the checkout result to billing', () => {
  const url = buildCreateReturnUrl('https://app.example', 'acme-studio', 'year');
  assert.equal(url, 'https://app.example/org/acme-studio?interval=year');
});
