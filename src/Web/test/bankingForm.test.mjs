import test from 'node:test';
import assert from 'node:assert/strict';
import {
  buildCreatePayload,
  needsCashHistoryStep,
  pickDefaultPaymentBankId,
  pickDefaultReceiptBankId,
} from '../src/lib/bankingForm.ts';

test('pickDefaultReceiptBankId prefers the receipt default', () => {
  assert.equal(
    pickDefaultReceiptBankId([
      { id: 1, isDefaultForReceipts: false },
      { id: 2, isDefaultForReceipts: true },
    ]),
    2,
  );
});

test('pickDefaultReceiptBankId falls back to the first real bank', () => {
  assert.equal(
    pickDefaultReceiptBankId([
      { id: 0, isDefaultForReceipts: true },
      { id: 7, isDefaultForReceipts: false },
    ]),
    7,
  );
});

test('pickDefaultPaymentBankId prefers the payment default', () => {
  assert.equal(
    pickDefaultPaymentBankId([
      { id: 3, isDefaultForPayments: false },
      { id: 4, isDefaultForPayments: true },
    ]),
    4,
  );
});

test('pickDefaultPaymentBankId returns null when only other-cash placeholders exist', () => {
  assert.equal(pickDefaultPaymentBankId([{ id: 0, isDefaultForPayments: false }]), null);
});

test('needsCashHistoryStep is true only for two or more unassigned groups', () => {
  assert.equal(needsCashHistoryStep([]), false);
  assert.equal(
    needsCashHistoryStep([{ accountId: 1, description: 'a', receiptCount: 0, paymentCount: 0, otherLineCount: 0, balance: 0 }]),
    false,
  );
  assert.equal(
    needsCashHistoryStep([
      { accountId: 1, description: 'a', receiptCount: 0, paymentCount: 0, otherLineCount: 0, balance: 0 },
      { accountId: 2, description: 'b', receiptCount: 0, paymentCount: 0, otherLineCount: 0, balance: 0 },
    ]),
    true,
  );
});

test('buildCreatePayload trims fields and includes opening balance when set', () => {
  const payload = buildCreatePayload({
    name: '  Operating  ',
    type: 1,
    bankName: '  First  ',
    openingBalance: '1250.50',
    openingBalanceDate: '2026-01-01',
    adoptAccountId: 11,
  });
  assert.equal(payload.name, 'Operating');
  assert.equal(payload.bankName, 'First');
  assert.equal(payload.openingBalance, 1250.5);
  assert.equal(payload.openingBalanceDate, '2026-01-01');
  assert.equal(payload.adoptAccountId, 11);
});

test('buildCreatePayload refuses an empty name and a non-numeric opening balance', () => {
  assert.throws(() => buildCreatePayload({ name: '  ', type: 1 }), /Name is required/);
  assert.throws(
    () => buildCreatePayload({ name: 'Cash', type: 1, openingBalance: 'nope' }),
    /Opening balance must be a number/,
  );
});
