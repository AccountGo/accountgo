import assert from 'node:assert/strict';
import test from 'node:test';
import { invoiceLineAmounts } from '../src/lib/invoiceTax.ts';

test('invoice tax is added on top of each rounded, discounted subtotal', () => {
  assert.deepEqual(invoiceLineAmounts(1, 100, 0, 12), { subtotal: 100, discount: 0, tax: 12 });
  assert.deepEqual(invoiceLineAmounts(1, 200, 10, 12), { subtotal: 180, discount: 20, tax: 21.6 });
  assert.deepEqual(invoiceLineAmounts(3, 0.05, 10, 12), { subtotal: 0.14, discount: 0.02, tax: 0.02 });
  assert.deepEqual(invoiceLineAmounts(0, 100, 0, 12), { subtotal: 0, discount: 0, tax: 0 });
  assert.deepEqual(invoiceLineAmounts(1, 100, 0, 0), { subtotal: 100, discount: 0, tax: 0 });
});

test('preview rounds decimal midpoints half away from zero, without binary floating-point drift', () => {
  assert.equal(invoiceLineAmounts(1, 2.3, 5, 12).subtotal, 2.19);
  assert.equal(invoiceLineAmounts(1, 2.5, 3, 12).subtotal, 2.43);
  assert.equal(invoiceLineAmounts(1, 2.65, 10, 12).subtotal, 2.39);
  assert.equal(invoiceLineAmounts(1, 2.9, 25, 12).subtotal, 2.18);
  assert.equal(invoiceLineAmounts(1, 0.05, 0, 10).tax, 0.01);
  assert.equal(invoiceLineAmounts(-1, 0.05, 0, 10).tax, -0.01);
});

test('preview handles fractional quantities, rates, large values and scientific notation', () => {
  assert.deepEqual(invoiceLineAmounts(2.5, 10, 12.5, 7.5), { subtotal: 21.88, discount: 3.13, tax: 1.64 });
  assert.deepEqual(invoiceLineAmounts(1e6, 1e6, 10, 12), { subtotal: 9e11, discount: 1e11, tax: 1.08e11 });
  assert.deepEqual(invoiceLineAmounts(1e-7, 100, 0, 12), { subtotal: 0, discount: 0, tax: 0 });
  assert.throws(() => invoiceLineAmounts(1, Infinity, 0, 12), /finite numbers/);
});

test('preview matches exact-cent expectations across prices and discounts', () => {
  for (let cents = 1; cents <= 20000; cents++) {
    for (const discount of [1, 3, 5, 7, 10, 12, 15, 25, 30, 33, 50]) {
      const subtotalCents = (BigInt(cents) * BigInt(100 - discount) + 50n) / 100n;
      const taxCents = (subtotalCents * 12n + 50n) / 100n;
      const actual = invoiceLineAmounts(1, cents / 100, discount, 12);
      assert.equal(actual.subtotal, Number(subtotalCents) / 100);
      assert.equal(actual.tax, Number(taxCents) / 100);
    }
  }
});
