import {observable, extendObservable, action} from 'mobx';
import * as axios from "axios";

import Config = require("Config");

import PurchaseInvoice from './PurchaseInvoice';
import PurchaseInvoiceLine from './PurchaseInvoiceLine';

import CommonStore from "../Common/CommonStore";

let baseUrl = location.protocol
    + "//" + location.hostname
    + (location.port && ":" + location.port)
    + "/";

export default class PurchaseOrderStore {
    purchaseInvoice;
    commonStore;
    @observable validationErrors;

    constructor() {
        this.purchaseInvoice = new PurchaseInvoice();
        extendObservable(this.purchaseInvoice, {
            vendorId: this.purchaseInvoice.vendorId,
            invoiceDate: this.purchaseInvoice.invoiceDate,
            paymentTermId: this.purchaseInvoice.paymentTermId,
            referenceNo: this.purchaseInvoice.referenceNo,
            purchaseInvoiceLines: []
        });

        this.commonStore = new CommonStore();
    }

    savePurchaseInvoice() {
        //console.log(this.purchaseOrder);
        this.validationErrors = [];
        if (this.purchaseInvoice.vendorId === undefined || this.purchaseInvoice.vendorId === "")
            this.validationErrors.push("Vendor is required.");
        if (this.purchaseInvoice.paymentTermId === undefined || this.purchaseInvoice.paymentTermId === "")
            this.validationErrors.push("Payment term is required.");
        if (this.purchaseInvoice.orderDate === undefined || this.purchaseInvoice.orderDate === "")
            this.validationErrors.push("Date is required.");
        if (this.purchaseInvoice.purchaseOrderLines === undefined || this.purchaseInvoice.purchaseOrderLines.length < 1)
            this.validationErrors.push("Enter at least 1 line item.");
        if (this.purchaseInvoice.purchaseOrderLines !== undefined && this.purchaseInvoice.purchaseOrderLines.length > 0) {
            for (var i = 0; i < this.purchaseInvoice.purchaseOrderLines.length; i++) {
                if (this.purchaseInvoice.purchaseOrderLines[i].itemId === undefined
                    || this.purchaseInvoice.purchaseOrderLines[i].itemId === "")
                    this.validationErrors.push("Item is required.");
                if (this.purchaseInvoice.purchaseOrderLines[i].measurementId === undefined
                    || this.purchaseInvoice.purchaseOrderLines[i].measurementId === "")
                    this.validationErrors.push("Uom is required.");
                if (this.purchaseInvoice.purchaseOrderLines[i].quantity === undefined
                    || this.purchaseInvoice.purchaseOrderLines[i].quantity === ""
                    || this.purchaseInvoice.purchaseOrderLines[i].quantity === 0)
                    this.validationErrors.push("Quantity is required.");
                if (this.purchaseInvoice.purchaseOrderLines[i].amount === undefined
                    || this.purchaseInvoice.purchaseOrderLines[i].amount === ""
                    || this.purchaseInvoice.purchaseOrderLines[i].amount === 0)
                    this.validationErrors.push("Amount is required.");
                if (this.lineTotal(i) === undefined
                    || this.lineTotal(i).toString() === "NaN"
                    || this.lineTotal(i) === 0)
                    this.validationErrors.push("Invalid data.");
            }
        }

        if (this.validationErrors.length === 0) {
            axios.post(Config.apiUrl + "api/purchasing/savepurchaseinvoice", JSON.stringify(this.purchaseInvoice),
                {
                    headers: {
                        'Content-type': 'application/json'
                    }
                })
                .then(function (response) {
                    return;
                })
                .catch(function (error) {
                    console.log(error);
                    this.validationErrors.push("An error occured on posting data. Please check the browser console for more details.");
                }.bind(this))
        }
    }

    changedVendor(vendorId) {
        this.purchaseInvoice.vendorId = vendorId;
    }

    changedInvoiceDate(date) {
        this.purchaseInvoice.invoiceDate = date;
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