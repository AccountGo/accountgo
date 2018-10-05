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
const SalesOrder_1 = require("./SalesOrder");
const SalesOrderLine_1 = require("./SalesOrderLine");
const CommonStore_1 = require("../Common/CommonStore");
let baseUrl = location.protocol
    + "//" + location.hostname
    + (location.port && ":" + location.port)
    + "/";
class SalesOrderStore {
    constructor(quotationId, orderId) {
        this.editMode = false;
        this.hasQuotation = false;
        this.RTotal = 0;
        this.GTotal = 0;
        this.TTotal = 0;
        this.commonStore = new CommonStore_1.default();
        this.salesOrder = new SalesOrder_1.default();
        mobx_1.extendObservable(this.salesOrder, {
            customerId: this.salesOrder.customerId,
            orderDate: this.salesOrder.orderDate,
            paymentTermId: this.salesOrder.paymentTermId,
            referenceNo: this.salesOrder.referenceNo,
            statusId: this.salesOrder.statusId,
            salesOrderLines: []
        });
        mobx_1.autorun(() => this.computeTotals());
        if (quotationId !== undefined) {
            var result = axios.get(Config.apiUrl + "api/sales/quotation?id=" + quotationId);
            result.then(function (result) {
                this.changedCustomer(result.data.customerId);
                this.salesOrder.paymentTermId = result.data.paymentTermId;
                this.salesOrder.referenceNo = result.data.referenceNo;
                this.changedOrderDate(result.data.quotationDate);
                this.getQuotationStatus(result.data.statusId);
                this.hasQuotation = true;
                this.salesOrder.quotationId = quotationId;
                for (var i = 0; i < result.data.salesQuotationLines.length; i++) {
                    this.addLineItem(result.data.salesQuotationLines[i].id, result.data.salesQuotationLines[i].itemId, result.data.salesQuotationLines[i].measurementId, result.data.salesQuotationLines[i].quantity, result.data.salesQuotationLines[i].amount, result.data.salesQuotationLines[i].discount);
                }
                this.computeTotals();
                var nodes = document.getElementById("divSalesOrderForm").getElementsByTagName('*');
                for (var i = 0; i < nodes.length; i++) {
                    nodes[i].className += " disabledControl";
                }
            }.bind(this));
        }
        else if (orderId !== undefined) {
            var result = axios.get(Config.apiUrl + "api/sales/salesorder?id=" + orderId);
            result.then(function (result) {
                this.salesOrder.id = result.data.id;
                this.changedCustomer(result.data.customerId);
                this.salesOrder.paymentTermId = result.data.paymentTermId;
                this.salesOrder.referenceNo = result.data.referenceNo;
                this.changedOrderDate(result.data.orderDate);
                this.getOrderStatus(result.data.statusId);
                for (var i = 0; i < result.data.salesOrderLines.length; i++) {
                    this.addLineItem(result.data.salesOrderLines[i].id, result.data.salesOrderLines[i].itemId, result.data.salesOrderLines[i].measurementId, result.data.salesOrderLines[i].quantity, result.data.salesOrderLines[i].amount, result.data.salesOrderLines[i].discount);
                    this.updateLineItem(i, 'code', this.changeItemCode(result.data.salesOrderLines[i].itemId));
                }
                this.computeTotals();
                var nodes = document.getElementById("divSalesOrderForm").getElementsByTagName('*');
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
        for (var i = 0; i < this.salesOrder.salesOrderLines.length; i++) {
            var lineItem = this.salesOrder.salesOrderLines[i];
            rtotal = rtotal + this.getLineTotal(i);
            axios.get(Config.apiUrl + "api/tax/gettax?itemId=" + lineItem.itemId + "&partyId=" + this.salesOrder.customerId + "&type=1")
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
    saveNewSalesOrder() {
        if (this.salesOrder.orderDate === undefined)
            this.salesOrder.orderDate = new Date(Date.now()).toISOString().substring(0, 10);
        if (this.validation() && this.validationErrors.length === 0) {
            axios.post(Config.apiUrl + "api/sales/savesalesorder", JSON.stringify(this.salesOrder), {
                headers: {
                    'Content-type': 'application/json'
                }
            })
                .then(function (response) {
                window.location.href = baseUrl + 'sales/salesorders';
            })
                .catch(function (error) {
                error.data.map(function (err) {
                    this.validationErrors.push(err);
                }.bind(this));
            }.bind(this));
        }
    }
    validation() {
        this.validationErrors = [];
        if (this.salesOrder.customerId === undefined)
            this.validationErrors.push("Customer is required.");
        if (this.salesOrder.paymentTermId === undefined)
            this.validationErrors.push("Payment term is required.");
        if (this.salesOrder.orderDate === undefined)
            this.validationErrors.push("Date is required.");
        if (this.salesOrder.salesOrderLines === undefined || this.salesOrder.salesOrderLines.length < 1)
            this.validationErrors.push("Enter at least 1 line item.");
        if (this.salesOrder.salesOrderLines !== undefined && this.salesOrder.salesOrderLines.length > 0) {
            for (var i = 0; i < this.salesOrder.salesOrderLines.length; i++) {
                if (this.salesOrder.salesOrderLines[i].itemId === undefined)
                    this.validationErrors.push("Item is required.");
                if (this.salesOrder.salesOrderLines[i].measurementId === undefined)
                    this.validationErrors.push("Uom is required.");
                if (this.salesOrder.salesOrderLines[i].quantity === undefined
                    || this.salesOrder.salesOrderLines[i].quantity === 0)
                    this.validationErrors.push("Quantity is required.");
                if (this.salesOrder.salesOrderLines[i].amount === undefined
                    || this.salesOrder.salesOrderLines[i].amount === 0)
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
        if (this.salesOrder.salesOrderLines !== undefined && this.salesOrder.salesOrderLines.length > 0) {
            for (var i = 0; i < this.salesOrder.salesOrderLines.length; i++) {
                if (this.salesOrder.salesOrderLines[i].itemId === undefined)
                    this.validationErrors.push("Item is required.");
                if (this.salesOrder.salesOrderLines[i].measurementId === undefined)
                    this.validationErrors.push("Uom is required.");
                if (this.salesOrder.salesOrderLines[i].quantity === undefined)
                    this.validationErrors.push("Quantity is required.");
                if (this.salesOrder.salesOrderLines[i].amount === undefined)
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
    getOrderStatus(statusId) {
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
        else if (statusId === 5)
            status = "Partially Invoiced";
        else if (statusId === 6)
            status = "Fully Invoiced";
        this.salesOrderStatus = status;
    }
    changeItemCode(itemId) {
        for (var x = 0; x < this.commonStore.items.length; x++) {
            if (this.commonStore.items[x].id === parseInt(itemId)) {
                return this.commonStore.items[x].code;
            }
        }
    }
    changedReferenceNo(refNo) {
        this.salesOrder.referenceNo = refNo;
    }
    changedCustomer(custId) {
        this.salesOrder.customerId = custId;
    }
    changedOrderDate(date) {
        this.salesOrder.orderDate = date;
    }
    changedPaymentTerm(termId) {
        this.salesOrder.paymentTermId = termId;
    }
    addLineItem(id, itemId, measurementId, quantity, amount, discount, code) {
        var newLineItem = new SalesOrderLine_1.default(id, itemId, measurementId, quantity, amount, discount, code);
        this.salesOrder.salesOrderLines.push(mobx_1.extendObservable(newLineItem, newLineItem));
    }
    removeLineItem(row) {
        this.salesOrder.salesOrderLines.splice(row, 1);
    }
    updateLineItem(row, targetProperty, value) {
        if (this.salesOrder.salesOrderLines.length > 0)
            this.salesOrder.salesOrderLines[row][targetProperty] = value;
        this.computeTotals();
    }
    getLineTotal(row) {
        let lineSum = 0;
        let lineItem = this.salesOrder.salesOrderLines[row];
        lineSum = (lineItem.quantity * lineItem.amount) - lineItem.discount;
        return lineSum;
    }
    changedEditMode(editMode) {
        this.editMode = editMode;
    }
}
__decorate([
    mobx_1.observable
], SalesOrderStore.prototype, "validationErrors", void 0);
__decorate([
    mobx_1.observable
], SalesOrderStore.prototype, "salesOrderStatus", void 0);
__decorate([
    mobx_1.observable
], SalesOrderStore.prototype, "salesQuotationStatus", void 0);
__decorate([
    mobx_1.observable
], SalesOrderStore.prototype, "editMode", void 0);
__decorate([
    mobx_1.observable
], SalesOrderStore.prototype, "hasQuotation", void 0);
__decorate([
    mobx_1.observable
], SalesOrderStore.prototype, "RTotal", void 0);
__decorate([
    mobx_1.observable
], SalesOrderStore.prototype, "GTotal", void 0);
__decorate([
    mobx_1.observable
], SalesOrderStore.prototype, "TTotal", void 0);
exports.default = SalesOrderStore;
//# sourceMappingURL=SalesOrderStore.js.map