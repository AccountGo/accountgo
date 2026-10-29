import type { Bill } from '../types';

/** How a bill is named in messages: the vendor's number when it has one, else the workspace's, else "this draft". */
export const billTitle = (bill: Pick<Bill, 'vendorInvoiceNo' | 'no' | 'vendorName'>) =>
  bill.vendorInvoiceNo
    ? `bill ${bill.vendorInvoiceNo}${bill.vendorName ? ` from ${bill.vendorName}` : ''}`
    : bill.no
      ? `bill #${bill.no}`
      : 'this draft bill';

/** The same, starting a sentence. */
export const BillTitle = (bill: Pick<Bill, 'vendorInvoiceNo' | 'no' | 'vendorName'>) => {
  const title = billTitle(bill);
  return title.charAt(0).toUpperCase() + title.slice(1);
};
