import {observable, extendObservable, action} from 'mobx';
import SalesOrder from './SalesOrder';
import SalesOrderLine from './SalesOrderLine';

export default class SalesStore {
    salesOrder;

    constructor() {
        this.salesOrder = new SalesOrder();
        extendObservable(this.salesOrder, {
            customerId: this.salesOrder.customerId,
            orderDate: this.salesOrder.orderDate,
            paymentTermId: this.salesOrder.paymentTermId,
            referenceNo: this.salesOrder.referenceNo,
            salesOrderLines: []
        });
    }
    
    changedCustomer(custId) {
        this.salesOrder.customerId = custId;
    }

    addLineItem(itemId, measurementId, quantity, amount, discount) {
        var newLineItem = new SalesOrderLine(itemId, measurementId, quantity, amount, discount);
        this.salesOrder.salesOrderLines.push(extendObservable(newLineItem, newLineItem));        
    }

    removeLineItem(row) {
        this.salesOrder.salesOrderLines.splice(row, 1);
    }

    updateLineItem(row, targetProperty, value) {
        this.salesOrder.salesOrderLines[row][targetProperty] = value;
    }

    grandTotal() {
        var sum = 0;
        for (var i = 0; i < this.salesOrder.salesOrderLines.length; i++) {
            var lineSum = this.salesOrder.salesOrderLines[i].quantity * this.salesOrder.salesOrderLines[i].amount;
            sum = sum + lineSum;
        }
        return sum;
    }
}