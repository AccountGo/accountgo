import {observable, extendObservable, action} from 'mobx';
import * as axios from "axios";

import Config = require("Config");

import PurchaseOrder from './PurchaseOrder';
import PurchaseOrderLine from './PurchaseOrderLine';

import CommonStore from "../Common/CommonStore";

let baseUrl = location.protocol
    + "//" + location.hostname
    + (location.port && ":" + location.port)
    + "/";

export default class PurchaseOrderStore {
    purchaseOrder;
    commonStore;
    @observable validationErrors;

    constructor(purchId: any) {
        this.commonStore = new CommonStore();
        this.purchaseOrder = new PurchaseOrder();
        extendObservable(this.purchaseOrder, {
            vendorId: this.purchaseOrder.vendorId,
            orderDate: this.purchaseOrder.orderDate,
            paymentTermId: this.purchaseOrder.paymentTermId,
            referenceNo: this.purchaseOrder.referenceNo,
            purchaseOrderLines: []
        });

        if (purchId !== undefined) {
            axios.get(Config.apiUrl + "api/purchasing/purchaseorder?id=" + purchId)
                .then(function (result) {
                    console.log(result);
                    this.changedVendor(result.data.vendorId);
                    this.changedOrderDate(result.data.orderDate);
                    for (var i = 0; i < result.data.purchaseOrderLines.length; i++) {
                        this.addLineItem(result.data.purchaseOrderLines[i].itemId,
                            result.data.purchaseOrderLines[i].measurementId,
                            result.data.purchaseOrderLines[i].quantity,
                            result.data.purchaseOrderLines[i].amount,
                            result.data.purchaseOrderLines[i].discount
                        );
                    }
                }.bind(this))
                .catch(function (error) {
                }.bind(this));
        }
    }

    savePurchaseOrder() {
        //console.log(this.purchaseOrder);
        this.validationErrors = [];
        if (this.purchaseOrder.vendorId === undefined || this.purchaseOrder.vendorId === "")
            this.validationErrors.push("Vendor is required.");
        if (this.purchaseOrder.paymentTermId === undefined || this.purchaseOrder.paymentTermId === "")
            this.validationErrors.push("Payment term is required.");
        if (this.purchaseOrder.orderDate === undefined || this.purchaseOrder.orderDate === "")
            this.validationErrors.push("Date is required.");
        if (this.purchaseOrder.purchaseOrderLines === undefined || this.purchaseOrder.purchaseOrderLines.length < 1)
            this.validationErrors.push("Enter at least 1 line item.");
        if (this.purchaseOrder.purchaseOrderLines !== undefined && this.purchaseOrder.purchaseOrderLines.length > 0) {
            for (var i = 0; i < this.purchaseOrder.purchaseOrderLines.length; i++) {
                if (this.purchaseOrder.purchaseOrderLines[i].itemId === undefined
                    || this.purchaseOrder.purchaseOrderLines[i].itemId === "")
                    this.validationErrors.push("Item is required.");
                if (this.purchaseOrder.purchaseOrderLines[i].measurementId === undefined
                    || this.purchaseOrder.purchaseOrderLines[i].measurementId === "")
                    this.validationErrors.push("Uom is required.");
                if (this.purchaseOrder.purchaseOrderLines[i].quantity === undefined
                    || this.purchaseOrder.purchaseOrderLines[i].quantity === ""
                    || this.purchaseOrder.purchaseOrderLines[i].quantity === 0)
                    this.validationErrors.push("Quantity is required.");
                if (this.purchaseOrder.purchaseOrderLines[i].amount === undefined
                    || this.purchaseOrder.purchaseOrderLines[i].amount === ""
                    || this.purchaseOrder.purchaseOrderLines[i].amount === 0)
                    this.validationErrors.push("Amount is required.");
                if (this.lineTotal(i) === undefined
                    || this.lineTotal(i).toString() === "NaN"
                    || this.lineTotal(i) === 0)
                    this.validationErrors.push("Invalid data.");
            }
        }

        if (this.validationErrors.length === 0) {
            axios.post(Config.apiUrl + "api/purchasing/savepurchaseorder", JSON.stringify(this.purchaseOrder),
                {
                    headers: {
                        'Content-type': 'application/json'
                    }
                })
                .then(function (response) {
                    return;
                })
                .catch(function (error) {
                    console.log(error);            
                    this.validationErrors.push("An error occured on posting data. Please check the browser console for more details.");
                }.bind(this))
        }
    }

    changedVendor(vendorId) {
        this.purchaseOrder.vendorId = vendorId;
    }

    changedPaymentTerm(paymentTermId) {
        this.purchaseOrder.paymentTermId = paymentTermId;
    }

    changedOrderDate(date) {
        this.purchaseOrder.orderDate = date;
    }

    addLineItem(itemId, measurementId, quantity, amount, discount) {
        var newLineItem = new PurchaseOrderLine(itemId, measurementId, quantity, amount, discount);
        this.purchaseOrder.purchaseOrderLines.push(extendObservable(newLineItem, newLineItem));        
    }

    removeLineItem(row) {
        this.purchaseOrder.purchaseOrderLines.splice(row, 1);
    }

    updateLineItem(row, targetProperty, value) {
        if (this.purchaseOrder.purchaseOrderLines.length > 0)
            this.purchaseOrder.purchaseOrderLines[row][targetProperty] = value;
    }

    grandTotal() {
        var sum = 0;
        for (var i = 0; i < this.purchaseOrder.purchaseOrderLines.length; i++) {
            var lineSum = this.purchaseOrder.purchaseOrderLines[i].quantity * this.purchaseOrder.purchaseOrderLines[i].amount;
            sum = sum + lineSum;
        }
        return sum;
    }

    lineTotal(row) {
        var lineSum = this.purchaseOrder.purchaseOrderLines[row].quantity * this.purchaseOrder.purchaseOrderLines[row].amount;
        return lineSum;
    }
}