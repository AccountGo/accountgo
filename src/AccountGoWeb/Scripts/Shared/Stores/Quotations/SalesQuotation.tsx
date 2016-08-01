import SalesQuotationLine from "./SalesQuotationLine";

export default class SalesQuotation {
    id: number;
    customerId: number;
    quotationDate: Date;
    paymentTermId: number;
    referenceNo: string;
    salesQuotationLines: SalesQuotationLine[] = [];
}