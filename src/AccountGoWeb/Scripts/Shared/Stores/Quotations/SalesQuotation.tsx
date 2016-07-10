import SalesQuotationLine from "./SalesQuotationLine";

export default class SalesQuotation {
    customerId: number;
    orderDate: Date;
    paymentTermid: number;
    referenceNo: string;
    salesQuotationLines: SalesQuotationLine[] = [];
}