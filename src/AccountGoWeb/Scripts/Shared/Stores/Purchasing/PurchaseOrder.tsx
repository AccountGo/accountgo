import PurchaseOrderLine from "./PurchaseOrderLine";

export default class PurchaseOrder {
    vendorId: number;
    orderDate: Date;
    paymentTermId: number;
    referenceNo: string;
    purchaseOrderLines: PurchaseOrderLine[] = [];
}