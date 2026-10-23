import { Invoice } from '../types';

export interface ClientBalance {
  /** Unpaid on issued invoices. */
  outstanding: number;
  /** The part of it past due. */
  overdue: number;
  /** Date of the latest issued invoice. */
  lastInvoiceDate?: string;
}

/**
 * What each client owes, from the same invoice list and balances the Invoices page shows. Drafts and voided
 * invoices are not owed.
 */
export function clientBalances(invoices: Invoice[]): Map<number, ClientBalance> {
  const byClient = new Map<number, ClientBalance>();
  for (const inv of invoices) {
    if (inv.status === 'Draft' || inv.status === 'Void') continue;
    const entry = byClient.get(inv.customerId) ?? { outstanding: 0, overdue: 0 };
    if (inv.balanceDue > 0) {
      entry.outstanding += inv.balanceDue;
      if (inv.status === 'Overdue') entry.overdue += inv.balanceDue;
    }
    if (inv.invoiceDate && (!entry.lastInvoiceDate || inv.invoiceDate > entry.lastInvoiceDate)) {
      entry.lastInvoiceDate = inv.invoiceDate;
    }
    byClient.set(inv.customerId, entry);
  }
  return byClient;
}
