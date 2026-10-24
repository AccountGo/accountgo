import test from 'node:test';
import assert from 'node:assert/strict';
import { buildChecklist } from '../src/lib/onboardingChecklist.ts';

const ids = (items) => items.map((i) => i.id);

test('accounting plan without tax lists bank, coa, business, invite', () => {
  assert.deepEqual(ids(buildChecklist({ plan: 'accounting', taxRegime: 'None', hasLegalName: false })), ['bank', 'coa', 'business', 'invite']);
});

test('tax posture adds the tax item first', () => {
  assert.equal(buildChecklist({ plan: 'accounting', taxRegime: 'VatGst', hasLegalName: false })[0].id, 'tax');
});

test('invoicing plan gets the shorter list', () => {
  assert.deepEqual(ids(buildChecklist({ plan: 'invoicing', taxRegime: 'None', hasLegalName: false })), ['business', 'invite']);
});

test('legal name marks the business item done', () => {
  const item = buildChecklist({ plan: 'accounting', taxRegime: 'None', hasLegalName: true }).find((i) => i.id === 'business');
  assert.equal(item.done, true);
});
