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
    salesOrder: SalesOrder;
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
                this.changedOrderDate(result.data.quotationDate);
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
        else if (orderId !== undefined) {
            var result = axios.get(Config.apiUrl + "api/sales/salesorder?id=" + orderId);
            result.then(function (result) {
                this.changedCustomer(result.data.customerId);
                //this.changedOrderDate(result.data.orderDate);
                for (var i = 0; i < result.data.salesOrderLines.length; i++) {
                    this.addLineItem(result.data.salesOrderLines[i].itemId,
                        result.data.salesOrderLines[i].measurementId,
                        result.data.salesOrderLines[i].quantity,
                        result.data.salesOrderLines[i].amount,
                        result.data.salesOrderLines[i].discount
                    );
                }
            }.bind(this));
        }
    }

    saveNewSalesOrder() {
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
                if (this.salesOrder.salesOrderLines[i].itemId === undefined
                    || this.salesOrder.salesOrderLines[i].itemId === "")
                    this.validationErrors.push("Item is required.");
                if (this.salesOrder.salesOrderLines[i].measurementId === undefined
                    || this.salesOrder.salesOrderLines[i].measurementId === "")
                    this.validationErrors.push("Uom is required.");
                if (this.salesOrder.salesOrderLines[i].quantity === undefined
                    || this.salesOrder.salesOrderLines[i].quantity === ""
                    || this.salesOrder.salesOrderLines[i].quantity === 0)
                    this.validationErrors.push("Quantity is required.");
                if (this.salesOrder.salesOrderLines[i].amount === undefined
                    || this.salesOrder.salesOrderLines[i].amount === ""
                    || this.salesOrder.salesOrderLines[i].amount === 0)
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