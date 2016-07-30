import {observable, extendObservable, action} from 'mobx';
import * as axios from "axios";

import Config = require("Config");

import SalesInvoice from './SalesInvoice';
import SalesInvoiceLine from './SalesInvoiceLine';

import CommonStore from "../Common/CommonStore";

let baseUrl = location.protocol
    + "//" + location.hostname
    + (location.port && ":" + location.port)
    + "/";

export default class SalesStore {
    salesInvoice;
    commonStore;
    @observable validationErrors;

    constructor() {
        this.salesInvoice = new SalesInvoice();
        extendObservable(this.salesInvoice, {
            customerId: this.salesInvoice.customerId,
            invoiceDate: this.salesInvoice.invoiceDate,
            paymentTermId: this.salesInvoice.paymentTermId,
            referenceNo: this.salesInvoice.referenceNo,
            salesInvoiceLines: []
        });

        this.commonStore = new CommonStore();
    }

    saveNewSalesInvoice() {
        this.validationErrors = [];
        if (this.salesInvoice.customerId === undefined || this.salesInvoice.customerId === "")
            this.validationErrors.push("Customer is required.");
        if (this.salesInvoice.paymentTermId === undefined || this.salesInvoice.paymentTermId === "")
            this.salesInvoice.push("Payment term is required.");
        if (this.salesInvoice.orderDate === undefined || this.salesInvoice.orderDate === "")
            this.validationErrors.push("Date is required.");
        if (this.salesInvoice.purchaseOrderLines === undefined || this.salesInvoice.purchaseOrderLines.length < 1)
            this.validationErrors.push("Enter at least 1 line item.");
        if (this.salesInvoice.purchaseOrderLines !== undefined && this.salesInvoice.purchaseOrderLines.length > 0) {
            for (var i = 0; i < this.salesInvoice.purchaseOrderLines.length; i++) {
                if (this.salesInvoice.purchaseOrderLines[i].itemId === undefined
                    || this.salesInvoice.purchaseOrderLines[i].itemId === "")
                    this.validationErrors.push("Item is required.");
                if (this.salesInvoice.purchaseOrderLines[i].measurementId === undefined
                    || this.salesInvoice.purchaseOrderLines[i].measurementId === "")
                    this.validationErrors.push("Uom is required.");
                if (this.salesInvoice.purchaseOrderLines[i].quantity === undefined
                    || this.salesInvoice.purchaseOrderLines[i].quantity === ""
                    || this.salesInvoice.purchaseOrderLines[i].quantity === 0)
                    this.validationErrors.push("Quantity is required.");
                if (this.salesInvoice.purchaseOrderLines[i].amount === undefined
                    || this.salesInvoice.purchaseOrderLines[i].amount === ""
                    || this.salesInvoice.purchaseOrderLines[i].amount === 0)
                    this.validationErrors.push("Amount is required.");
                if (this.lineTotal(i) === undefined
                    || this.lineTotal(i).toString() === "NaN"
                    || this.lineTotal(i) === 0)
                    this.validationErrors.push("Invalid data.");
            }
        }

        if (this.validationErrors.length === 0) {
            axios.post(Config.apiUrl + "api/sales/savesalesinvoice", JSON.stringify(this.salesInvoice),
                {
                    headers: {
                        'Content-type': 'application/json'
                    }
                })
                .then(function (response) {
                    console.log(response);
                })
                .catch(function (error) {
                    console.log(error);
                }.bind(this))
        }
    }

    changedCustomer(custId) {
        this.salesInvoice.customerId = custId;
    }

    changedInvoiceDate(date) {
        this.salesInvoice.invoiceDate = date;
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