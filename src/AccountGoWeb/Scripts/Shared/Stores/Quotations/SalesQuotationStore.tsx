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

    constructor() {
        this.salesQuotation = new SalesQuotation();
        extendObservable(this.salesQuotation, {
            customerId: this.salesQuotation.customerId,
            quotationDate: this.salesQuotation.quotationDate,
            paymentTermId: this.salesQuotation.paymentTermId,
            referenceNo: this.salesQuotation.referenceNo,
            salesQuotationLines: []
        });

        this.commonStore = new CommonStore();
    }

    saveNewQuotation() {
        //console.log(JSON.stringify(this.salesQuotation));
        axios.post(Config.apiUrl + "api/sales/addquotation", JSON.stringify(this.salesQuotation),
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