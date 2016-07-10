import {observable, extendObservable, action} from 'mobx';
import SalesQuotation from './SalesQuotation';
import SalesQuotationLine from './SalesQuotationLine';

export default class SalesQuotationStore {
    salesQuotation;
    constructor() {
        this.salesQuotation = new SalesQuotation();
        extendObservable(this.salesQuotation, {
            customerId: this.salesQuotation.customerId,
            orderDate: this.salesQuotation.orderDate,
            paymentTermId: this.salesQuotation.paymentTermId,
            referenceNo: this.salesQuotation.referenceNo,
            salesOrderLines: []
        });
    }
    
    changedCustomer(custId) {
        this.salesQuotation.customerId = custId;
    }

    addLineItem(itemId, measurementId, quantity, amount, discount) {
        var newLineItem = new SalesQuotationLine(itemId, measurementId, quantity, amount, discount);
        this.salesQuotation.salesOrderLines.push(extendObservable(newLineItem, newLineItem));        
    }

    removeLineItem(row) {
        this.salesQuotation.salesOrderLines.splice(row, 1);
    }

    updateLineItem(row, targetProperty, value) {
        if (this.salesQuotation.salesOrderLines.length > 0)
            this.salesQuotation.salesOrderLines[row][targetProperty] = value;
    }

    grandTotal() {
        var sum = 0;
        for (var i = 0; i < this.salesQuotation.salesOrderLines.length; i++) {
            var lineSum = this.salesQuotation.salesOrderLines[i].quantity * this.salesQuotation.salesOrderLines[i].amount;
            sum = sum + lineSum;
        }
        return sum;
    }

    lineTotal(row) {
        var lineSum = this.salesQuotation.salesOrderLines[row].quantity * this.salesQuotation.salesOrderLines[row].amount;;
        return lineSum;
    }
}