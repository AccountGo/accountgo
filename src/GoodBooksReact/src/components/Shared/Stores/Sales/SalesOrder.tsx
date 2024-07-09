import SalesOrderLine from "./SalesOrderLine";

export default class SalesOrder {
    id: number = 0;
    customerId: number = 0;
    orderDate: Date = new Date();
    paymentTermId: number = 0;
    referenceNo: string = "";
    statusId: number = 0;
    quotationId: number = 0;
    salesOrderLines: SalesOrderLine[] = [];
}