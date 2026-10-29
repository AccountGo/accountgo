/** Bank accounts and register — mirrors src/Dto/Banking. */

export type BankAccountType = 1 | 2 | 3;

export interface BankAccount {
  id: number;
  name: string;
  type: number;
  accountNumberMasked?: string | null;
  accountNumber?: string | null;
  bankName?: string | null;
  routingNumber?: string | null;
  iban?: string | null;
  swift?: string | null;
  accountId?: number | null;
  accountCode?: string | null;
  accountName?: string | null;
  isDefaultForReceipts: boolean;
  isDefaultForPayments: boolean;
  bookBalance: number;
  reconciledThrough?: string | null;
  archivedAt?: string | null;
  openingBalance?: number | null;
  openingBalanceDate?: string | null;
  currencyCode?: string | null;
}

export interface BankAccountPickerItem {
  id: number;
  name: string;
  accountNumberMasked?: string | null;
  isDefaultForReceipts: boolean;
  isDefaultForPayments: boolean;
  group?: string | null;
  accountId?: number | null;
}

export interface UnassignedCash {
  accountId: number;
  accountCode?: string | null;
  accountName?: string | null;
  description: string;
  receiptCount: number;
  paymentCount: number;
  otherLineCount: number;
  balance: number;
}

export interface BankAccountsList {
  accounts: BankAccount[];
  unassignedCash: UnassignedCash[];
}

export interface CreateBankAccountPayload {
  name: string;
  type: number;
  bankName?: string | null;
  accountNumber?: string | null;
  routingNumber?: string | null;
  iban?: string | null;
  swift?: string | null;
  isDefaultForReceipts?: boolean;
  isDefaultForPayments?: boolean;
  openingBalance?: number | null;
  openingBalanceDate?: string | null;
  adoptAccountId?: number | null;
  postingAccountId?: number | null;
  currencyCode?: string | null;
}

export interface UpdateBankAccountPayload {
  name: string;
  type: number;
  bankName?: string | null;
  accountNumber?: string | null;
  routingNumber?: string | null;
  iban?: string | null;
  swift?: string | null;
  isDefaultForReceipts?: boolean;
  isDefaultForPayments?: boolean;
  postingAccountId?: number | null;
  currencyCode?: string | null;
  openingBalance?: number | null;
  openingBalanceDate?: string | null;
}

export interface BankRegisterSourceDoc {
  type: string;
  id: number;
  no?: string | null;
}

export interface BankRegisterLine {
  lineId: number;
  headerId: number;
  date: string;
  type: string;
  payee?: string | null;
  reference?: string | null;
  sourceDoc?: BankRegisterSourceDoc | null;
  in: number;
  out: number;
  balance: number;
  reconciliationId?: number | null;
}

export interface BankRegister {
  broughtForward: number;
  lines: BankRegisterLine[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface EligiblePostingAccount {
  id: number;
  code?: string;
  name?: string;
  accountCode?: string;
  accountName?: string;
}

export const BANK_TYPE_LABELS: Record<number, string> = {
  1: 'Checking',
  2: 'Savings',
  3: 'Cash on hand',
};

export function bankTypeLabel(type: number): string {
  return BANK_TYPE_LABELS[type] ?? `Type ${type}`;
}
