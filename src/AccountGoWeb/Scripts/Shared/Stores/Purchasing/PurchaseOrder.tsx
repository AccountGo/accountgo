import PurchaseOrderLine from "./PurchaseOrderLine";

export default class PurchaseOrder {
    id: number;
    vendorId: number;
    orderDate: Date;
    paymentTermId: number;
    referenceNo: string;
    purchaseOrderLines: PurchaseOrderLine[] = [];

    constructor(id = 0, vendorId = null, invoiceDate = null, paymentTermId = null, referenceNo = null) {
        this.id = id;
        this.vendorId = vendorId;
        this.orderDate = invoiceDate;
        this.paymentTermId = paymentTermId;
        this.referenceNo = referenceNo;
    }
}