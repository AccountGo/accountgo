import SalesInvoiceLine from "./SalesInvoiceLine";

interface ISalesInvoice {
    customerId;
    invoiceDate;
    paymentTermid;
    referenceNo;
    salesInvoiceLines: SalesInvoiceLine[];
}

export default class SalesInvoice implements ISalesInvoice {
    customerId: number;
    invoiceDate: Date;
    paymentTermid: number;
    referenceNo: string;
    salesInvoiceLines: SalesInvoiceLine[] = [];
}