import {observable, extendObservable, action, autorun, computed} from 'mobx';
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
                this.salesInvoice.fromSalesOrderId = orderId;
                this.changedCustomer(result.data.customerId);
                this.salesInvoice.paymentTermId = result.data.paymentTermId;
                for (var i = 0; i < result.data.salesOrderLines.length; i++) {
                    this.addLineItem(
                        result.data.salesOrderLines[i].id,
                        result.data.salesOrderLines[i].itemId,
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
                this.salesInvoice.id = result.data.id;
                this.changedCustomer(result.data.customerId);
                this.salesInvoice.paymentTermId = result.data.paymentTermId;
                for (var i = 0; i < result.data.salesInvoiceLines.length; i++) {
                    this.addLineItem(
                        result.data.salesInvoiceLines[i].id,
                        result.data.salesInvoiceLines[i].itemId,
                        result.data.salesInvoiceLines[i].measurementId,
                        result.data.salesInvoiceLines[i].quantity,
                        result.data.salesInvoiceLines[i].amount,
                        result.data.salesInvoiceLines[i].discount
                    );
                }
            }.bind(this));
        }

        autorun(() => this.computeTotals());
    }

    @observable RTotal = 0;
    @observable GTotal = 0;
    @observable TTotal = 0;

    async computeTotals() {
        var rtotal = 0;
        var ttotal = 0;
        var gtotal = 0;

        for (var i = 0; i < this.salesInvoice.salesInvoiceLines.length; i++) {
            var lineItem = this.salesInvoice.salesInvoiceLines[i];
            var lineSum = lineItem.quantity * lineItem.amount;
            rtotal = rtotal + lineSum;
            await axios.get(Config.apiUrl + "api/tax/gettax?itemId=" + lineItem.itemId + "&partyId=" + this.salesInvoice.customerId)
                .then(function (result) {
                    if (result.data.length > 0) {
                        ttotal = ttotal + this.commonStore.getSalesLineTaxAmount(lineItem.quantity, lineItem.amount, lineItem.discount, result.data);
                    }
                }.bind(this));
        }

        this.RTotal = rtotal;
        this.TTotal = ttotal;
        this.GTotal = rtotal - ttotal;
    }

    async saveNewSalesInvoice() {
        if (this.validation() && this.validationErrors.length === 0) {
            await axios.post(Config.apiUrl + "api/sales/savesalesinvoice", JSON.stringify(this.salesInvoice),
                {
                    headers: {
                        'Content-type': 'application/json'
                    }
                })
                .then(function (response) {
                    window.location.href = baseUrl + 'sales/salesinvoices';
                })
                .catch(function (error) {
                    error.data.map(function (err) {
                        this.validationErrors.push(err);
                    }.bind(this));
                }.bind(this))
        }
    }

    validation() {
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
                if (this.salesInvoice.salesInvoiceLines[i].itemId === undefined)
                    this.validationErrors.push("Item is required.");
                if (this.salesInvoice.salesInvoiceLines[i].measurementId === undefined)
                    this.validationErrors.push("Uom is required.");
                if (this.salesInvoice.salesInvoiceLines[i].quantity === undefined
                    || this.salesInvoice.salesInvoiceLines[i].quantity === 0)
                    this.validationErrors.push("Quantity is required.");
                if (this.salesInvoice.salesInvoiceLines[i].amount === undefined
                    || this.salesInvoice.salesInvoiceLines[i].amount === 0)
                    this.validationErrors.push("Amount is required.");
                if (this.getLineTotal(i) === undefined
                    || this.getLineTotal(i).toString() === "NaN"
                    || this.getLineTotal(i) === 0)
                    this.validationErrors.push("Invalid data.");
            }
        }

        return this.validationErrors.length === 0;
    }

    changedCustomer(custId) {
        this.salesInvoice.customerId = custId;
    }

    changedInvoiceDate(date) {
        this.salesInvoice.invoiceDate = date;
    }

    changedPaymentTerm(termId) {
        this.salesInvoice.paymentTermId = termId;
    }

    addLineItem(id = 0, itemId, measurementId, quantity, amount, discount) {
        var newLineItem = new SalesInvoiceLine(id, itemId, measurementId, quantity, amount, discount);
        this.salesInvoice.salesInvoiceLines.push(extendObservable(newLineItem, newLineItem));        
    }

    removeLineItem(row) {
        this.salesInvoice.salesInvoiceLines.splice(row, 1);
    }

    updateLineItem(row, targetProperty, value) {
        if (this.salesInvoice.salesInvoiceLines.length > 0)
            this.salesInvoice.salesInvoiceLines[row][targetProperty] = value;

        this.computeTotals();
    }

    getLineTotal(row) {
        let lineSum = 0;
        let lineItem = this.salesInvoice.salesInvoiceLines[row];
        lineSum = (lineItem.quantity * lineItem.amount) - lineItem.discount;
        return lineSum;
    }
}