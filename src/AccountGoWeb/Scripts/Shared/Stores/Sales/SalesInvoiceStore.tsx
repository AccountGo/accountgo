import {observable, extendObservable, action} from 'mobx';
import SalesInvoice from './SalesInvoice';
import SalesInvoiceLine from './SalesInvoiceLine';

export default class SalesStore {
    salesInvoice;
    constructor() {
        this.salesInvoice = new SalesInvoice();
        extendObservable(this.salesInvoice, {
            customerId: this.salesInvoice.customerId,
            orderDate: this.salesInvoice.orderDate,
            paymentTermId: this.salesInvoice.paymentTermId,
            referenceNo: this.salesInvoice.referenceNo,
            salesInvoiceLines: []
        });
    }
    
    changedCustomer(custId) {
        this.salesInvoice.customerId = custId;
    }

    addLineItem(itemId, measurementId, quantity, amount, discount) {
        var newLineItem = new SalesInvoiceLine(itemId, measurementId, quantity, amount, discount);
        this.salesInvoice.salesInvoiceLines.push(extendObservable(newLineItem, newLineItem));        
    }

    removeLineItem(row) {
        this.salesInvoice.salesInvoiceLines.splice(row, 1);
    }

    updateLineItem(row, targetProperty, value) {
        if (this.salesInvoice.salesInvoiceLines.length > 0)
            this.salesInvoice.salesInvoiceLines[row][targetProperty] = value;
    }

    grandTotal() {
        var sum = 0;
        for (var i = 0; i < this.salesInvoice.salesInvoiceLines.length; i++) {
            var lineSum = this.salesInvoice.salesInvoiceLines[i].quantity * this.salesInvoice.salesInvoiceLines[i].amount;
            sum = sum + lineSum;
        }
        return sum;
    }

    lineTotal(row) {
        var lineSum = this.salesInvoice.salesInvoiceLines[row].quantity * this.salesInvoice.salesInvoiceLines[row].amount;;
        return lineSum;
    }
}