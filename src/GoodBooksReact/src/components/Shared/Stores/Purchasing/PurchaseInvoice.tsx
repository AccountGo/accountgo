import PurchaseInvoiceLine from "./PurchaseInvoiceLine";

export default class PurchaseInvoice {
    id: number = 0;
    fromPurchaseOrderId: number = 0;
    vendorId: number = 0;
    invoiceDate: Date = new Date();
    paymentTermId: number = 0;
    referenceNo: string = "";
    posted: boolean = false;
    readyForPosting: boolean = false;
    purchaseInvoiceLines: PurchaseInvoiceLine[] = [];
    statusId: number = 0;

    constructor() {
        this.id = 0;
        this.posted = false;
        this.readyForPosting = false;

    }
}