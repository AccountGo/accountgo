import type { InvoiceStatus } from './invoicing';
import type { PartyBase } from './parties';

/** A vendor: the party a bill is from (api/Purchasing/Vendor). */
export interface Vendor extends PartyBase {
  /** Date of its latest approved bill. */
  lastBillDate?: string | null;
}

/** A bill shows the same statuses an invoice does. */
export type BillStatus = InvoiceStatus;

export interface BillLine {
  id: number;
  itemId?: number | null;
  description?: string | null;
  /** The account the line is booked to. */
  accountId?: number | null;
  accountName?: string | null;
  isStockItem?: boolean;
  quantity: number;
  /** Unit cost. */
  amount: number;
  /** Percent off. */
  discount?: number | null;
  taxId?: number | null;
  taxAmount: number;
  subtotal: number;
}

export interface BillPayment {
  id: number;
  no?: string | null;
  date: string;
  amount: number;
  accountId?: number | null;
  /** The cash or bank account it was paid from. */
  accountName?: string | null;
  paymentMethod?: string | null;
  referenceNo?: string | null;
  memo?: string | null;
}

/** A vendor's bill (api/Purchasing/Bill). */
export interface Bill {
  id: number;
  /** The workspace's own number, given when the bill is approved. */
  no?: string | null;
  vendorId: number;
  vendorName?: string | null;
  vendorEmail?: string | null;
  vendorPhone?: string | null;
  vendorAddress?: string | null;
  /** The vendor's own number for the bill. */
  vendorInvoiceNo?: string | null;
  date: string;
  dueDate?: string | null;
  paymentTermId?: number | null;
  referenceNo?: string | null;
  notes?: string | null;
  lines: BillLine[];
  subtotal: number;
  totalTax: number;
  total: number;
  amountPaid: number;
  balanceDue: number;
  status: BillStatus;
  approved: boolean;
  payments: BillPayment[];
}

/** An account a bill line can be booked to, or a bill paid from. */
export interface BillAccount {
  id: number;
  code: string;
  name: string;
  group: 'Expense' | 'Asset';
  isInventoryAccount?: boolean;
}
