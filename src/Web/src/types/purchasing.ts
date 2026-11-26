import type { InvoiceStatus } from './invoicing';
import type { PartyBase } from './parties';
import type { BillSourceDocumentType } from '../lib/billSourceDocument';

export type { BillSourceDocumentType };

/** A vendor: the party a bill is from (api/Purchasing/Vendor). */
export interface Vendor extends PartyBase {
  /** Date of its latest approved bill. */
  lastBillDate?: string | null;
}

/** A bill shows the same statuses an invoice does. */
export type BillStatus = InvoiceStatus;

export interface BillLine {
  id: string;
  itemId?: string | null;
  description?: string | null;
  /** The account the line is booked to. */
  accountId?: string | null;
  accountName?: string | null;
  isStockItem?: boolean;
  quantity: number;
  /** Unit cost. */
  amount: number;
  /** Percent off. */
  discount?: number | null;
  taxId?: string | null;
  taxAmount: number;
  subtotal: number;
}

export interface BillPayment {
  id: string;
  no?: string | null;
  date: string;
  amount: number;
  accountId?: string | null;
  /** The cash or bank account it was paid from. */
  accountName?: string | null;
  paymentMethod?: string | null;
  referenceNo?: string | null;
  memo?: string | null;
  isVoid?: boolean;
  voidedBy?: string | null;
  voidedAt?: string | null;
  voidReason?: string | null;
}

/** A vendor's bill (api/Purchasing/Bill). */
export interface Bill {
  id: string;
  /** The workspace's own number, given when the bill is approved. */
  no?: string | null;
  vendorId: string;
  vendorName?: string | null;
  vendorEmail?: string | null;
  vendorPhone?: string | null;
  vendorAddress?: string | null;
  /** The vendor's own number for the bill. */
  vendorInvoiceNo?: string | null;
  date: string;
  dueDate?: string | null;
  paymentTermId?: string | null;
  referenceNo?: string | null;
  notes?: string | null;
  /** Primary source document classification chosen during draft entry (`unspecified`, `supplier-invoice`, `expense-receipt`). */
  sourceDocumentType?: BillSourceDocumentType | null;
  lines: BillLine[];
  subtotal: number;
  totalTax: number;
  total: number;
  amountPaid: number;
  creditedAmount?: number;
  advanceAppliedAmount?: number;
  balanceDue: number;
  settlementSummary?: string | null;
  version?: number;
  status: BillStatus;
  approved: boolean;
  approvedBy?: string | null;
  approvedAt?: string | null;
  voidedBy?: string | null;
  voidedAt?: string | null;
  voidReason?: string | null;
  confirmDuplicate?: boolean;
  payments: BillPayment[];
}

/** An account a bill line can be booked to, or a bill paid from. */
export interface BillAccount {
  id: string;
  code: string;
  name: string;
  group: 'Expense' | 'Asset';
  isInventoryAccount?: boolean;
}
