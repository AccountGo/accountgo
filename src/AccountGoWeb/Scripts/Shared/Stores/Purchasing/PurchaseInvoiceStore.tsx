import {observable, extendObservable, action} from 'mobx';
import PurchaseInvoice from './PurchaseInvoice';
import PurchaseInvoiceLine from './PurchaseInvoiceLine';

export default class PurchaseOrderStore {
    purchaseInvoice;
    constructor() {
        this.purchaseInvoice = new PurchaseInvoice();
        extendObservable(this.purchaseInvoice, {
            vendorId: this.purchaseInvoice.vendorId,
            orderDate: this.purchaseInvoice.orderDate,
            paymentTermId: this.purchaseInvoice.paymentTermId,
            referenceNo: this.purchaseInvoice.referenceNo,
            purchaseInvoiceLines: []
        });
    }
    
    changedVendor(vendorId) {
        this.purchaseInvoice.vendorId = vendorId;
    }

    addLineItem(itemId, measurementId, quantity, amount, discount) {
        var newLineItem = new PurchaseInvoiceLine(itemId, measurementId, quantity, amount, discount);
        this.purchaseInvoice.purchaseInvoiceLines.push(extendObservable(newLineItem, newLineItem));        
    }

    removeLineItem(row) {
        this.purchaseInvoice.purchaseInvoiceLines.splice(row, 1);
    }

    updateLineItem(row, targetProperty, value) {
        if (this.purchaseInvoice.purchaseInvoiceLines.length > 0)
            this.purchaseInvoice.purchaseInvoiceLines[row][targetProperty] = value;
    }

    grandTotal() {
        var sum = 0;
        for (var i = 0; i < this.purchaseInvoice.purchaseInvoiceLines.length; i++) {
            var lineSum = this.purchaseInvoice.purchaseInvoiceLines[i].quantity * this.purchaseInvoice.purchaseInvoiceLines[i].amount;
            sum = sum + lineSum;
        }
        return sum;
    }

    lineTotal(row) {
        var lineSum = this.purchaseInvoice.purchaseInvoiceLines[row].quantity * this.purchaseInvoice.purchaseInvoiceLines[row].amount;
        return lineSum;
    }
}