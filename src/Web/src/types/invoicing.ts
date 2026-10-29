import type { PartyBase } from './parties';

export type InvoiceStatus = 'Draft' | 'Open' | 'Partially Paid' | 'Paid' | 'Overdue' | 'Void';

export interface InvoiceLineItem {
  id?: number;
  itemId?: number | null;
  measurementId?: number | null;
  itemDescription?: string;
  measurementDescription?: string;
  quantity: number;
  amount: number;
  discount: number;
  lineTotal?: number;
  taxRate?: number;
  taxId?: number | null;
  taxAmount?: number;
  salesAccountId?: number | null;
  salesAccountCode?: string | null;
  salesAccountName?: string | null;
}

export interface InvoicePaymentItem {
  id: number;
  receiptNo?: string;
  date: string;
  amount: number;
  paymentMethod?: string;
  referenceNo?: string;
  notes?: string;
}

export interface Invoice {
  id: number;
  /** Null while a draft has no number; it is numbered when issued. */
  no: string | null;
  customerId: number;
  customerName: string;
  /** Attention line under the company name, e.g. "Attn: Jane Doe". */
  customerAttention?: string;
  /** As printed on the invoice: recorded when it was issued. */
  customerEmail?: string;
  /** Where emails go: Bill To contact email when set, else the company email on file now. */
  recipientEmail?: string;
  customerPhone?: string;
  customerAddress?: string;
  invoiceDate: string;
  dueDate?: string;
  paymentTermId?: number | null;
  referenceNo?: string;
  amount: number;
  totalTax?: number;
  shippingHandlingCharge?: number;
  totalAmountAfterTax?: number;
  totalAllocatedAmount: number;
  paidAmount: number;
  balanceDue: number;
  posted: boolean;
  status: InvoiceStatus;
  statusId?: number;
  notes?: string;
  paymentInstructions?: string;
  companyName?: string;
  seller?: InvoiceSeller;
  display?: InvoiceDisplay;
  salesInvoiceLines: InvoiceLineItem[];
  payments?: InvoicePaymentItem[];
  /** Emails sent to the customer about this invoice, newest first. */
  emails?: InvoiceEmailItem[];
}

export interface InvoiceEmailItem {
  id: number;
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
  showNotes: boolean;
  showPaymentInstructions: boolean;
}

export type SellerNameSource = 'TradingName' | 'LegalName';

export interface SalesInvoiceSettings {
  sellerName: SellerNameSource;
  showSellerAddress: boolean;
  showCustomerEmail: boolean;
  showNotes: boolean;
  showPaymentInstructions: boolean;
  defaultNotes: string | null;
  defaultPaymentInstructions: string | null;
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
  invoiceId: number;
  amount: number;
  paymentDate: string;
  paymentMethod: string;
  referenceNo?: string;
  notes?: string;
}

/** A client or vendor as a picker lists it. */
export interface CustomerOption {
  id: number;
  name: string;
  email?: string;
  phone?: string;
  paymentTermId?: number;
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
  id: number;
  description: string;
  dueAfterDays?: number;
}

export interface ItemOption {
  id: number;
  code?: string;
  description: string;
  price?: number;
  sellMeasurementId?: number;
  trackStock?: boolean;
  inventoryAccountId?: number | null;
  quantityOnHand?: number;
  averageCost?: number;
  salesAccountId?: number | null;
  salesAccountCode?: string | null;
  salesAccountName?: string | null;
  /** Archived items are left out when picking an item for a line. */
  archived?: boolean;
}

/** A product or service as the Products & services page shows and edits it (api/Catalog/Item). */
export interface CatalogItem {
  id: number;
  name: string;
  code?: string | null;
  /** Default selling price; null when there is none. */
  price?: number | null;
  /** Default purchase cost; null when there is none. */
  cost?: number | null;
  archived: boolean;
  trackStock: boolean;
  inventoryAccountId?: number | null;
  /** The account credited when this item is sold; defaults to 40100 (Sales). */
  salesAccountId?: number | null;
  salesAccountCode?: string | null;
  salesAccountName?: string | null;
  quantityOnHand: number;
  averageCost: number;
  hasStockMovements: boolean;
  /** No document uses it yet, so it can be deleted rather than archived. */
  canDelete?: boolean;
}
