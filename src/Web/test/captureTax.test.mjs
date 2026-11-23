import assert from 'node:assert/strict';
import test from 'node:test';
import {
  convertInclusiveLinesToNet,
  isTaxOnlyDifference,
  matchCaptureTaxRate,
  planCaptureTax,
  spreadTaxIntoLineCosts,
} from '../src/components/document-capture/fields.ts';

const tax = (id, rate, extra = {}) => ({ id, taxCode: `T${id}`, taxName: `Rate ${rate}`, rate, isActive: true, ...extra });
const cents = (n) => Math.round(n * 100);
const lineTotal = (l, unit) => Math.round(l.quantity * unit * (1 - l.discount / 100) * 100) / 100;

test('T-09 matches an exclusive 7% document to the one 7% rate', () => {
  const m = matchCaptureTaxRate({ subtotal: '100.00', taxTotal: '7.00', grandTotal: '107.00', priceBasis: 'exclusive' }, [], [tax('a', 7), tax('b', 10)]);
  assert.equal(m.kind, 'single');
  assert.equal(m.tax.id, 'a');
});

test('T-09 matches an exclusive 8.25% document', () => {
  const m = matchCaptureTaxRate({ subtotal: '407.47', taxTotal: '33.62', grandTotal: '441.09' }, [], [tax('a', 7), tax('c', 8.25)]);
  assert.equal(m.kind, 'single');
  assert.equal(m.tax.id, 'c');
});

test('T-09 derives the rate from the gross for inclusive documents', () => {
  const m = matchCaptureTaxRate({ subtotal: '107.00', taxTotal: '7.00', grandTotal: '107.00', priceBasis: 'inclusive' }, [], [tax('a', 7)]);
  assert.equal(m.kind, 'single');
  assert.equal(m.tax.id, 'a');
});

test('T-09 reports none when no active rate is close', () => {
  const m = matchCaptureTaxRate({ subtotal: '100.00', taxTotal: '12.00', grandTotal: '112.00' }, [], [tax('a', 7), tax('b', 10), tax('z', 12, { isActive: false })]);
  assert.equal(m.kind, 'none');
});

test('T-09 reports ambiguous when two rates are within tolerance', () => {
  const m = matchCaptureTaxRate({ subtotal: '100.00', taxTotal: '7.00', grandTotal: '107.00' }, [], [tax('a', 7), tax('b', 7.03)]);
  assert.equal(m.kind, 'ambiguous');
  assert.equal(m.taxes.length, 2);
});

test('T-09 does not auto-assign when scanned lines carry different tax hints', () => {
  const m = matchCaptureTaxRate(
    { subtotal: '100.00', taxTotal: '7.00', grandTotal: '107.00' },
    [{ taxHint: '7%' }, { taxHint: 'exempt' }],
    [tax('a', 7)]
  );
  assert.equal(m.kind, 'ambiguous');
});

test('T-09 is not applicable when the document has no tax', () => {
  assert.equal(matchCaptureTaxRate({ subtotal: '100.00', taxTotal: '0.00', grandTotal: '100.00' }, [], [tax('a', 7)]).kind, 'not-applicable');
  assert.equal(matchCaptureTaxRate({ subtotal: '100.00', grandTotal: '100.00' }, [], [tax('a', 7)]).kind, 'not-applicable');
});

test('T-10 spreads the tax pro rata so the lines add up to the grand total to the cent', () => {
  const lines = [
    { quantity: 1, amount: 244.48, discount: 0 },
    { quantity: 1, amount: 162.99, discount: 0 },
  ];
  const units = spreadTaxIntoLineCosts(lines, 33.62);
  assert.ok(units);
  const total = lines.reduce((sum, l, i) => sum + cents(lineTotal(l, units[i])), 0);
  assert.equal(total, cents(441.09));
});

test('T-10 puts the leftover cent on the largest line', () => {
  const lines = [
    { quantity: 1, amount: 10.0, discount: 0 },
    { quantity: 1, amount: 10.0, discount: 0 },
    { quantity: 1, amount: 30.0, discount: 0 },
  ];
  // 1.00 tax: shares 0.20 + 0.20 + 0.60 are exact, so use 1.01: 0.2020 -> 0.20, 0.20, 0.6060 -> 0.61, leftover 0.00.
  // 0.02 tax: 0.004 -> 0.00, 0.00, 0.012 -> 0.01, leftover 0.01 goes to the largest line.
  const units = spreadTaxIntoLineCosts(lines, 0.02);
  assert.ok(units);
  assert.deepEqual(
    units.map((u, i) => cents(lineTotal(lines[i], u))),
    [1000, 1000, 3002]
  );
});

test('T-10 keeps quantity and discount and still reproduces the total', () => {
  const lines = [
    { quantity: 3, amount: 19.99, discount: 10 },
    { quantity: 7, amount: 4.35, discount: 0 },
  ];
  const net = lines.reduce((s, l) => s + cents(lineTotal(l, l.amount)), 0);
  const units = spreadTaxIntoLineCosts(lines, 9.17);
  assert.ok(units);
  const total = lines.reduce((s, l, i) => s + cents(lineTotal(l, units[i])), 0);
  assert.equal(total, net + 917);
});

test('T-10 refuses to spread over lines with no usable amount', () => {
  assert.equal(spreadTaxIntoLineCosts([{ quantity: 1, amount: null, discount: 0 }], 5), null);
});

test('T-10 converts inclusive 7% lines to net so net plus per-line tax equals the grand total', () => {
  const lines = [
    { quantity: 1, amount: 53.5, discount: 0 },
    { quantity: 2, amount: 26.75, discount: 0 },
    { quantity: 1, amount: 10.7, discount: 0 },
  ];
  const grand = 117.7;
  const units = convertInclusiveLinesToNet(lines, 7, grand);
  assert.ok(units);
  const total = lines.reduce((s, l, i) => {
    const net = lineTotal(l, units[i]);
    return s + cents(net) + cents(Math.round(net * 7) / 100);
  }, 0);
  assert.equal(total, cents(grand));
});

test('planCaptureTax: VatGst single match sets the tax id; SalesTax and None add to cost', () => {
  const totals = { subtotal: '407.47', taxTotal: '33.62', grandTotal: '441.09', priceBasis: 'exclusive' };
  const lines = [
    { quantity: 1, amount: 244.48, discount: 0 },
    { quantity: 1, amount: 162.99, discount: 0 },
  ];
  const taxes = [tax('c', 8.25)];
  const vat = planCaptureTax('VatGst', totals, [], lines, taxes);
  assert.equal(vat.taxId, 'c');
  assert.equal(vat.treatment.kind, 'tax-rate');
  assert.equal(vat.unitAmounts, null);

  for (const regime of ['SalesTax', 'None']) {
    const p = planCaptureTax(regime, totals, [], lines, taxes);
    assert.equal(p.taxId, '');
    assert.equal(p.treatment.kind, 'added-to-cost');
    assert.equal(p.treatment.unregistered, regime === 'None');
    assert.ok(p.unitAmounts);
  }

  assert.equal(planCaptureTax('VatGst', totals, [], lines, []).treatment.kind, 'needs-rate');
  assert.equal(planCaptureTax(null, totals, [], lines, taxes).treatment.kind, 'unresolved');
  assert.equal(planCaptureTax('VatGst', { subtotal: '10', taxTotal: '0', grandTotal: '10' }, [], lines, taxes).treatment.kind, 'none');
});

test('isTaxOnlyDifference is true only when the bill is short by the document tax', () => {
  const totals = { subtotal: '407.47', taxTotal: '33.62', grandTotal: '441.09' };
  assert.equal(isTaxOnlyDifference(totals, -33.62), true);
  assert.equal(isTaxOnlyDifference(totals, -33.61), true);
  assert.equal(isTaxOnlyDifference(totals, -10), false);
  assert.equal(isTaxOnlyDifference(totals, 33.62), false);
  assert.equal(isTaxOnlyDifference(totals, null), false);
  assert.equal(isTaxOnlyDifference({ taxTotal: '0.00' }, 0), false);
});
