import {observable, extendObservable, action} from 'mobx';
import * as axios from "axios";

import Config = require("Config");

import SalesQuotation from './SalesQuotation';
import SalesQuotationLine from './SalesQuotationLine';

import CommonStore from "../Common/CommonStore";

let baseUrl = location.protocol
    + "//" + location.hostname
    + (location.port && ":" + location.port)
    + "/";

export default class SalesQuotationStore {
    salesQuotation;
    commonStore;
    @observable validationErrors;

    constructor(quotationId) {
        this.commonStore = new CommonStore();
        this.salesQuotation = new SalesQuotation();
        extendObservable(this.salesQuotation, {
            customerId: this.salesQuotation.customerId,
            quotationDate: this.salesQuotation.quotationDate,
            paymentTermId: this.salesQuotation.paymentTermId,
            referenceNo: this.salesQuotation.referenceNo,
            salesQuotationLines: []
        });

        if (quotationId !== undefined) {
            axios.get(Config.apiUrl + "api/sales/quotation?id=" + quotationId)
                .then(function (result) {
                    this.changedCustomer(result.data.customerId);
                    this.changedQuotationDate(result.data.quotationDate);
                    for (var i = 0; i < result.data.SalesQuotationLines.length; i++) {
                        this.addLineItem(result.data.SalesQuotationLines[i].itemId,
                            result.data.SalesQuotationLines[i].measurementId,
                            result.data.SalesQuotationLines[i].quantity,
                            result.data.SalesQuotationLines[i].amount,
                            result.data.SalesQuotationLines[i].discount
                        );
                    }
                }.bind(this))
                .catch(function (error) {
                }.bind(this));
        }
    }

    saveNewQuotation() {
        this.validationErrors = [];
        if (this.salesQuotation.customerId === undefined || this.salesQuotation.customerId === "")
            this.validationErrors.push("Customer is required.");
        if (this.salesQuotation.paymentTermId === undefined || this.salesQuotation.paymentTermId === "")
            this.salesQuotation.push("Payment term is required.");
        if (this.salesQuotation.orderDate === undefined || this.salesQuotation.orderDate === "")
            this.validationErrors.push("Date is required.");
        if (this.salesQuotation.purchaseOrderLines === undefined || this.salesQuotation.purchaseOrderLines.length < 1)
            this.validationErrors.push("Enter at least 1 line item.");
        if (this.salesQuotation.purchaseOrderLines !== undefined && this.salesQuotation.purchaseOrderLines.length > 0) {
            for (var i = 0; i < this.salesQuotation.purchaseOrderLines.length; i++) {
                if (this.salesQuotation.purchaseOrderLines[i].itemId === undefined
                    || this.salesQuotation.purchaseOrderLines[i].itemId === "")
                    this.validationErrors.push("Item is required.");
                if (this.salesQuotation.purchaseOrderLines[i].measurementId === undefined
                    || this.salesQuotation.purchaseOrderLines[i].measurementId === "")
                    this.validationErrors.push("Uom is required.");
                if (this.salesQuotation.purchaseOrderLines[i].quantity === undefined
                    || this.salesQuotation.purchaseOrderLines[i].quantity === ""
                    || this.salesQuotation.purchaseOrderLines[i].quantity === 0)
                    this.validationErrors.push("Quantity is required.");
                if (this.salesQuotation.purchaseOrderLines[i].amount === undefined
                    || this.salesQuotation.purchaseOrderLines[i].amount === ""
                    || this.salesQuotation.purchaseOrderLines[i].amount === 0)
                    this.validationErrors.push("Amount is required.");
                if (this.lineTotal(i) === undefined
                    || this.lineTotal(i).toString() === "NaN"
                    || this.lineTotal(i) === 0)
                    this.validationErrors.push("Invalid data.");
            }
        }

        if (this.validationErrors.length === 0) {
            axios.post(Config.apiUrl + "api/sales/savequotation", JSON.stringify(this.salesQuotation),
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
        this.salesQuotation.customerId = custId;
    }

    changedPaymentTerm(termId) {
        this.salesQuotation.paymentTermId = termId;
    }

    changedQuotationDate(date) {
        this.salesQuotation.quotationDate = date;
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