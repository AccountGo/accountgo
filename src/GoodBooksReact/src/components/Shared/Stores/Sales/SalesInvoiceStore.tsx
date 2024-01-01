import {observable, extendObservable, autorun, makeObservable} from 'mobx';
import axios from "axios";
import Config from '../../Config';

import SalesInvoice from './SalesInvoice';
import SalesInvoiceLine from './SalesInvoiceLine';
import CommonStore from "../Common/CommonStore";

const baseUrl = location.protocol
    + "//" + location.hostname
    + (location.port && ":" + location.port)
    + "/";

export default class SalesStore {
    salesInvoice;
    commonStore;
    validationErrors: string[] = [];
    editMode = false;
    
    RTotal = 0;
    GTotal = 0;
    TTotal = 0;

    constructor(orderId: number, invoiceId: number) {
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

        makeObservable(this, {
            validationErrors: observable,
            editMode: observable,
            RTotal: observable,
            GTotal: observable,
            TTotal: observable,
        });

        autorun(() => this.computeTotals());

        if (orderId !== undefined) {
            const result = axios.get(Config.API_URL + "sales/salesorder?id=" + orderId);
            result.then((result) => {

                for (let i = 0; i < result.data.salesOrderLines.length; i++) {
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
            const result = axios.get(Config.API_URL + "sales/salesinvoice?id=" + invoiceId);
            result.then((result) => {
                for (let i = 0; i < result.data.salesInvoiceLines.length; i++) {
                    this.addLineItem(
                        result.data.salesInvoiceLines[i].id,
                        result.data.salesInvoiceLines[i].itemId,
                        result.data.salesInvoiceLines[i].measurementId,
                        result.data.salesInvoiceLines[i].quantity,
                        result.data.salesInvoiceLines[i].amount,
                        result.data.salesInvoiceLines[i].discount,
                        result.data.salesInvoiceLines[i].code
                    );
                    this.updateLineItem(i, 'code', this.changeItemCode(result.data.salesInvoiceLines[i].itemId)!);
                }

                this.salesInvoice.id = result.data.id;
                this.changedCustomer(result.data.customerId);
                this.salesInvoice.paymentTermId = result.data.paymentTermId;
                this.salesInvoice.referenceNo = result.data.referenceNo;
                this.salesInvoice.invoiceDate = result.data.invoiceDate;
                this.salesInvoice.posted = result.data.posted;
                this.salesInvoice.readyForPosting = result.data.readyForPosting;
                this.computeTotals();

                const nodes = document.getElementById("divSalesInvoiceForm")!.getElementsByTagName('*');
                for (let i = 0; i < nodes.length; i++) {
                    nodes[i].className += " disabledControl";
                }
            });
        }
        else
            this.changedEditMode(true);           
    }

    computeTotals() {
        let rtotal = 0;
        let ttotal = 0;

        for (let i = 0; i < this.salesInvoice.salesInvoiceLines.length; i++) {
            const lineItem = this.salesInvoice.salesInvoiceLines[i];
            rtotal = rtotal + this.getLineTotal(i);
            axios.get(Config.API_URL + "tax/gettax?itemId=" + lineItem.itemId + "&partyId=" + this.salesInvoice.customerId + "&type=1")
                .then((result) => {
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
                .catch((error) => {
                    if (axios.isAxiosError(error)) {
                        this.validationErrors.push(`Status: ${error.status} - Message: ${error.response?.data}`);
                      } else {
                        console.error(error);
                        this.validationErrors.push(`Error: ${error}`);
                      }
                })
        }
    }

    printInvoice() {
        const w = 800;
        const h = 600;
        const wLeft = window.screenLeft ? window.screenLeft : window.screenX;
        const wTop = window.screenTop ? window.screenTop : window.screenY;

        const left = wLeft + (window.innerWidth / 2) - (w / 2);
        const top = wTop + (window.innerHeight / 2) - (h / 2);
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
                .catch((error) => {
                    if (axios.isAxiosError(error)) {
                        this.validationErrors.push(`Status: ${error.status} - Message: ${error.response?.data}`);
                      } else {
                        console.error(error);
                        this.validationErrors.push(`Error: ${error}`);
                      }
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
            for (let i = 0; i < this.salesInvoice.salesInvoiceLines.length; i++) {
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
            for (let i = 0; i < this.salesInvoice.salesInvoiceLines.length; i++) {
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
            const itemId: string = (document.getElementById("optNewItemId") as HTMLInputElement).value;
            const measurementId: string = (document.getElementById("optNewMeasurementId") as HTMLInputElement).value;
            const quantity: string = (document.getElementById("txtNewQuantity") as HTMLInputElement).value;
            const amount: string = (document.getElementById("txtNewAmount") as HTMLInputElement).value;

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
            const itemId: string = (document.getElementById("optNewItemId") as HTMLInputElement).value;
            const measurementId: string = (document.getElementById("optNewMeasurementId") as HTMLInputElement).value;
            const quantity: string = (document.getElementById("txtNewQuantity") as HTMLInputElement).value;
            const amount: string = (document.getElementById("txtNewAmount") as HTMLInputElement).value;

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


    changedReferenceNo(refNo: string) {
        this.salesInvoice.referenceNo = refNo;
    }

    changedCustomer(custId: number) {
        this.salesInvoice.customerId = custId;
    }

    changedInvoiceDate(date: Date) {
        this.salesInvoice.invoiceDate = date;
    }

    changedPaymentTerm(termId: number) {
        this.salesInvoice.paymentTermId = termId;
    }

    addLineItem(id: number, itemId: number, measurementId: number, quantity: number, amount: number, discount: number, code: string) {
        const newLineItem = new SalesInvoiceLine(id, itemId, measurementId, quantity, amount, discount, code);
        this.salesInvoice.salesInvoiceLines.push(extendObservable(newLineItem, newLineItem));        
    }

    removeLineItem(row: number) {
        this.salesInvoice.salesInvoiceLines.splice(row, 1);
    }

    updateLineItem(row: number, targetProperty: keyof SalesInvoiceLine, value: string | number) {
        if (this.salesInvoice.salesInvoiceLines.length > 0)
            (this.salesInvoice.salesInvoiceLines[row] as Record<keyof SalesInvoiceLine, string | number>)[targetProperty] = value;

        this.computeTotals();
    }

    getLineTotal(row: number) {
        let lineSum = 0;
        const lineItem = this.salesInvoice.salesInvoiceLines[row];
        lineSum = (lineItem.quantity * lineItem.amount) - lineItem.discount;
        return lineSum;
    }

    changedEditMode(editMode: any) {
        this.editMode = editMode;
    }

    changeItemCode(itemId: number) {

        for (let x = 0; x < this.commonStore.items.length; x++) {
            const lineItem = this.commonStore.items[x] as SalesInvoiceLine;
            if (lineItem.id === itemId) {
                return lineItem.code;
            }
        }
    }
}