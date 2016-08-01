import SalesInvoiceLine from "./SalesInvoiceLine";

interface ISalesInvoice {
    id;
    customerId;
    invoiceDate;
    paymentTermId;
    referenceNo;
    salesInvoiceLines: SalesInvoiceLine[];
}

export default class SalesInvoice implements ISalesInvoice {
    id: number;
    customerId: number;
    invoiceDate: Date;
    paymentTermId: number;
    referenceNo: string;
    salesInvoiceLines: SalesInvoiceLine[] = [];
}