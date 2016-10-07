import SalesOrderLine from "./SalesOrderLine";

//interface ISalesOrder {
//    id;
//    customerId;
//    orderDate;
//    paymentTermId;
//    referenceNo;
//    salesOrderLines: SalesOrderLine[];
//}

export default class SalesOrder {
    id: number;
    customerId: number;
    orderDate: Date;
    paymentTermId: number;
    referenceNo: string;
    statusId: number;
    quotationId: number;
    salesOrderLines: SalesOrderLine[] = [];
}