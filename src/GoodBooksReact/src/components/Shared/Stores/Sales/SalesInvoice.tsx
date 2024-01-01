import SalesInvoiceLine from "./SalesInvoiceLine";

export default class SalesInvoice {
    id: number;
    fromSalesOrderId: number = 0;
    customerId: number = 0;
    invoiceDate: Date = new Date();
    paymentTermId: number = 0;
    referenceNo: string= "";
    posted: boolean;
    readyForPosting: boolean;
    salesInvoiceLines: SalesInvoiceLine[] = [];

    constructor() {
        this.id = 0;
        this.posted = false;
        this.readyForPosting = false;
   
    }
}