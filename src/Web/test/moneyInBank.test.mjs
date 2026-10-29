import test from 'node:test';
import assert from 'node:assert/strict';
import { bankRegisterPath, moneyInBankRows, rowsMatchTotal } from '../src/lib/moneyInBank.ts';

const kpis = {
  moneyInBank: 1600,
  bankBalances: [
    { bankId: 4, name: 'Checking', balance: 1250.5 },
    { bankId: 9, name: 'Savings', balance: 300 },
  ],
  unassignedCash: 49.5,
};

test('builds one linked row per bank plus unassigned cash', () => {
  const rows = moneyInBankRows(kpis, 'acme');
  assert.deepEqual(rows.map((r) => r.path), ['/org/acme/banking/4', '/org/acme/banking/9', '/org/acme/banking']);
  assert.equal(rows.at(-1).key, 'unassigned');
});

test('rows add up to the headline total', () => {
  assert.ok(rowsMatchTotal(moneyInBankRows(kpis, 'acme'), kpis.moneyInBank));
  assert.ok(!rowsMatchTotal(moneyInBankRows(kpis, 'acme'), 1700));
});

test('omits unassigned cash when it is zero', () => {
  const rows = moneyInBankRows({ ...kpis, unassignedCash: 0 }, 'acme');
  assert.equal(rows.length, 2);
});

test('no rows when the caller cannot see bank accounts or data is missing', () => {
  assert.deepEqual(moneyInBankRows({ moneyInBank: 5, bankBalances: null }, 'acme'), []);
  assert.deepEqual(moneyInBankRows(undefined, 'acme'), []);
});

test('bankRegisterPath targets the list page without a bank id', () => {
  assert.equal(bankRegisterPath('acme'), '/org/acme/banking');
  assert.equal(bankRegisterPath('acme', 3), '/org/acme/banking/3');
});

test('lists an archived bank that still holds money, labelled, and the rows still add up', () => {
  const withArchived = {
    moneyInBank: 1800,
    bankBalances: [
      ...kpis.bankBalances,
      { bankId: 12, name: 'Old account', balance: 200, isArchived: true },
    ],
    unassignedCash: 49.5,
  };
  const rows = moneyInBankRows(withArchived, 'acme');
  assert.equal(rows.find((r) => r.key === 'bank-12').label, 'Old account (archived)');
  assert.equal(rows.find((r) => r.key === 'bank-4').label, 'Checking');
  assert.ok(rowsMatchTotal(rows, withArchived.moneyInBank));
});
