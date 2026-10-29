// A draft has no number until it is issued (or one is typed in), so it reads "Draft invoice", never its internal id,
// which would pass for a real invoice number. Mirrors InvoiceFormat.Label on the API.

/** "Invoice #1043", or "Draft invoice". */
export const invoiceTitle = (invoice: { no?: string | null }) => (invoice.no ? `Invoice #${invoice.no}` : 'Draft invoice');
