import {observable, extendObservable, action} from 'mobx';
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
            axios.get(Config.apiUrl + "api/sales/quotation?id=" + quotationId)
                .then(function (result) {
                    this.changedCustomer(result.data.customerId);
                    this.changedOrderDate(result.data.quotationDate);
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
        else if (orderId !== undefined) {
            axios.get(Config.apiUrl + "api/sales/salesorder?id=" + orderId)
                .then(function (result) {
                    this.changedCustomer(result.data.customerId);
                    this.changedOrderDate(result.data.orderDate);
                    for (var i = 0; i < result.data.SalesOrderLines.length; i++) {
                        this.addLineItem(result.data.SalesOrderLines[i].itemId,
                            result.data.SalesOrderLines[i].measurementId,
                            result.data.SalesOrderLines[i].quantity,
                            result.data.SalesOrderLines[i].amount,
                            result.data.SalesOrderLines[i].discount
                        );
                    }
                }.bind(this))
                .catch(function (error) {
                }.bind(this));
        }
    }

    saveNewSalesOrder() {
        this.validationErrors = [];
        if (this.salesOrder.customerId === undefined || this.salesOrder.customerId === "")
            this.validationErrors.push("Customer is required.");
        if (this.salesOrder.paymentTermId === undefined || this.salesOrder.paymentTermId === "")
            this.salesOrder.push("Payment term is required.");
        if (this.salesOrder.orderDate === undefined || this.salesOrder.orderDate === "")
            this.validationErrors.push("Date is required.");
        if (this.salesOrder.purchaseOrderLines === undefined || this.salesOrder.purchaseOrderLines.length < 1)
            this.validationErrors.push("Enter at least 1 line item.");
        if (this.salesOrder.purchaseOrderLines !== undefined && this.salesOrder.purchaseOrderLines.length > 0) {
            for (var i = 0; i < this.salesOrder.purchaseOrderLines.length; i++) {
                if (this.salesOrder.purchaseOrderLines[i].itemId === undefined
                    || this.salesOrder.purchaseOrderLines[i].itemId === "")
                    this.validationErrors.push("Item is required.");
                if (this.salesOrder.purchaseOrderLines[i].measurementId === undefined
                    || this.salesOrder.purchaseOrderLines[i].measurementId === "")
                    this.validationErrors.push("Uom is required.");
                if (this.salesOrder.purchaseOrderLines[i].quantity === undefined
                    || this.salesOrder.purchaseOrderLines[i].quantity === ""
                    || this.salesOrder.purchaseOrderLines[i].quantity === 0)
                    this.validationErrors.push("Quantity is required.");
                if (this.salesOrder.purchaseOrderLines[i].amount === undefined
                    || this.salesOrder.purchaseOrderLines[i].amount === ""
                    || this.salesOrder.purchaseOrderLines[i].amount === 0)
                    this.validationErrors.push("Amount is required.");
                if (this.lineTotal(i) === undefined
                    || this.lineTotal(i).toString() === "NaN"
                    || this.lineTotal(i) === 0)
                    this.validationErrors.push("Invalid data.");
            }
        }

        if (this.validationErrors.length === 0) {
            axios.post(Config.apiUrl + "api/sales/savesalesorder", JSON.stringify(this.salesOrder),
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
        this.salesOrder.customerId = custId;
    }

    changedOrderDate(date) {
        this.salesOrder.orderDate = date;
    }

    addLineItem(itemId, measurementId, quantity, amount, discount) {
        var newLineItem = new SalesOrderLine(itemId, measurementId, quantity, amount, discount);
        this.salesOrder.salesOrderLines.push(extendObservable(newLineItem, newLineItem));        
    }

    removeLineItem(row) {
        this.salesOrder.salesOrderLines.splice(row, 1);
    }

    updateLineItem(row, targetProperty, value) {
        if (this.salesOrder.salesOrderLines.length > 0)
            this.salesOrder.salesOrderLines[row][targetProperty] = value;
    }

    grandTotal() {
        var sum = 0;
        for (var i = 0; i < this.salesOrder.salesOrderLines.length; i++) {
            var lineSum = this.salesOrder.salesOrderLines[i].quantity * this.salesOrder.salesOrderLines[i].amount;
            sum = sum + lineSum;
        }
        return sum;
    }

    lineTotal(row) {
        var lineSum = this.salesOrder.salesOrderLines[row].quantity * this.salesOrder.salesOrderLines[row].amount;;
        return lineSum;
    }
}