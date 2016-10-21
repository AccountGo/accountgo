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
    salesInvoice;
    commonStore;
    @observable validationErrors;
    @observable editMode = false;


    constructor(orderId, invoiceId) {
        this.commonStore = new CommonStore();
        this.salesInvoice = new SalesInvoice();
        extendObservable(this.salesInvoice, {
            customerId: this.salesInvoice.customerId,
            invoiceDate: this.salesInvoice.invoiceDate,
            paymentTermId: this.salesInvoice.paymentTermId,
            referenceNo: this.salesInvoice.referenceNo,
            posted: this.salesInvoice.posted,
            readyForPosting: this.salesInvoice.readyForPosting,
            salesInvoiceLines: []
        });


        autorun(() => this.computeTotals());

        if (orderId !== undefined) {
            var result = axios.get(Config.apiUrl + "api/sales/salesorder?id=" + orderId);
            result.then(function(result) {

                for (var i = 0; i < result.data.salesOrderLines.length; i++) {
                    if (result.data.salesOrderLines[i].remainingQtyToInvoice == 0)
                        continue;
                    this.addLineItem(
                        result.data.salesOrderLines[i].id,
                        result.data.salesOrderLines[i].itemId,
                        result.data.salesOrderLines[i].measurementId,
                        result.data.salesOrderLines[i].remainingQtyToInvoice,
                        result.data.salesOrderLines[i].amount,
                        result.data.salesOrderLines[i].discount
                    );
                }
                this.salesInvoice.fromSalesOrderId = orderId;
                this.changedCustomer(result.data.customerId);
                this.salesInvoice.paymentTermId = result.data.paymentTermId;
                this.salesInvoice.referenceNo = result.data.referenceNo;
                this.salesInvoice.invoiceDate = result.data.orderDate;
                this.computeTotals();
                this.changedEditMode(true);

            }.bind(this));
        } else if (invoiceId !== undefined) {
            var result = axios.get(Config.apiUrl + "api/sales/salesinvoice?id=" + invoiceId);
            result.then(function(result) {
                for (var i = 0; i < result.data.salesInvoiceLines.length; i++) {
                    this.addLineItem(
                        result.data.salesInvoiceLines[i].id,
                        result.data.salesInvoiceLines[i].itemId,
                        result.data.salesInvoiceLines[i].measurementId,
                        result.data.salesInvoiceLines[i].quantity,
                        result.data.salesInvoiceLines[i].amount,
                        result.data.salesInvoiceLines[i].discount
                    );
                    this.updateLineItem(i, 'code', this.changeItemCode(result.data.salesInvoiceLines[i].itemId));
                }

                this.salesInvoice.id = result.data.id;
                this.changedCustomer(result.data.customerId);
                this.salesInvoice.paymentTermId = result.data.paymentTermId;
                this.salesInvoice.referenceNo = result.data.referenceNo;
                this.salesInvoice.invoiceDate = result.data.invoiceDate;
                this.salesInvoice.posted = result.data.posted;
                this.salesInvoice.readyForPosting = result.data.readyForPosting;
                this.computeTotals();

                var nodes = document.getElementById("divSalesInvoiceForm").getElementsByTagName('*');
                for (var i = 0; i < nodes.length; i++) {
                    nodes[i].className += " disabledControl";
                }
            }.bind(this));
        }
        else
            this.changedEditMode(true);           
    }


    @observable RTotal = 0;
    @observable GTotal = 0;
    @observable TTotal = 0;

    computeTotals() {
        var rtotal = 0;
        var ttotal = 0;
        var gtotal = 0;

        for (var i = 0; i < this.salesInvoice.salesInvoiceLines.length; i++) {
            var lineItem = this.salesInvoice.salesInvoiceLines[i];
            rtotal = rtotal + this.getLineTotal(i);
            axios.get(Config.apiUrl + "api/tax/gettax?itemId=" + lineItem.itemId + "&partyId=" + this.salesInvoice.customerId + "&type=1")
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

    saveNewSalesInvoice() {

        if (this.salesInvoice.invoiceDate === undefined)
            this.salesInvoice.invoiceDate = new Date(Date.now()).toISOString().substring(0, 10);

        if (this.validation() && this.validationErrors.length === 0) {
            axios.post(Config.apiUrl + "api/sales/savesalesinvoice", JSON.stringify(this.salesInvoice),
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

    printInvoice() {
        var w = 800;
        var h = 600;
        var wLeft = window.screenLeft ? window.screenLeft : window.screenX;
        var wTop = window.screenTop ? window.screenTop : window.screenY;

        var left = wLeft + (window.innerWidth / 2) - (w / 2);
        var top = wTop + (window.innerHeight / 2) - (h / 2);
        window.open(baseUrl + 'sales/salesinvoicepdf?id=' + this.salesInvoice.id, "_blank", 'toolbar=no, location=no, directories=no, status=no, menubar=no, scrollbars=no, resizable=no, copyhistory=no, width=' + w + ', height=' + h + ', top=' + top + ', left=' + left);
    }

    postInvoice() {
        if (this.validation() && this.validationErrors.length === 0) {
            axios.post(Config.apiUrl + "api/sales/postsalesinvoice", JSON.stringify(this.salesInvoice),
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

    validationLine() {
        this.validationErrors = [];
        if (this.salesInvoice.salesInvoiceLines !== undefined && this.salesInvoice.salesInvoiceLines.length > 0) {
            for (var i = 0; i < this.salesInvoice.salesInvoiceLines.length; i++) {
                if (this.salesInvoice.salesInvoiceLines[i].itemId === undefined)
                    this.validationErrors.push("Item is required.");
                if (this.salesInvoice.salesInvoiceLines[i].measurementId === undefined)
                    this.validationErrors.push("Uom is required.");
                if (this.salesInvoice.salesInvoiceLines[i].quantity === undefined)
                    this.validationErrors.push("Quantity is required.");
                if (this.salesInvoice.salesInvoiceLines[i].amount === undefined)
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


    changedReferenceNo(refNo) {
        this.salesInvoice.referenceNo = refNo;
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

    addLineItem(id, itemId, measurementId, quantity, amount, discount, code) {
        var newLineItem = new SalesInvoiceLine(id, itemId, measurementId, quantity, amount, discount, code);
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

    changedEditMode(editMode) {
        this.editMode = editMode;
    }

    changeItemCode(itemId) {

        for (var x = 0; x < this.commonStore.items.length; x++) {
            if (this.commonStore.items[x].id === parseInt(itemId)) {
                return this.commonStore.items[x].code;
            }
        }
    }
}