import SalesOrderLine from "./SalesOrderLine";

interface ISalesOrder {
    customerId;
    orderDate;
    paymentTermid;
    referenceNo;
    salesOrderLines: SalesOrderLine[];
}

export default class SalesOrder implements ISalesOrder {
    customerId: number;
    orderDate: Date;
    paymentTermid: number;
    referenceNo: string;
    salesOrderLines: SalesOrderLine[] = [];
    //constructor(customerId, orderDate, paymentTermId, referenceNo) {
    //    this.customerId = customerId;
    //    this.orderDate = orderDate;
    //    this.paymentTermid = paymentTermId;
    //    this.referenceNo = referenceNo;
    //}
}