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

/** 1 MoneyIn, 2 MoneyOut, 3 Transfer, 4 Fee, 5 Interest, 6 Adjustment, 7 OpeningBalance */
export type BankTransactionKind = 1 | 2 | 3 | 4 | 5 | 6 | 7;

export const BANK_TRANSACTION_KIND_LABELS: Record<number, string> = {
  1: 'Money in',
  2: 'Money out',
  3: 'Transfer',
  4: 'Fee',
  5: 'Interest',
  6: 'Adjustment',
  7: 'Opening balance',
};

export interface BankTransaction {
  id: number;
  kind: number;
  kindLabel: string;
  bankId: number;
  bankName?: string | null;
  toBankId?: number | null;
  toBankName?: string | null;
  date: string;
  amount: number;
  contraAccountId?: number | null;
  contraAccountCode?: string | null;
  contraAccountName?: string | null;
  partyId?: number | null;
  partyName?: string | null;
  no: string;
  referenceNo?: string | null;
  memo?: string | null;
  generalLedgerHeaderId?: number | null;
  isVoid: boolean;
  voidGeneralLedgerHeaderId?: number | null;
}

export interface CreateBankTransactionPayload {
  kind: number;
  bankId: number;
  toBankId?: number | null;
  date: string;
  amount: number;
  contraAccountId?: number | null;
  partyId?: number | null;
  referenceNo?: string | null;
  memo?: string | null;
  increaseBalance?: boolean | null;
}

export interface BankTransactionList {
  transactions: BankTransaction[];
}

export interface BankReconciliationCandidateLine {
  lineId: number;
  headerId: number;
  date: string;
  type: string;
  payee?: string | null;
  reference?: string | null;
  in: number;
  out: number;
  ticked: boolean;
  isOpeningBalance: boolean;
}

export interface BankReconciliationListItem {
  id: number;
  bankId: number;
  bankName?: string | null;
  statementEndDate: string;
  statementEndingBalance: number;
  beginningBalance: number;
  status: number;
  statusLabel: string;
  finishedAt?: string | null;
  finishedBy?: string | null;
  reviewedAt?: string | null;
  reviewedBy?: string | null;
  version: number;
}

export interface BankReconciliation {
  id: number;
  bankId: number;
  bankName?: string | null;
  statementEndDate: string;
  statementEndingBalance: number;
  beginningBalance: number;
  status: number;
  statusLabel: string;
  version: number;
  clearedDeposits: number;
  clearedPayments: number;
  clearedBalance: number;
  difference: number;
  createdBy?: string | null;
  createdAt: string;
  finishedBy?: string | null;
  finishedAt?: string | null;
  reviewedBy?: string | null;
  reviewedAt?: string | null;
  deposits: BankReconciliationCandidateLine[];
  payments: BankReconciliationCandidateLine[];
}

export const BANK_RECONCILIATION_STATUS = {
  Draft: 1,
  Finished: 2,
  Undone: 3,
} as const;

export interface OutstandingAgingBucket {
  label: string;
  minDays: number;
  maxDays?: number | null;
  count: number;
  depositsInTransit: number;
  outstandingPayments: number;
  net: number;
  isStale: boolean;
}

export interface OutstandingAging {
  bankId: number;
  asOfDate: string;
  buckets: OutstandingAgingBucket[];
  totalCount: number;
  totalDepositsInTransit: number;
  totalOutstandingPayments: number;
  staleCount: number;
  hasStale: boolean;
}

export interface BankCloseStatusBank {
  bankId: number;
  name: string;
  reconciledThrough?: string | null;
  isReconciled: boolean;
}

export interface BankCloseStatus {
  periodEnd: string;
  periodLabel: string;
  reconciledCount: number;
  totalCount: number;
  isComplete: boolean;
  banks: BankCloseStatusBank[];
}
