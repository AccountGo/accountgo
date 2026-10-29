import type { CreateBankAccountPayload, UnassignedCash } from '../types/banking';

/** Pure helpers for banking create flow (Vitest / node:test friendly). */

export function pickDefaultReceiptBankId(
  items: Array<{ id: number; isDefaultForReceipts: boolean }>,
): number | null {
  const banks = items.filter((i) => i.id > 0);
  if (banks.length === 0) return null;
  const preferred = banks.find((i) => i.isDefaultForReceipts);
  return (preferred ?? banks[0]).id;
}

export function pickDefaultPaymentBankId(
  items: Array<{ id: number; isDefaultForPayments: boolean }>,
): number | null {
  const banks = items.filter((i) => i.id > 0);
  if (banks.length === 0) return null;
  const preferred = banks.find((i) => i.isDefaultForPayments);
  return (preferred ?? banks[0]).id;
}

/** When CashHistory has 2+ groups, the first create must assign each group (or leave unassigned). */
export function needsCashHistoryStep(groups: UnassignedCash[]): boolean {
  return groups.length >= 2;
}

export function buildCreatePayload(input: {
  name: string;
  type: number;
  bankName?: string;
  accountNumber?: string;
  routingNumber?: string;
  iban?: string;
  swift?: string;
  isDefaultForReceipts?: boolean;
  isDefaultForPayments?: boolean;
  openingBalance?: string;
  openingBalanceDate?: string;
  adoptAccountId?: number | null;
  postingAccountId?: number | null;
}): CreateBankAccountPayload {
  const name = input.name.trim();
  if (!name) throw new Error('Name is required.');

  const payload: CreateBankAccountPayload = {
    name,
    type: input.type,
    bankName: input.bankName?.trim() || null,
    accountNumber: input.accountNumber?.trim() || null,
    routingNumber: input.routingNumber?.trim() || null,
    iban: input.iban?.trim() || null,
    swift: input.swift?.trim() || null,
    isDefaultForReceipts: !!input.isDefaultForReceipts,
    isDefaultForPayments: !!input.isDefaultForPayments,
  };

  if (input.adoptAccountId != null) payload.adoptAccountId = input.adoptAccountId;
  if (input.postingAccountId != null) payload.postingAccountId = input.postingAccountId;

  const rawAmount = input.openingBalance?.trim();
  if (rawAmount) {
    const amount = Number(rawAmount);
    if (Number.isNaN(amount)) throw new Error('Opening balance must be a number.');
    payload.openingBalance = amount;
    payload.openingBalanceDate = input.openingBalanceDate || null;
  }

  return payload;
}
