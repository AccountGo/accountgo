import PurchaseOrderLine from "./PurchaseOrderLine";

export default class PurchaseOrder {
    id: number = 0;
    vendorId: number = 0;
    orderDate: Date = new Date();
    paymentTermId: number = 0;
    referenceNo: string = "";
    statusId: number = 0;
    purchaseOrderLines: PurchaseOrderLine[] = [];
}