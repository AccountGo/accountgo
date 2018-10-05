"use strict";
var __decorate = (this && this.__decorate) || function (decorators, target, key, desc) {
    var c = arguments.length, r = c < 3 ? target : desc === null ? desc = Object.getOwnPropertyDescriptor(target, key) : desc, d;
    if (typeof Reflect === "object" && typeof Reflect.decorate === "function") r = Reflect.decorate(decorators, target, key, desc);
    else for (var i = decorators.length - 1; i >= 0; i--) if (d = decorators[i]) r = (c < 3 ? d(r) : c > 3 ? d(target, key, r) : d(target, key)) || r;
    return c > 3 && r && Object.defineProperty(target, key, r), r;
};
Object.defineProperty(exports, "__esModule", { value: true });
const mobx_1 = require("mobx");
const axios = require("axios");
const Config = require("Config");
const SalesQuotation_1 = require("./SalesQuotation");
const SalesQuotationLine_1 = require("./SalesQuotationLine");
const CommonStore_1 = require("../Common/CommonStore");
let baseUrl = location.protocol
    + "//" + location.hostname
    + (location.port && ":" + location.port)
    + "/";
class SalesQuotationStore {
    constructor(quotationId) {
        this.editMode = false;
        this.RTotal = 0;
        this.GTotal = 0;
        this.TTotal = 0;
        this.commonStore = new CommonStore_1.default();
        this.salesQuotation = new SalesQuotation_1.default();
        mobx_1.extendObservable(this.salesQuotation, {
            customerId: this.salesQuotation.customerId,
            quotationDate: this.salesQuotation.quotationDate,
            paymentTermId: this.salesQuotation.paymentTermId,
            referenceNo: this.salesQuotation.referenceNo,
            statusId: this.salesQuotation.statusId,
            salesQuotationLines: []
        });
        mobx_1.autorun(() => this.computeTotals());
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
                    this.addLineItem(result.data.salesQuotationLines[i].id, result.data.salesQuotationLines[i].itemId, result.data.salesQuotationLines[i].measurementId, result.data.salesQuotationLines[i].quantity, result.data.salesQuotationLines[i].amount, result.data.salesQuotationLines[i].discount);
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
                axios.post(Config.apiUrl + "api/sales/savequotation", JSON.stringify(this.salesQuotation), {
                    headers: {
                        'Content-type': 'application/json'
                    }
                })
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
                axios.post(Config.apiUrl + "api/sales/bookquotation?id=" + parseInt(this.salesQuotation.id), {
                    headers: {
                        'Content-type': 'application/json'
                    }
                })
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
            itemId = document.getElementById("optNewItemId").value;
            measurementId = document.getElementById("optNewMeasurementId").value;
            quantity = document.getElementById("txtNewQuantity").value;
            amount = document.getElementById("txtNewAmount").value;
            discount = document.getElementById("txtNewDiscount").value;
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
            itemId = document.getElementById("optNewItemId").value;
            measurementId = document.getElementById("optNewMeasurementId").value;
            quantity = document.getElementById("txtNewQuantity").value;
            amount = document.getElementById("txtNewAmount").value;
            discount = document.getElementById("txtNewDiscount").value;
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
        var newLineItem = new SalesQuotationLine_1.default(id, itemId, measurementId, quantity, amount, discount, code);
        this.salesQuotation.salesQuotationLines.push(mobx_1.extendObservable(newLineItem, newLineItem));
    }
    removeLineItem(row) {
        this.salesQuotation.salesQuotationLines.splice(row, 1);
    }
    updateLineItem(row, targetProperty, value) {
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
__decorate([
    mobx_1.observable
], SalesQuotationStore.prototype, "salesQuotationStatus", void 0);
__decorate([
    mobx_1.observable
], SalesQuotationStore.prototype, "validationErrors", void 0);
__decorate([
    mobx_1.observable
], SalesQuotationStore.prototype, "editMode", void 0);
__decorate([
    mobx_1.observable
], SalesQuotationStore.prototype, "RTotal", void 0);
__decorate([
    mobx_1.observable
], SalesQuotationStore.prototype, "GTotal", void 0);
__decorate([
    mobx_1.observable
], SalesQuotationStore.prototype, "TTotal", void 0);
exports.default = SalesQuotationStore;
//# sourceMappingURL=SalesQuotationStore.js.map