import {observable, extendObservable, action, autorun, computed} from 'mobx';
import * as axios from "axios";

import Config = require("Config");

import SalesOrder from './SalesOrder';
import SalesOrderLine from './SalesOrderLine';

import CommonStore from "../Common/CommonStore";

let baseUrl = location.protocol
    + "//" + location.hostname
    + (location.port && ":" + location.port)
    + "/";

export default class SalesOrderStore {
    salesOrder;
    commonStore;
    @observable validationErrors;

    constructor(quotationId, orderId) {
        this.commonStore = new CommonStore();
        this.salesOrder = new SalesOrder();
        extendObservable(this.salesOrder, {
            customerId: this.salesOrder.customerId,
            orderDate: this.salesOrder.orderDate,
            paymentTermId: this.salesOrder.paymentTermId,
            referenceNo: this.salesOrder.referenceNo,
            salesOrderLines: []
        });

        if (quotationId !== undefined) {
            var result = axios.get(Config.apiUrl + "api/sales/quotation?id=" + quotationId);
            result.then(function (result) {
                this.changedCustomer(result.data.customerId);
                this.salesOrder.paymentTermId = result.data.paymentTermId;
                this.salesOrder.referenceNo = result.data.referenceNo;
                this.changedOrderDate(result.data.quotationDate);
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
                console.log(this.salesQuotation);
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

        autorun(() => this.computeTotals());
    }

    @observable RTotal = 0;
    @observable GTotal = 0;
    @observable TTotal = 0;

    async computeTotals() {
        var rtotal = 0;
        var ttotal = 0;
        var gtotal = 0;

        for (var i = 0; i < this.salesOrder.salesOrderLines.length; i++) {
            var lineItem = this.salesOrder.salesOrderLines[i];
            var lineSum = lineItem.quantity * lineItem.amount;
            rtotal = rtotal + lineSum;
            await axios.get(Config.apiUrl + "api/tax/gettax?itemId=" + lineItem.itemId + "&partyId=" + this.salesOrder.customerId + "&type=1")
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

    async saveNewSalesOrder() {
        if (this.validation() && this.validationErrors.length === 0) {
            await axios.post(Config.apiUrl + "api/sales/savesalesorder", JSON.stringify(this.salesOrder),
                {
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
                }.bind(this))
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

    addLineItem(id = 0, itemId, measurementId, quantity, amount, discount) {
        var newLineItem = new SalesOrderLine(id, itemId, measurementId, quantity, amount, discount);
        this.salesOrder.salesOrderLines.push(extendObservable(newLineItem, newLineItem));        
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
}