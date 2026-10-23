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
  taxAmount?: number;
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
  /** As printed on the invoice: recorded when it was issued. */
  customerEmail?: string;
  /** Where emails go: the client's email on file now. */
  recipientEmail?: string;
  customerPhone?: string;
  customerAddress?: string;
  invoiceDate: string;
  dueDate?: string;
  paymentTermId?: number | null;
  referenceNo?: string;
  amount: number;
  totalTax?: number;
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
  kind: 'Invoice' | string;
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

export interface CustomerOption {
  id: number;
  name: string;
  email?: string;
  phone?: string;
  paymentTermId?: number;
  /** Archived clients are left out when picking a client for an invoice. */
  archived?: boolean;
}

/** A client as the Clients pages show and edit it (api/Sales/Client). */
export interface Client {
  id: number;
  name: string;
  email?: string | null;
  phone?: string | null;
  street?: string | null;
  /** City and state or province, one field as the invoice prints it. */
  city?: string | null;
  postalCode?: string | null;
  country?: string | null;
  /** Null: new invoices use the workspace's default terms. */
  paymentTermId?: number | null;
  archived: boolean;
  /** Nothing recorded for it yet, so it can be deleted rather than archived. */
  canDelete?: boolean;
  /** Unpaid on its issued invoices. */
  outstanding: number;
  /** The part of it past due. */
  overdue: number;
  /** Payments recorded against its invoices. */
  paidToDate: number;
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
  /** No document uses it yet, so it can be deleted rather than archived. */
  canDelete?: boolean;
}
