import SalesInvoiceLine from "./SalesInvoiceLine";

interface ISalesInvoice {
    customerId;
    orderDate;
    paymentTermid;
    referenceNo;
    salesInvoiceLines: SalesInvoiceLine[];
}

export default class SalesInvoice implements ISalesInvoice {
    customerId: number;
    orderDate: Date;
    paymentTermid: number;
    referenceNo: string;
    salesInvoiceLines: SalesInvoiceLine[] = [];
}