import PurchaseInvoiceLine from "./PurchaseInvoiceLine";

export default class PurchaseInvoice {
    vendorId: number;
    invoiceDate: Date;
    paymentTermId: number;
    referenceNo: string;
    purchaseInvoiceLines: PurchaseInvoiceLine[] = [];
}