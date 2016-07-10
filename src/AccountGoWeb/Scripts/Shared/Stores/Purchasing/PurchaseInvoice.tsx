import PurchaseInvoiceLine from "./PurchaseInvoiceLine";

export default class PurchaseInvoice {
    vendorId: number;
    orderDate: Date;
    paymentTermId: number;
    referenceNo: string;
    purchaseInvoiceLines: PurchaseInvoiceLine[] = [];
}