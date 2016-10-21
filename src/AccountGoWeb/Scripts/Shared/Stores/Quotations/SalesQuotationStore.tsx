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
    @observable salesQuotationStatus;
    @observable validationErrors;
    @observable editMode = false;

    constructor(quotationId) {
        this.commonStore = new CommonStore();
        this.salesQuotation = new SalesQuotation();
        extendObservable(this.salesQuotation, {
            customerId: this.salesQuotation.customerId,
            quotationDate: this.salesQuotation.quotationDate,
            paymentTermId: this.salesQuotation.paymentTermId,
            referenceNo: this.salesQuotation.referenceNo,
            statusId: this.salesQuotation.statusId,
            salesQuotationLines: []
        });



        autorun(() => this.computeTotals());

        if (quotationId !== undefined) {
            var result = axios.get(Config.apiUrl + "api/sales/quotation?id=" + quotationId);
            result.then(function (result) {
                this.salesQuotation.id = result.data.id;
                this.salesQuotation.paymentTermId = result.data.paymentTermId;
                this.salesQuotation.referenceNo = result.data.referenceNo;
                this.salesQuotation.statusId = result.data.statusId;
                this.changedCustomer(result.data.customerId);
                this.getQuotationStatus(result.data.statusId);
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
                    this.updateLineItem(i, 'code', this.changeItemCode(result.data.salesQuotationLines[i].itemId));
                }
                this.computeTotals();
                var nodes = document.getElementById("divSalesQuotationForm").getElementsByTagName('*');
                for (var i = 0; i < nodes.length; i++) {
                    nodes[i].className += " disabledControl";
                }
            }.bind(this));
        }
        else {
            this.changedEditMode(true);
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
                    this.GTotal = rtotal + ttotal;
                }.bind(this));
            this.RTotal = rtotal;
        }
    }

    saveNewQuotation() {

        if (this.salesQuotation.quotationDate === undefined)
            this.salesQuotation.quotationDate = new Date(Date.now()).toISOString().substring(0, 10);

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

    bookQuotation() {

        if (this.validation()) {
            if (this.validationErrors.length === 0) {
                axios.post(Config.apiUrl + "api/sales/bookquotation?id=" + parseInt(this.salesQuotation.id),
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
                if (this.salesQuotation.salesQuotationLines[i].itemId === undefined || this.salesQuotation.salesQuotationLines[i].itemId === "")
                    this.validationErrors.push("Item is required.");
                if (this.salesQuotation.salesQuotationLines[i].measurementId === undefined || this.salesQuotation.salesQuotationLines[i].measurementId === "")
                    this.validationErrors.push("Uom is required.");
                if (this.salesQuotation.salesQuotationLines[i].quantity === undefined || this.salesQuotation.salesQuotationLines[i].quantity === "")
                    this.validationErrors.push("Quantity is required.");
                if (this.salesQuotation.salesQuotationLines[i].amount === undefined || this.salesQuotation.salesQuotationLines[i].amount === "")
                    this.validationErrors.push("Amount is required.");
                if (this.getLineTotal(i) === undefined
                    || this.getLineTotal(i).toString() === "NaN")
                    this.validationErrors.push("Invalid data.");
            }
        }

        return this.validationErrors.length === 0;
    }


    getQuotationStatus(statusId) {
        var status = "";
        if (statusId === 0)
            status = "Draft";
        else if (statusId === 1)
            status = "Open";
        else if (statusId === 2)
            status = "Overdue";
        else if (statusId === 3)
            status = "Closed";
        else if (statusId === 4)
            status = "Void";
        this.salesQuotationStatus = status;
    }

    //initializedQuotationDate()
    //{
    //    if (this.salesQuotation.quotationDate === undefined || this.salesQuotation.quotationDate === "")
    //        this.salesQuotation.quotationDate = new Date(Date.now()).toISOString().substring(0, 10);
    //}

    validationLine() {
        this.validationErrors = [];
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
        else {
            var itemId, measurementId, quantity, amount, discount;
            itemId = (document.getElementById("optNewItemId") as HTMLInputElement).value;
            measurementId = (document.getElementById("optNewMeasurementId") as HTMLInputElement).value;
            quantity = (document.getElementById("txtNewQuantity") as HTMLInputElement).value;
            amount = (document.getElementById("txtNewAmount") as HTMLInputElement).value;
            discount = (document.getElementById("txtNewDiscount") as HTMLInputElement).value;

            if (itemId == "" || itemId === undefined)
                this.validationErrors.push("Item is required.");
            if (measurementId == "" || measurementId === undefined)
                this.validationErrors.push("Uom is required.");
            if (quantity == "" || quantity === undefined)
                this.validationErrors.push("Quantity is required.");
            if (amount == "" || amount === undefined)
                this.validationErrors.push("Amount is required.");
        }

        if (document.getElementById("optNewItemId")) {
            var itemId, measurementId, quantity, amount, discount;
            itemId = (document.getElementById("optNewItemId") as HTMLInputElement).value;
            measurementId = (document.getElementById("optNewMeasurementId") as HTMLInputElement).value;
            quantity = (document.getElementById("txtNewQuantity") as HTMLInputElement).value;
            amount = (document.getElementById("txtNewAmount") as HTMLInputElement).value;
            discount = (document.getElementById("txtNewDiscount") as HTMLInputElement).value;

            if (itemId == "" || itemId === undefined)
                this.validationErrors.push("Item is required.");
            if (measurementId == "" || measurementId === undefined)
                this.validationErrors.push("Uom is required.");
            if (quantity == "" || quantity === undefined)
                this.validationErrors.push("Quantity is required.");
            if (amount == "" || amount === undefined)
                this.validationErrors.push("Amount is required.");
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

    changedQuoteStatus(statusId) {
        this.salesQuotation.statusId = statusId;
    }

    changeItemCode(itemId) {

        for (var x = 0; x < this.commonStore.items.length; x++) {
            if (this.commonStore.items[x].id === parseInt(itemId)) {
                return this.commonStore.items[x].code;
            }
        }
    }

    addLineItem(id, itemId, measurementId, quantity, amount, discount, code) {
        var newLineItem = new SalesQuotationLine(id, itemId, measurementId, quantity, amount, discount, code);
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

    changedEditMode(editMode) {
        this.editMode = editMode;
    }


}