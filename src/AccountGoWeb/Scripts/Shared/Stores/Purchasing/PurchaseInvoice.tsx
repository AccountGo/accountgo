import PurchaseInvoiceLine from "./PurchaseInvoiceLine";

export default class PurchaseInvoice {
    id: number;
    purchaseOrderId: number;
    vendorId: number;
    invoiceDate: Date;
    paymentTermId: number;
    referenceNo: string;
    purchaseInvoiceLines: PurchaseInvoiceLine[] = [];
 
    constructor(id = 0, vendorId = null, invoiceDate = null, paymentTermId = null, referenceNo = null) {
        this.id = id;
        this.vendorId = vendorId;
        this.invoiceDate = invoiceDate;
        this.paymentTermId = paymentTermId;
        this.referenceNo = referenceNo;
        this.purchaseOrderId = 0;
    }
}