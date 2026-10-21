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
  no: string;
  customerId: number;
  customerName: string;
  customerEmail?: string;
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
  salesInvoiceLines: InvoiceLineItem[];
  payments?: InvoicePaymentItem[];
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
}
