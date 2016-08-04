import PurchaseInvoiceLine from "./PurchaseInvoiceLine";

export default class PurchaseInvoice {
    id: number;
    purchaseOrderId: number;
    vendorId: number;
    invoiceDate: Date;
    paymentTermId: number;
    referenceNo: string;
    purchaseInvoiceLines: PurchaseInvoiceLine[] = [];
}