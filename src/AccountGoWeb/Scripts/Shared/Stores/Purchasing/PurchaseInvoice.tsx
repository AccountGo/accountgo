import PurchaseInvoiceLine from "./PurchaseInvoiceLine";

export default class PurchaseInvoice {
    id: number;
    fromPurchaseOrderId: number;
    vendorId: number;
    invoiceDate: Date;
    paymentTermId: number;
    referenceNo: string;
    posted: boolean;
    readyForPosting: boolean;
    purchaseInvoiceLines: PurchaseInvoiceLine[] = [];

    constructor() {
        this.id = 0;
        this.posted = false;
        this.readyForPosting = false;

    }
}