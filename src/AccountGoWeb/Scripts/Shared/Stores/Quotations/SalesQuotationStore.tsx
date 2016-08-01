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
    salesQuotation: SalesQuotation;
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
            var result = axios.get(Config.apiUrl + "api/sales/quotation?id=" + quotationId);
            result.then(function (result) {                
                this.changedCustomer(result.data.customerId);
                this.changedQuotationDate(result.data.quotationDate);
                for (var i = 0; i < result.data.salesQuotationLines.length; i++) {
                    this.addLineItem(
                        result.data.salesQuotationLines[i].itemId,
                        result.data.salesQuotationLines[i].measurementId,
                        result.data.salesQuotationLines[i].quantity,
                        result.data.salesQuotationLines[i].amount,
                        result.data.salesQuotationLines[i].discount
                    );
                }
                console.log(this.salesQuotation);
            }.bind(this));
        }
    }

    saveNewQuotation() {
        this.validationErrors = [];
        if (this.salesQuotation.customerId === undefined)
            this.validationErrors.push("Customer is required.");
        if (this.salesQuotation.paymentTermId === undefined)
            this.validationErrors.push("Payment term is required.");
        if (this.salesQuotation.quotationDate === undefined)
            this.validationErrors.push("Date is required.");
        if (this.salesQuotation.salesQuotationLines === undefined || this.salesQuotation.salesQuotationLines.length < 1)
            this.validationErrors.push("Enter at least 1 line item.");
        if (this.salesQuotation.salesQuotationLines !== undefined && this.salesQuotation.salesQuotationLines.length > 0) {
            for (var i = 0; i < this.salesQuotation.salesQuotationLines.length; i++) {
                if (this.salesQuotation.salesQuotationLines[i].itemId === undefined
                    || this.salesQuotation.salesQuotationLines[i].itemId === "")
                    this.validationErrors.push("Item is required.");
                if (this.salesQuotation.salesQuotationLines[i].measurementId === undefined
                    || this.salesQuotation.salesQuotationLines[i].measurementId === "")
                    this.validationErrors.push("Uom is required.");
                if (this.salesQuotation.salesQuotationLines[i].quantity === undefined
                    || this.salesQuotation.salesQuotationLines[i].quantity === ""
                    || this.salesQuotation.salesQuotationLines[i].quantity === 0)
                    this.validationErrors.push("Quantity is required.");
                if (this.salesQuotation.salesQuotationLines[i].amount === undefined
                    || this.salesQuotation.salesQuotationLines[i].amount === ""
                    || this.salesQuotation.salesQuotationLines[i].amount === 0)
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

    addLineItem(id = 0, itemId, measurementId, quantity, amount, discount) {
        var newLineItem = new SalesQuotationLine(id, itemId, measurementId, quantity, amount, discount);
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