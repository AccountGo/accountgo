import {observable, extendObservable, autorun} from 'mobx';
import axios from "axios";
import Config from '../../Config';

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
    @observable validationErrors: any[] = [];
    @observable editMode = false;


    constructor(orderId: any, invoiceId: any) {
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
            var result = axios.get(Config.API_URL + "sales/salesorder?id=" + orderId);
            result.then((result: any) => {

                for (var i = 0; i < result.data.salesOrderLines.length; i++) {
                    if (result.data.salesOrderLines[i].remainingQtyToInvoice == 0)
                        continue;
                    this.addLineItem(
                        result.data.salesOrderLines[i].id,
                        result.data.salesOrderLines[i].itemId,
                        result.data.salesOrderLines[i].measurementId,
                        result.data.salesOrderLines[i].remainingQtyToInvoice,
                        result.data.salesOrderLines[i].amount,
                        result.data.salesOrderLines[i].discount,
                        result.data.salesOrderLines[i].code
                    );
                }
                this.salesInvoice.fromSalesOrderId = orderId;
                this.changedCustomer(result.data.customerId);
                this.salesInvoice.paymentTermId = result.data.paymentTermId;
                this.salesInvoice.referenceNo = result.data.referenceNo;
                this.salesInvoice.invoiceDate = result.data.orderDate;
                this.computeTotals();
                this.changedEditMode(true);

            });
        } else if (invoiceId !== undefined) {
            var result = axios.get(Config.API_URL + "sales/salesinvoice?id=" + invoiceId);
            result.then((result: any) => {
                for (var i = 0; i < result.data.salesInvoiceLines.length; i++) {
                    this.addLineItem(
                        result.data.salesInvoiceLines[i].id,
                        result.data.salesInvoiceLines[i].itemId,
                        result.data.salesInvoiceLines[i].measurementId,
                        result.data.salesInvoiceLines[i].quantity,
                        result.data.salesInvoiceLines[i].amount,
                        result.data.salesInvoiceLines[i].discount,
                        result.data.salesInvoiceLines[i].code
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

                var nodes = document.getElementById("divSalesInvoiceForm")!.getElementsByTagName('*');
                for (var i = 0; i < nodes.length; i++) {
                    nodes[i].className += " disabledControl";
                }
            });
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

        for (var i = 0; i < this.salesInvoice.salesInvoiceLines.length; i++) {
            var lineItem = this.salesInvoice.salesInvoiceLines[i];
            rtotal = rtotal + this.getLineTotal(i);
            axios.get(Config.API_URL + "tax/gettax?itemId=" + lineItem.itemId + "&partyId=" + this.salesInvoice.customerId + "&type=1")
                .then((result: any) => {
                    if (result.data.length > 0) {
                        ttotal = ttotal + this.commonStore.getSalesLineTaxAmount(lineItem.quantity, lineItem.amount, lineItem.discount, result.data);
                    }
                    this.TTotal = ttotal;
                    this.GTotal = rtotal + ttotal;
                });
            this.RTotal = rtotal;
        }
    }

    saveNewSalesInvoice() {

        if (this.salesInvoice.invoiceDate === undefined)
            this.salesInvoice.invoiceDate = new Date(new Date(Date.now()).toISOString().substring(0, 10));

        if (this.validation() && this.validationErrors.length === 0) {
            axios.post(Config.API_URL + "sales/savesalesinvoice", JSON.stringify(this.salesInvoice),
                {
                    headers: {
                        'Content-type': 'application/json'
                    }
                })
                .then(() => {
                    window.location.href = baseUrl + 'sales/salesinvoices';
                })
                .catch((error: any) => {
                    error.data.map((err: any) => {
                        this.validationErrors.push(err);
                    });
                });
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
            axios.post(Config.API_URL + "sales/postsalesinvoice", JSON.stringify(this.salesInvoice),
                {
                    headers: {
                        'Content-type': 'application/json'
                    }
                })
                .then(() => {
                    window.location.href = baseUrl + 'sales/salesinvoices';
                })
                .catch((error: any) => {
                    error.data.map((err: any) => {
                        this.validationErrors.push(err);
                    });
                })
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
            var itemId: any = (document.getElementById("optNewItemId") as HTMLInputElement).value;
            var measurementId: any = (document.getElementById("optNewMeasurementId") as HTMLInputElement).value;
            var quantity: any = (document.getElementById("txtNewQuantity") as HTMLInputElement).value;
            var amount: any = (document.getElementById("txtNewAmount") as HTMLInputElement).value;

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
            var itemId: any = (document.getElementById("optNewItemId") as HTMLInputElement).value;
            var measurementId: any = (document.getElementById("optNewMeasurementId") as HTMLInputElement).value;
            var quantity: any = (document.getElementById("txtNewQuantity") as HTMLInputElement).value;
            var amount: any = (document.getElementById("txtNewAmount") as HTMLInputElement).value;

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


    changedReferenceNo(refNo: any) {
        this.salesInvoice.referenceNo = refNo;
    }
    changedCustomer(custId: any) {
        this.salesInvoice.customerId = custId;
    }

    changedInvoiceDate(date: any) {
        this.salesInvoice.invoiceDate = date;
    }

    changedPaymentTerm(termId: any) {
        this.salesInvoice.paymentTermId = termId;
    }

    addLineItem(id: any, itemId: any, measurementId: any, quantity: number, amount: number, discount: number, code: any) {
        var newLineItem = new SalesInvoiceLine(id, itemId, measurementId, quantity, amount, discount, code);
        this.salesInvoice.salesInvoiceLines.push(extendObservable(newLineItem, newLineItem));        
    }

    removeLineItem(row: any) {
        this.salesInvoice.salesInvoiceLines.splice(row, 1);
    }

    updateLineItem(row: number, targetProperty: keyof SalesInvoiceLine, value: any) {
        if (this.salesInvoice.salesInvoiceLines.length > 0)
            (this.salesInvoice.salesInvoiceLines[row] as any)[targetProperty] = value;

        this.computeTotals();
    }

    getLineTotal(row: any) {
        let lineSum = 0;
        let lineItem = this.salesInvoice.salesInvoiceLines[row];
        lineSum = (lineItem.quantity * lineItem.amount) - lineItem.discount;
        return lineSum;
    }

    changedEditMode(editMode: any) {
        this.editMode = editMode;
    }

    changeItemCode(itemId: any) {

        for (var x = 0; x < this.commonStore.items.length; x++) {
            var lineItem = this.commonStore.items[x] as SalesInvoiceLine;
            if (lineItem.id === parseInt(itemId)) {
                return lineItem.code;
            }
        }
    }
}