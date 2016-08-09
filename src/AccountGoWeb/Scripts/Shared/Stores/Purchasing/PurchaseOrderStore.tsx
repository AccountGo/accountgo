import {observable, extendObservable, action, autorun, computed} from 'mobx';
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
                    this.purchaseOrder.id = result.data.id;
                    this.purchaseOrder.paymentTermId = result.data.paymentTermId;
                    this.purchaseOrder.referenceNo = result.data.referenceNo;
                    this.changedVendor(result.data.vendorId);
                    this.purchaseOrder.orderDate = result.data.orderDate;
                    for (var i = 0; i < result.data.purchaseOrderLines.length; i++) {
                        this.addLineItem(
                            result.data.purchaseOrderLines[i].id,
                            result.data.purchaseOrderLines[i].itemId,
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

        autorun(() => this.computeTotals());
    }

    @observable RTotal = 0;
    @observable GTotal = 0;
    @observable TTotal = 0;

    async computeTotals() {
        var rtotal = 0;
        var ttotal = 0;
        var gtotal = 0;

        for (var i = 0; i < this.purchaseOrder.purchaseOrderLines.length; i++) {
            var lineItem = this.purchaseOrder.purchaseOrderLines[i];
            var lineSum = lineItem.quantity * lineItem.amount;
            rtotal = rtotal + lineSum;
            await axios.get(Config.apiUrl + "api/tax/gettax?itemId=" + lineItem.itemId + "&partyId=" + this.purchaseOrder.vendorId + "&type=2")
                .then(function (result) {
                    if (result.data.length > 0) {
                        ttotal = ttotal + this.commonStore.getPurhcaseLineTaxAmount(lineItem.quantity, lineItem.amount, lineItem.discount, result.data);
                    }
                }.bind(this));
        }

        this.RTotal = rtotal;
        this.TTotal = ttotal;
        this.GTotal = rtotal - ttotal;
    }

    async savePurchaseOrder() {
        if (this.validation() && this.validationErrors.length === 0) {
            await axios.post(Config.apiUrl + "api/purchasing/savepurchaseorder", JSON.stringify(this.purchaseOrder),
                {
                    headers: {
                        'Content-type': 'application/json'
                    }
                })
                .then(function (response) {
                    window.location.href = baseUrl + 'purchasing/purchaseorders';
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
                if (this.getLineTotal(i) === undefined
                    || this.getLineTotal(i).toString() === "NaN"
                    || this.getLineTotal(i) === 0)
                    this.validationErrors.push("Invalid data.");
            }
        }

        return this.validationErrors.length === 0;
    }
    changedReferenceNo(refNo) {
        this.purchaseOrder.referenceNo = refNo;
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

    addLineItem(id = 0, itemId, measurementId, quantity, amount, discount) {
        var newLineItem = new PurchaseOrderLine(id, itemId, measurementId, quantity, amount, discount);
        this.purchaseOrder.purchaseOrderLines.push(extendObservable(newLineItem, newLineItem));        
    }

    removeLineItem(row) {
        this.purchaseOrder.purchaseOrderLines.splice(row, 1);
    }

    updateLineItem(row, targetProperty, value) {
        if (this.purchaseOrder.purchaseOrderLines.length > 0)
            this.purchaseOrder.purchaseOrderLines[row][targetProperty] = value;

        this.computeTotals();
    }

    getLineTotal(row) {
        let lineSum = 0;
        let lineItem = this.purchaseOrder.purchaseOrderLines[row];
        lineSum = (lineItem.quantity * lineItem.amount) - lineItem.discount;
        return lineSum;
    }
}