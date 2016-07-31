import SalesInvoiceLine from "./SalesInvoiceLine";

interface ISalesInvoice {
    id;
    customerId;
    invoiceDate;
    paymentTermid;
    referenceNo;
    salesInvoiceLines: SalesInvoiceLine[];
}

export default class SalesInvoice implements ISalesInvoice {
    id: number;
    customerId: number;
    invoiceDate: Date;
    paymentTermid: number;
    referenceNo: string;
    salesInvoiceLines: SalesInvoiceLine[] = [];
}