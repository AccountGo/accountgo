import assert from 'node:assert/strict';
import test from 'node:test';
import {
  anyPlanIncludesModule,
  featureAvailabilityText,
  firstPlanWithModule,
  formatPlanList,
  planNamesWithFeature,
  productGroups,
} from '../src/lib/planFeatures.ts';

const FEATURE = 'banking.statement_import';

/** Neutral fixture catalog — no hosted plan keys or names. */
const catalog = [
  { name: 'Basic', selfService: true, features: [] },
  { name: 'Plus', selfService: true, features: [FEATURE] },
  { name: 'Ledger', selfService: true, features: [FEATURE, 'mfa'] },
  { name: 'Custom', selfService: false, features: [FEATURE] },
];

test('names only self-service plans that include the feature, in catalog order', () => {
  assert.deepEqual(planNamesWithFeature(catalog, FEATURE), ['Plus', 'Ledger']);
});

test('formats plan lists for one, two and three names', () => {
  assert.equal(formatPlanList([]), '');
  assert.equal(formatPlanList(['Plus']), 'Plus');
  assert.equal(formatPlanList(['Plus', 'Ledger']), 'Plus and Ledger');
  assert.equal(formatPlanList(['A', 'B', 'C']), 'A, B and C');
});

test('availability text names catalog plans, or falls back when none list the feature', () => {
  assert.equal(featureAvailabilityText(catalog, FEATURE), 'Available on Plus and Ledger.');
  assert.equal(featureAvailabilityText(catalog, 'unknown.feature'), 'Available on a higher plan.');
  assert.equal(featureAvailabilityText(null, FEATURE), 'Available on a higher plan.');
});

const modulePlans = [
  { key: 'a', name: 'Basic', selfService: true, includedModules: ['sales'], product: 'docs', productLabel: 'Documents' },
  { key: 'b', name: 'Plus', selfService: true, includedModules: ['sales', 'purchases'], product: 'docs' },
  { key: 'c', name: 'Ledger', selfService: true, includedModules: ['sales', 'financials'], product: 'books', productLabel: 'Books' },
  { key: 'd', name: 'Custom', selfService: false, includedModules: ['sales', 'inventory'], product: 'books' },
];

test('first self-service plan with a module, in catalog order', () => {
  assert.equal(firstPlanWithModule(modulePlans, 'purchases')?.name, 'Plus');
  assert.equal(firstPlanWithModule(modulePlans, 'financials')?.name, 'Ledger');
  assert.equal(firstPlanWithModule(modulePlans, 'inventory'), null);
  assert.equal(firstPlanWithModule(null, 'sales'), null);
});

test('product groups keep catalog order and use the catalog label, else the group key', () => {
  assert.deepEqual(productGroups(modulePlans), [
    { product: 'docs', label: 'Documents' },
    { product: 'books', label: 'Books' },
  ]);
  assert.deepEqual(productGroups([{ key: 'x', name: 'X', selfService: true, product: 'solo' }]), [{ product: 'solo', label: 'solo' }]);
  assert.deepEqual(productGroups([{ key: 'community', name: 'Community', selfService: false }]), []);
});

test('any plan in a group includes a module', () => {
  assert.equal(anyPlanIncludesModule(modulePlans.filter((p) => p.product === 'books'), 'financials'), true);
  assert.equal(anyPlanIncludesModule(modulePlans.filter((p) => p.product === 'docs'), 'financials'), false);
});
