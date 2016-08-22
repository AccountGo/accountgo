import SalesInvoiceLine from "./SalesInvoiceLine";

export default class SalesInvoice {
    id: number;
    fromSalesOrderId: any;
    customerId: number;
    invoiceDate: Date;
    paymentTermId: number;
    referenceNo: string;
    posted: boolean;
    salesInvoiceLines: SalesInvoiceLine[] = [];
}