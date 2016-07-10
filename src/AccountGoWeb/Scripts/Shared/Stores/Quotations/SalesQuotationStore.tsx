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
            salesQuotationLines: []
        });
    }
    
    changedCustomer(custId) {
        this.salesQuotation.customerId = custId;
    }

    addLineItem(itemId, measurementId, quantity, amount, discount) {
        var newLineItem = new SalesQuotationLine(itemId, measurementId, quantity, amount, discount);
        this.salesQuotation.salesQuotationLines.push(extendObservable(newLineItem, newLineItem));        
    }

    removeLineItem(row) {
        this.salesQuotation.salesQuotationLines.splice(row, 1);
    }

    updateLineItem(row, targetProperty, value) {
        if (this.salesQuotation.salesQuotationLines.length > 0)
            this.salesQuotation.salesQuotationLines[row][targetProperty] = value;
    }

    grandTotal() {
        var sum = 0;
        for (var i = 0; i < this.salesQuotation.salesQuotationLines.length; i++) {
            var lineSum = this.salesQuotation.salesQuotationLines[i].quantity * this.salesQuotation.salesQuotationLines[i].amount;
            sum = sum + lineSum;
        }
        return sum;
    }

    lineTotal(row) {
        var lineSum = this.salesQuotation.salesQuotationLines[row].quantity * this.salesQuotation.salesQuotationLines[row].amount;;
        return lineSum;
    }
}