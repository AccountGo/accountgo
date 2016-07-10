import SalesQuotationLine from "./SalesQuotationLine";

export default class SalesQuotation {
    customerId: number;
    quotationDate: Date;
    paymentTermid: number;
    referenceNo: string;
    salesQuotationLines: SalesQuotationLine[] = [];
}