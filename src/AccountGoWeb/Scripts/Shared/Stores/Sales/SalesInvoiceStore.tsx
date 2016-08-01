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
    salesInvoice: SalesInvoice;
    commonStore;
    @observable validationErrors;

    constructor(orderId, invoiceId) {
        this.commonStore = new CommonStore();
        this.salesInvoice = new SalesInvoice();
        extendObservable(this.salesInvoice, {
            customerId: this.salesInvoice.customerId,
            invoiceDate: this.salesInvoice.invoiceDate,
            paymentTermId: this.salesInvoice.paymentTermId,
            referenceNo: this.salesInvoice.referenceNo,
            salesInvoiceLines: []
        });

        if (orderId !== undefined) {
            var result = axios.get(Config.apiUrl + "api/sales/salesorder?id=" + orderId);
            result.then(function (result) {
                this.changedCustomer(result.data.customerId);
                for (var i = 0; i < result.data.salesOrderLines.length; i++) {
                    this.addLineItem(result.data.salesOrderLines[i].itemId,
                        result.data.salesOrderLines[i].measurementId,
                        result.data.salesOrderLines[i].quantity,
                        result.data.salesOrderLines[i].amount,
                        result.data.salesOrderLines[i].discount
                    );
                }
            }.bind(this));
        }
        else if (invoiceId !== undefined) {
            var result = axios.get(Config.apiUrl + "api/sales/salesinvoice?id=" + invoiceId);
            result.then(function (result) {
                this.changedCustomer(result.data.customerId);
                for (var i = 0; i < result.data.salesInvoiceLines.length; i++) {
                    this.addLineItem(result.data.salesInvoiceLines[i].itemId,
                        result.data.salesInvoiceLines[i].measurementId,
                        result.data.salesInvoiceLines[i].quantity,
                        result.data.salesInvoiceLines[i].amount,
                        result.data.salesInvoiceLines[i].discount
                    );
                }
            }.bind(this));
        }
    }

    saveNewSalesInvoice() {
        this.validationErrors = [];
        if (this.salesInvoice.customerId === undefined)
            this.validationErrors.push("Customer is required.");
        if (this.salesInvoice.paymentTermId === undefined)
            this.validationErrors.push("Payment term is required.");
        if (this.salesInvoice.invoiceDate === undefined)
            this.validationErrors.push("Date is required.");
        if (this.salesInvoice.salesInvoiceLines === undefined || this.salesInvoice.salesInvoiceLines.length < 1)
            this.validationErrors.push("Enter at least 1 line item.");
        if (this.salesInvoice.salesInvoiceLines !== undefined && this.salesInvoice.salesInvoiceLines.length > 0) {
            for (var i = 0; i < this.salesInvoice.salesInvoiceLines.length; i++) {
                if (this.salesInvoice.salesInvoiceLines[i].itemId === undefined
                    || this.salesInvoice.salesInvoiceLines[i].itemId === "")
                    this.validationErrors.push("Item is required.");
                if (this.salesInvoice.salesInvoiceLines[i].measurementId === undefined
                    || this.salesInvoice.salesInvoiceLines[i].measurementId === "")
                    this.validationErrors.push("Uom is required.");
                if (this.salesInvoice.salesInvoiceLines[i].quantity === undefined
                    || this.salesInvoice.salesInvoiceLines[i].quantity === ""
                    || this.salesInvoice.salesInvoiceLines[i].quantity === 0)
                    this.validationErrors.push("Quantity is required.");
                if (this.salesInvoice.salesInvoiceLines[i].amount === undefined
                    || this.salesInvoice.salesInvoiceLines[i].amount === ""
                    || this.salesInvoice.salesInvoiceLines[i].amount === 0)
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