import type { PartyBase } from './parties';

export type InvoiceStatus = 'Draft' | 'Open' | 'Partially Paid' | 'Partially Settled' | 'Paid' | 'Settled' | 'Overdue' | 'Void';

export interface InvoiceLineItem {
  id?: string;
  itemId?: string | null;
  measurementId?: string | null;
  itemDescription?: string;
  measurementDescription?: string;
  quantity: number;
  amount: number;
  discount: number;
  lineTotal?: number;
  taxRate?: number;
  taxId?: string | null;
  taxAmount?: number;
  salesAccountId?: string | null;
  salesAccountCode?: string | null;
  salesAccountName?: string | null;
}

export interface InvoicePaymentItem {
  id: string;
  receiptNo?: string;
  date: string;
  amount: number;
  paymentMethod?: string;
  referenceNo?: string;
  notes?: string;
}

export interface Invoice {
  id: string;
  /** Null while a draft has no number; it is numbered when issued. */
  no: string | null;
  customerId: string;
  customerName: string;
  /** Attention line under the company name, e.g. "Attn: Jane Doe". */
  customerAttention?: string;
  /** As printed on the invoice: recorded when it was issued. */
  customerEmail?: string;
  /** Where emails go: the company email on file, else the Bill To contact's when the company has none. */
  recipientEmail?: string;
  /** The Bill To contact's email, copied on invoice emails; absent when it is already the recipient. */
  recipientCcEmail?: string;
  customerPhone?: string;
  customerAddress?: string;
  invoiceDate: string;
  dueDate?: string;
  paymentTermId?: string | null;
  referenceNo?: string;
  amount: number;
  totalTax?: number;
  shippingHandlingCharge?: number;
  totalAmountAfterTax?: number;
  totalAllocatedAmount: number;
  paidAmount: number;
  creditedAmount?: number;
  writtenOffAmount?: number;
  balanceDue: number;
  settlementSummary?: string | null;
  version?: number;
  posted: boolean;
  status: InvoiceStatus;
  statusId?: number;
  notes?: string;
  paymentInstructions?: string;
  /** What prints on the PDF and email: own text, else payment bank details, else the default text. */
  effectivePaymentInstructions?: string | null;
  companyName?: string;
  seller?: InvoiceSeller;
  display?: InvoiceDisplay;
  salesInvoiceLines: InvoiceLineItem[];
  payments?: InvoicePaymentItem[];
  /** Emails sent to the customer about this invoice, newest first. */
  emails?: InvoiceEmailItem[];
}

export interface InvoiceEmailItem {
  dispatchId?: string | null;
  fromEmail?: string | null;
  id: string;
  kind: 'Invoice' | 'FollowUp' | string;
  to: string;
  sentAt: string;
  sentBy?: string | null;
}

/** The invoicing party as it should print, already resolved from the workspace settings. */
export interface InvoiceSeller {
  name: string;
  taxId?: string | null;
  addressLines: string[];
  email?: string | null;
  phone?: string | null;
}

export interface InvoiceDisplay {
  showCustomerEmail: boolean;
  showAttention: boolean;
  showNotes: boolean;
  showPaymentInstructions: boolean;
}

export type SellerNameSource = 'TradingName' | 'LegalName';

export interface SalesInvoiceSettings {
  sellerName: SellerNameSource;
  showSellerAddress: boolean;
  showCustomerEmail: boolean;
  showAttention: boolean;
  showNotes: boolean;
  showPaymentInstructions: boolean;
  defaultNotes: string | null;
  defaultPaymentInstructions: string | null;
  /** Bank account whose details print as payment instructions; null uses the free-text default. */
  paymentBankId: string | null;
}

export interface PostalAddress {
  line1: string | null;
  line2: string | null;
  city: string | null;
  region: string | null;
  postalCode: string | null;
  countryCode: string | null;
}

export interface BusinessProfile {
  legalName: string | null;
  businessEmail: string | null;
  businessPhone: string | null;
  address: PostalAddress;
}

export interface InvoicingKPIs {
  totalOutstanding: number;
  totalOverdue: number;
  paidThisMonth: number;
  draftsCount: number;
  draftsTotal: number;
}

export interface RecordPaymentPayload {
  invoiceId: string;
  amount: number;
  paymentDate: string;
  paymentMethod: string;
  referenceNo?: string;
  notes?: string;
  /** Deposit-to bank account; omitted when the workspace has no banks. */
  bankAccountId?: string | null;
}

/** A client or vendor as a picker lists it. */
export interface CustomerOption {
  id: string;
  name: string;
  email?: string;
  phone?: string;
  paymentTermId?: string;
  /** Archived parties are left out when picking one for a new document. */
  archived?: boolean;
}

/** A client as the Clients pages show and edit it (api/Sales/Client). */
/** A client: the party an invoice is billed to. */
export interface Client extends PartyBase {
  /** Date of its latest issued invoice. */
  lastInvoiceDate?: string | null;
}

export interface PaymentTermOption {
  id: string;
  description: string;
  dueAfterDays?: number;
}

export interface ItemOption {
  id: string;
  code?: string;
  description: string;
  price?: number;
  sellMeasurementId?: string;
  trackStock?: boolean;
  inventoryAccountId?: string | null;
  quantityOnHand?: number;
  averageCost?: number;
  salesAccountId?: string | null;
  salesAccountCode?: string | null;
  salesAccountName?: string | null;
  /** Archived items are left out when picking an item for a line. */
  archived?: boolean;
}

/** A product or service as the Products & services page shows and edits it (api/Catalog/Item). */
export interface CatalogItem {
  id: string;
  name: string;
  code?: string | null;
  /** Default selling price; null when there is none. */
  price?: number | null;
  /** Default purchase cost; null when there is none. */
  cost?: number | null;
  archived: boolean;
  trackStock: boolean;
  inventoryAccountId?: string | null;
  /** The account credited when this item is sold; defaults to 40100 (Sales). */
  salesAccountId?: string | null;
  salesAccountCode?: string | null;
  salesAccountName?: string | null;
  quantityOnHand: number;
  averageCost: number;
  hasStockMovements: boolean;
  /** No document uses it yet, so it can be deleted rather than archived. */
  canDelete?: boolean;
}
