import assert from 'node:assert/strict';
import test from 'node:test';
import { featureAvailabilityText, formatPlanList, planNamesWithFeature } from '../src/lib/planFeatures.ts';

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
