﻿import {observable, extendObservable, action, autorun, computed} from 'mobx';
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

    constructor(purchId: any, invoiceId: any) {        
        this.commonStore = new CommonStore();
        this.purchaseInvoice = new PurchaseInvoice();
        extendObservable(this.purchaseInvoice, {
            vendorId: this.purchaseInvoice.vendorId,
            invoiceDate: this.purchaseInvoice.invoiceDate,
            paymentTermId: this.purchaseInvoice.paymentTermId,
            referenceNo: this.purchaseInvoice.referenceNo,
            purchaseInvoiceLines: []
        });

        autorun(() => this.computeTotals());

        if (purchId !== undefined) {
            axios.get(Config.apiUrl + "api/purchasing/purchaseorder?id=" + purchId)
                .then(function (result) {
                    this.purchaseInvoice.purchaseOrderHeaderId = purchId;     
                    this.purchaseInvoice.paymentTermId = result.data.paymentTermId;
                    this.purchaseInvoice.referenceNo = result.data.referenceNo;
                    this.changedVendor(result.data.vendorId);
                    this.changedInvoiceDate(result.data.orderDate);
                    for (var i = 0; i < result.data.purchaseOrderLines.length; i++) {
                        this.addLineItem(
                            result.data.purchaseOrderLines[i].id,
                            result.data.purchaseOrderLines[i].itemId,
                            result.data.purchaseOrderLines[i].measurementId,
                            result.data.purchaseOrderLines[i].quantity,
                            result.data.purchaseOrderLines[i].amount,
                            result.data.purchaseOrderLines[i].discount
                        );
                    }
                    this.computeTotals();
                }.bind(this))
                .catch(function (error) {
                }.bind(this));
        }
        else if (invoiceId !== undefined) {
            axios.get(Config.apiUrl + "api/purchasing/purchaseinvoice?id=" + invoiceId)
                .then(function (result) {
                    this.purchaseInvoice.id = result.data.id;
                    this.purchaseInvoice.paymentTermId = result.data.paymentTermId;
                    this.purchaseInvoice.referenceNo = result.data.referenceNo;
                    this.changedVendor(result.data.vendorId);
                    this.changedInvoiceDate(result.data.invoiceDate);
                    for (var i = 0; i < result.data.purchaseInvoiceLines.length; i++) {
                        this.addLineItem(
                            result.data.purchaseInvoiceLines[i].id,
                            result.data.purchaseInvoiceLines[i].itemId,
                            result.data.purchaseInvoiceLines[i].measurementId,
                            result.data.purchaseInvoiceLines[i].quantity,
                            result.data.purchaseInvoiceLines[i].amount,
                            result.data.purchaseInvoiceLines[i].discount
                        );
                    }
                    this.computeTotals();
                }.bind(this))
                .catch(function (error) {
                }.bind(this));
        }
    }

    @observable RTotal = 0;
    @observable GTotal = 0;
    @observable TTotal = 0;

    computeTotals() {
        var rtotal = 0;
        var ttotal = 0;
        var gtotal = 0;

        for (var i = 0; i < this.purchaseInvoice.purchaseInvoiceLines.length; i++) {
            var lineItem = this.purchaseInvoice.purchaseInvoiceLines[i];
            rtotal = rtotal + this.getLineTotal(i);
            axios.get(Config.apiUrl + "api/tax/gettax?itemId=" + lineItem.itemId + "&partyId=" + this.purchaseInvoice.vendorId + "&type=2")
                .then(function (result) {
                    if (result.data.length > 0) {
                        ttotal = ttotal + this.commonStore.getPurhcaseLineTaxAmount(lineItem.quantity, lineItem.amount, lineItem.discount, result.data);
                    }
                    this.TTotal = ttotal;
                    this.GTotal = rtotal - ttotal;
                }.bind(this));
            this.RTotal = rtotal;
        }
    }

    savePurchaseInvoice() {
        if (this.validation() && this.validationErrors.length === 0) {
            axios.post(Config.apiUrl + "api/purchasing/savepurchaseinvoice", JSON.stringify(this.purchaseInvoice),
                {
                    headers: {
                        'Content-type': 'application/json'
                    }
                })
                .then(function (response) {
                    window.location.href = baseUrl + 'purchasing/purchaseinvoices';
                })
                .catch(function (error) {
                    error.data.map(function (err) {
                        this.validationErrors.push(err);
                    }.bind(this));
                }.bind(this))
        }
    }

    postInvoice() {
        if (this.validation() && this.validationErrors.length === 0) {
            axios.post(Config.apiUrl + "api/purchasing/postpurchaseinvoice", JSON.stringify(this.purchaseInvoice),
                {
                    headers: {
                        'Content-type': 'application/json'
                    }
                })
                .then(function (response) {
                    window.location.href = baseUrl + 'purchasing/purchaseinvoices';
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
        if (this.purchaseInvoice.vendorId === undefined)
            this.validationErrors.push("Vendor is required.");
        if (this.purchaseInvoice.paymentTermId === undefined)
            this.validationErrors.push("Payment term is required.");
        if (this.purchaseInvoice.invoiceDate === undefined)
            this.validationErrors.push("Date is required.");
        if (this.purchaseInvoice.purchaseInvoiceLines === undefined || this.purchaseInvoice.purchaseInvoiceLines.length < 1)
            this.validationErrors.push("Enter at least 1 line item.");
        if (this.purchaseInvoice.purchaseInvoiceLines !== undefined && this.purchaseInvoice.purchaseInvoiceLines.length > 0) {
            for (var i = 0; i < this.purchaseInvoice.purchaseInvoiceLines.length; i++) {
                if (this.purchaseInvoice.purchaseInvoiceLines[i].itemId === undefined
                    || this.purchaseInvoice.purchaseInvoiceLines[i].itemId === "")
                    this.validationErrors.push("Item is required.");
                if (this.purchaseInvoice.purchaseInvoiceLines[i].measurementId === undefined
                    || this.purchaseInvoice.purchaseInvoiceLines[i].measurementId === "")
                    this.validationErrors.push("Uom is required.");
                if (this.purchaseInvoice.purchaseInvoiceLines[i].quantity === undefined
                    || this.purchaseInvoice.purchaseInvoiceLines[i].quantity === ""
                    || this.purchaseInvoice.purchaseInvoiceLines[i].quantity === 0)
                    this.validationErrors.push("Quantity is required.");
                if (this.purchaseInvoice.purchaseInvoiceLines[i].amount === undefined
                    || this.purchaseInvoice.purchaseInvoiceLines[i].amount === ""
                    || this.purchaseInvoice.purchaseInvoiceLines[i].amount === 0)
                    this.validationErrors.push("Amount is required.");
                if (this.getLineTotal(i) === undefined
                    || this.getLineTotal(i).toString() === "NaN"
                    || this.getLineTotal(i) === 0)
                    this.validationErrors.push("Invalid data.");
            }
        }

        return this.validationErrors.length === 0;
    }
    changedReferenceNo(refNo) {
        this.purchaseInvoice.referenceNo = refNo;
    }
    changedVendor(vendorId) {
        this.purchaseInvoice.vendorId = vendorId;
    }

    changedPaymentTerm(paymentTermId) {
        this.purchaseInvoice.paymentTermId = paymentTermId;
    }
    changedInvoiceDate(date) {
        this.purchaseInvoice.invoiceDate = date;
    }

    addLineItem(id, itemId, measurementId, quantity, amount, discount) {
        var newLineItem = new PurchaseInvoiceLine(id, itemId, measurementId, quantity, amount, discount);
        this.purchaseInvoice.purchaseInvoiceLines.push(extendObservable(newLineItem, newLineItem));        
    }

    removeLineItem(row) {
        this.purchaseInvoice.purchaseInvoiceLines.splice(row, 1);
    }

    updateLineItem(row, targetProperty, value) {
        if (this.purchaseInvoice.purchaseInvoiceLines.length > 0)
            this.purchaseInvoice.purchaseInvoiceLines[row][targetProperty] = value;

        this.computeTotals();
    }

    getLineTotal(row) {
        let lineSum = 0;
        let lineItem = this.purchaseInvoice.purchaseInvoiceLines[row];
        lineSum = (lineItem.quantity * lineItem.amount) - lineItem.discount;
        return lineSum;
    }
}