import SalesInvoiceLine from "./SalesInvoiceLine";

export default class SalesInvoice {
    id: number;
    fromSalesOrderId: any;
    customerId: number;
    invoiceDate: Date;
    paymentTermId: number;
    referenceNo: string;
    posted: boolean;
    readyForPosting: boolean;
    salesInvoiceLines: SalesInvoiceLine[] = [];

    constructor() {
        this.id = 0;
        this.posted = false;
        this.readyForPosting = false;
   
    }
}