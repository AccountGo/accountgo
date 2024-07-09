import SalesQuotationLine from "./SalesQuotationLine";

export default class SalesQuotation {
    id: number = 0;
    customerId: number = 0;
    quotationDate: Date = new Date();
    paymentTermId: number = 0;
    referenceNo: string = "";
    statusId: number = 0;
    salesQuotationLines: SalesQuotationLine[] = [];
}