import PurchaseInvoiceLine from "./PurchaseInvoiceLine";

export default class PurchaseInvoice {
    id: number;
    fromPurchaseOrderId: number;
    vendorId: number;
    invoiceDate: Date;
    paymentTermId: number;
    referenceNo: string;
    posted: boolean;
    purchaseInvoiceLines: PurchaseInvoiceLine[] = [];
}