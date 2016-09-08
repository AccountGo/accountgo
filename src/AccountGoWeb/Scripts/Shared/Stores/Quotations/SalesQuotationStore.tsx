import {observable, extendObservable, action, autorun, computed} from 'mobx';
import * as axios from "axios";
import * as d3 from "d3";

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

        autorun(() => this.computeTotals());

        if (quotationId !== undefined) {
            var result = axios.get(Config.apiUrl + "api/sales/quotation?id=" + quotationId);
            result.then(function (result) {
                this.salesQuotation.id = result.data.id;
                this.salesQuotation.paymentTermId = result.data.paymentTermId;
                this.salesQuotation.referenceNo = result.data.referenceNo;
                this.changedCustomer(result.data.customerId);
                this.changedQuotationDate(result.data.quotationDate);
                for (var i = 0; i < result.data.salesQuotationLines.length; i++) {
                    this.addLineItem(
                        result.data.salesQuotationLines[i].id,
                        result.data.salesQuotationLines[i].itemId,
                        result.data.salesQuotationLines[i].measurementId,
                        result.data.salesQuotationLines[i].quantity,
                        result.data.salesQuotationLines[i].amount,
                        result.data.salesQuotationLines[i].discount
                    );
                }
                this.computeTotals();
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
       
        for (var i = 0; i < this.salesQuotation.salesQuotationLines.length; i++) {
            var lineItem = this.salesQuotation.salesQuotationLines[i];
            rtotal = rtotal + this.getLineTotal(i);
            axios.get(Config.apiUrl + "api/tax/gettax?itemId=" + lineItem.itemId + "&partyId=" + this.salesQuotation.customerId + "&type=1")
                .then(function (result) {
                    if (result.data.length > 0) {
                        ttotal = ttotal + this.commonStore.getSalesLineTaxAmount(lineItem.quantity, lineItem.amount, lineItem.discount, result.data);
                    }                    
                    this.TTotal = ttotal;
                    this.GTotal = rtotal - ttotal;
                }.bind(this));
            this.RTotal = rtotal;            
        }
    }

    saveNewQuotation() {
        if (this.validation()) {
            if (this.validationErrors.length === 0) {
                axios.post(Config.apiUrl + "api/sales/savequotation", JSON.stringify(this.salesQuotation),
                    {
                        headers:
                        {
                            'Content-type': 'application/json'
                        }
                    }
                )
                    .then(function (response) {
                        window.location.href = baseUrl + 'quotations';
                    })
                    .catch(function (error) {
                        error.data.map(function (err) {
                            this.validationErrors.push(err);
                        }.bind(this));
                    }.bind(this));
            }
        }
    }

    validation() {
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
                if (this.salesQuotation.salesQuotationLines[i].itemId === undefined)
                    this.validationErrors.push("Item is required.");
                if (this.salesQuotation.salesQuotationLines[i].measurementId === undefined)
                    this.validationErrors.push("Uom is required.");
                if (this.salesQuotation.salesQuotationLines[i].quantity === undefined)
                    this.validationErrors.push("Quantity is required.");
                if (this.salesQuotation.salesQuotationLines[i].amount === undefined)
                    this.validationErrors.push("Amount is required.");
                if (this.getLineTotal(i) === undefined
                    || this.getLineTotal(i).toString() === "NaN")
                    this.validationErrors.push("Invalid data.");
            }
        }

        return this.validationErrors.length === 0;
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
    changedReferenceNo(refNo) {
        this.salesQuotation.referenceNo = refNo;
    }
    addLineItem(id, itemId, measurementId, quantity, amount, discount) {
        var newLineItem = new SalesQuotationLine(id, itemId, measurementId, quantity, amount, discount);
        this.salesQuotation.salesQuotationLines.push(extendObservable(newLineItem, newLineItem));
    }

    removeLineItem(row) {
        this.salesQuotation.salesQuotationLines.splice(row, 1);
    }

    updateLineItem(row, targetProperty, value) {
        //if (this.salesQuotation.salesQuotationLines.length > 0)
            this.salesQuotation.salesQuotationLines[row][targetProperty] = value;

        this.computeTotals();        
    }

    getLineTotal(row) {
        let lineSum = 0;
        let lineItem = this.salesQuotation.salesQuotationLines[row];
        lineSum = (lineItem.quantity * lineItem.amount) - lineItem.discount;
        return lineSum;
    }
}