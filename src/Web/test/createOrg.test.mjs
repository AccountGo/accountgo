import assert from 'node:assert/strict';
import test from 'node:test';
import { fiscalYearPreview } from '../src/pages/CreateOrg/lib/fyPreview.ts';
import {
  basisForFramework, buildPayload, defaultState, effectiveInventoryMethod, inventoryOptions, slugify, validateAll, validateStep,
} from '../src/pages/CreateOrg/lib/policyDefaults.ts';

const today = new Date(2026, 9, 24);

test('fiscal year preview mirrors the API derivation', () => {
  assert.equal(fiscalYearPreview('2026-02-15', 'Calendar', null)?.label, 'FY2026 · 1 Jan 2026 – 31 Dec 2026 · Open');
  assert.equal(fiscalYearPreview('2026-05-01', 'AprMar', null)?.label, 'FY2026-27 · 1 Apr 2026 – 31 Mar 2027 · Open');
  assert.equal(fiscalYearPreview('2026-02-15', 'AprMar', null)?.code, 'FY2025-26');
  assert.equal(fiscalYearPreview('2026-10-24', 'Custom', 10)?.label, 'FY2026-27 · 1 Oct 2026 – 30 Sep 2027 · Open');
  assert.equal(fiscalYearPreview('2026-02-15', 'JulJun', null)?.end, '2026-06-30');
  assert.equal(fiscalYearPreview('2027-02-10', 'Custom', 3)?.end, '2027-02-28');
  assert.equal(fiscalYearPreview('2027-06-01', 'Custom', 3)?.end, '2028-02-29');
});

test('fiscal year preview is empty until the inputs are valid', () => {
  assert.equal(fiscalYearPreview('2026-02-30', 'Calendar', null), null);
  assert.equal(fiscalYearPreview('', 'Calendar', null), null);
  assert.equal(fiscalYearPreview('2026-02-15', 'Custom', null), null);
  assert.equal(fiscalYearPreview('2026-02-15', 'Custom', 13), null);
});

test('defaults match the legacy API defaults and prefill the identity step', () => {
  const state = defaultState(today, { name: 'Acme Studio' });
  assert.equal(state.slug, 'acme-studio');
  assert.equal(state.booksStartDate, '2026-10-24');
  assert.equal(state.reportingFramework, 'IfrsForSmes');
  assert.equal(state.taxRegime, 'None');
  assert.equal(slugify('Apex & Co. / Logistics!'), 'apex-co-logistics');
});

test('cash basis framework forces cash basis; every other framework is accrual', () => {
  assert.equal(basisForFramework('CashBasis'), 'Cash');
  for (const framework of ['IfrsForSmes', 'UsGaapPrivate', 'Ifrs']) assert.equal(basisForFramework(framework), 'Accrual');
});

test('inventory costing only applies to product, mixed or enterprise workspaces and never offers LIFO', () => {
  const state = defaultState(today, { name: 'Acme' });
  assert.equal(effectiveInventoryMethod(state), 'None');
  assert.equal(effectiveInventoryMethod({ ...state, industryTemplate: 'ProductRetail' }), 'WeightedAverage');
  assert.equal(effectiveInventoryMethod({ ...state, plan: 'enterprise' }), 'WeightedAverage');
  assert.equal(effectiveInventoryMethod({ ...state, industryTemplate: 'Mixed', inventoryCostingMethod: 'Fifo' }), 'Fifo');
  assert.deepEqual(inventoryOptions(), ['WeightedAverage', 'Fifo']);
});

test('hard gates block advancing until required fields are valid', () => {
  const state = defaultState(today);
  assert.ok(validateStep('identity', state, today).name);
  assert.deepEqual(validateStep('identity', { ...state, name: 'Acme', slug: 'acme' }, today), {});
  assert.ok(validateStep('moneyTime', { ...state, currency: 'US' }, today).currency);
  assert.ok(validateStep('moneyTime', { ...state, booksStartDate: '2026-13-01' }, today).booksStartDate);
  assert.ok(validateStep('moneyTime', { ...state, booksStartDate: '1850-01-01' }, today).booksStartDate);
  assert.ok(validateStep('moneyTime', { ...state, booksStartDate: '2028-01-01' }, today).booksStartDate);
  assert.ok(validateStep('moneyTime', { ...state, fiscalYearPattern: 'Custom' }, today).fiscalYearStartMonth);
  assert.deepEqual(validateAll({ ...state, name: 'Acme', slug: 'acme' }, today), {});
});

test('payload carries the full hard-gate DTO the API expects', () => {
  const payload = buildPayload({
    ...defaultState(today, { name: ' Acme Studio ' }),
    legalName: 'Acme Studio LLC',
    reportingFramework: 'CashBasis',
    fiscalYearPattern: 'Custom',
    fiscalYearStartMonth: 10,
    taxRegime: 'VatGst',
    industryTemplate: 'ProductRetail',
    currency: 'php',
  });
  assert.deepEqual(payload, {
    name: 'Acme Studio', slug: 'acme-studio', plan: 'accounting', currency: 'PHP', legalName: 'Acme Studio LLC',
    reportingFramework: 'CashBasis', accountingBasis: 'Cash', fiscalYearPattern: 'Custom', fiscalYearStartMonth: 10,
    booksStartDate: '2026-10-24', taxRegime: 'VatGst', industryTemplate: 'ProductRetail', inventoryCostingMethod: 'WeightedAverage',
  });
});
