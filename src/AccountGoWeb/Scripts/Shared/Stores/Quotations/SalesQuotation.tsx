import SalesQuotationLine from "./SalesQuotationLine";

export default class SalesQuotation {
    customerId: number;
    quotationDate: Date;
    paymentTermId: number;
    referenceNo: string;
    salesQuotationLines: SalesQuotationLine[] = [];
}