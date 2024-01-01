import {makeObservable, observable, extendObservable, autorun} from 'mobx';
import axios from "axios";
import Config from '../../Config';

import PurchaseInvoice from './PurchaseInvoice';
import PurchaseInvoiceLine from './PurchaseInvoiceLine';

import CommonStore from "../Common/CommonStore";

const baseUrl = location.protocol
    + "//" + location.hostname
    + (location.port && ":" + location.port)
    + "/";

export default class PurchaseOrderStore {
    purchaseInvoice;
    commonStore;
    validationErrors: string[] = [];
    editMode = false;
    purchaseInvoiceStatus: string = "";

    RTotal = 0;
    GTotal = 0;
    TTotal = 0;

    constructor(purchId: number, invoiceId: number) {

        makeObservable(this, {
            validationErrors: observable,
            editMode: observable,
            purchaseInvoiceStatus: observable,
            RTotal: observable,
            GTotal: observable,
            TTotal: observable,
        });

        this.commonStore = new CommonStore();
        this.purchaseInvoice = new PurchaseInvoice();
        extendObservable(this.purchaseInvoice, {
            vendorId: this.purchaseInvoice.vendorId,
            invoiceDate: this.purchaseInvoice.invoiceDate,
            paymentTermId: this.purchaseInvoice.paymentTermId,
            referenceNo: this.purchaseInvoice.referenceNo,
            posted: this.purchaseInvoice.posted,
            readyForPosting: this.purchaseInvoice.readyForPosting,
            purchaseInvoiceLines: []
        });

        autorun(() => this.computeTotals());

        if (purchId !== undefined) {
            axios.get(Config.API_URL + "api/purchasing/purchaseorder?id=" + purchId)
                .then((result) => {
                    for (let i = 0; i < result.data.purchaseOrderLines.length; i++) {
                        if (result.data.purchaseOrderLines[i].remainingQtyToInvoice == 0)
                            continue;
                        this.addLineItem(
                            result.data.purchaseOrderLines[i].id,
                            result.data.purchaseOrderLines[i].itemId,
                            result.data.purchaseOrderLines[i].measurementId,
                            result.data.purchaseOrderLines[i].remainingQtyToInvoice,
                            result.data.purchaseOrderLines[i].amount,
                            result.data.purchaseOrderLines[i].discount,
                            result.data.purchaseOrderLines[i].code
                        );
                    }

                    this.purchaseInvoice.fromPurchaseOrderId = purchId;
                    this.purchaseInvoice.paymentTermId = result.data.paymentTermId;
                    this.purchaseInvoice.referenceNo = result.data.referenceNo;
                    this.changedVendor(result.data.vendorId);
                    this.changedInvoiceDate(result.data.orderDate);
                    this.computeTotals();
                    this.changedEditMode(true);
                })
                .catch(() => {});
        }
        else if (invoiceId !== undefined) {
            axios.get(Config.API_URL + "purchasing/purchaseinvoice?id=" + invoiceId)
                .then((result) => {
                    for (let i = 0; i < result.data.purchaseInvoiceLines.length; i++) {
                        this.addLineItem(
                            result.data.purchaseInvoiceLines[i].id,
                            result.data.purchaseInvoiceLines[i].itemId,
                            result.data.purchaseInvoiceLines[i].measurementId,
                            result.data.purchaseInvoiceLines[i].quantity,
                            result.data.purchaseInvoiceLines[i].amount,
                            result.data.purchaseInvoiceLines[i].discount,
                            result.data.purchaseInvoiceLines[i].code
                        );
                        const itemCode = this.changeItemCode(result.data.purchaseInvoiceLines[i].itemId) || ''; // Provide a default value if the item code is undefined
                        this.updateLineItem(i, 'code', itemCode);
                    }

                    this.purchaseInvoice.id = result.data.id;
                    this.purchaseInvoice.paymentTermId = result.data.paymentTermId;
                    this.purchaseInvoice.referenceNo = result.data.referenceNo;
                    this.changedVendor(result.data.vendorId);
                    this.changedInvoiceDate(result.data.invoiceDate);
                    this.purchaseInvoice.posted = result.data.posted;
                    this.purchaseInvoice.readyForPosting = result.data.readyForPosting;
                    this.getPurchaseInvoiceStatus(result.data.statusId);

                    this.computeTotals();

                    const nodes = document.getElementById("divPurchaseInvoiceForm")!.getElementsByTagName('*');
                    for (let i = 0; i < nodes.length; i++) {
                        nodes[i].className += " disabledControl";
                    }
                })
                .catch(() => {});
        }
        else {
            this.changedEditMode(true);
        }
    }

    computeTotals() {
        let rtotal = 0;
        let ttotal = 0;

        for (let i = 0; i < this.purchaseInvoice.purchaseInvoiceLines.length; i++) {
            const lineItem = this.purchaseInvoice.purchaseInvoiceLines[i];
            rtotal = rtotal + this.getLineTotal(i);
            axios.get(Config.API_URL + "tax/gettax?itemId=" + lineItem.itemId + "&partyId=" + this.purchaseInvoice.vendorId + "&type=2")
                .then((result) => {
                    if (result.data.length > 0) {
                        ttotal = ttotal + this.commonStore.getPurhcaseLineTaxAmount(lineItem.quantity, lineItem.amount, lineItem.discount, result.data);
                    }
                    this.TTotal = ttotal;
                    this.GTotal = rtotal + ttotal;
                });
            this.RTotal = rtotal;
        }
    }

    savePurchaseInvoice() {
        if (this.validation() && this.validationErrors.length === 0) {
            axios.post(Config.API_URL + "purchasing/savepurchaseinvoice", JSON.stringify(this.purchaseInvoice),
                {
                    headers: {
                        'Content-type': 'application/json'
                    }
                })
                .then(() => {
                    window.location.href = baseUrl + 'purchasing/purchaseinvoices';
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

    postInvoice() {
        if (this.validation() && this.validationErrors.length === 0) {
            axios.post(Config.API_URL + "purchasing/postpurchaseinvoice", JSON.stringify(this.purchaseInvoice),
                {
                    headers: {
                        'Content-type': 'application/json'
                    }
                })
                .then(() => {
                    window.location.href = baseUrl + 'purchasing/purchaseinvoices';
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
        if (this.purchaseInvoice.vendorId === undefined)
            this.validationErrors.push("Vendor is required.");
        if (this.purchaseInvoice.paymentTermId === undefined)
            this.validationErrors.push("Payment term is required.");
        if (this.purchaseInvoice.invoiceDate === undefined)
            this.validationErrors.push("Date is required.");
        if (this.purchaseInvoice.purchaseInvoiceLines === undefined || this.purchaseInvoice.purchaseInvoiceLines.length < 1)
            this.validationErrors.push("Enter at least 1 line item.");
        if (this.purchaseInvoice.purchaseInvoiceLines !== undefined && this.purchaseInvoice.purchaseInvoiceLines.length > 0) {
            for (let i = 0; i < this.purchaseInvoice.purchaseInvoiceLines.length; i++) {
                if (this.purchaseInvoice.purchaseInvoiceLines[i].itemId === undefined
                    || Number(this.purchaseInvoice.purchaseInvoiceLines[i].itemId) === 0)
                    this.validationErrors.push("Item is required.");
                if (this.purchaseInvoice.purchaseInvoiceLines[i].measurementId === undefined
                    || Number(this.purchaseInvoice.purchaseInvoiceLines[i].measurementId) === 0)
                    this.validationErrors.push("Uom is required.");
                if (this.purchaseInvoice.purchaseInvoiceLines[i].quantity === undefined
                    || this.purchaseInvoice.purchaseInvoiceLines[i].quantity.toString() === ""
                    || parseInt(this.purchaseInvoice.purchaseInvoiceLines[i].quantity.toString()) === 0)
                    this.validationErrors.push("Quantity is required.");
                if (this.purchaseInvoice.purchaseInvoiceLines[i].amount === undefined
                    || this.purchaseInvoice.purchaseInvoiceLines[i].amount.toString() === ""
                    || parseInt(this.purchaseInvoice.purchaseInvoiceLines[i].amount.toString()) === 0)
                    this.validationErrors.push("Amount is required.");
                if (this.getLineTotal(i) === undefined
                    || this.getLineTotal(i).toString() === "NaN"
                    || parseInt(this.getLineTotal(i).toString()) === 0)
                    this.validationErrors.push("Invalid data.");
            }
        }

        return this.validationErrors.length === 0;
    }

    validationLine() {
        this.validationErrors = [];
        if (this.purchaseInvoice.purchaseInvoiceLines !== undefined && this.purchaseInvoice.purchaseInvoiceLines.length > 0) {
            for (let i = 0; i < this.purchaseInvoice.purchaseInvoiceLines.length; i++) {
                if (this.purchaseInvoice.purchaseInvoiceLines[i].itemId === undefined)
                    this.validationErrors.push("Item is required.");
                if (this.purchaseInvoice.purchaseInvoiceLines[i].measurementId === undefined)
                    this.validationErrors.push("Uom is required.");
                if (this.purchaseInvoice.purchaseInvoiceLines[i].quantity === undefined)
                    this.validationErrors.push("Quantity is required.");
                if (this.purchaseInvoice.purchaseInvoiceLines[i].amount === undefined)
                    this.validationErrors.push("Amount is required.");
                if (this.getLineTotal(i) === undefined
                    || this.getLineTotal(i).toString() === "NaN")
                    this.validationErrors.push("Invalid data.");
            }
        }
        else {
            const itemId: number = Number((document.getElementById("optNewItemId") as HTMLInputElement).value);
            const measurementId: number = Number((document.getElementById("optNewMeasurementId") as HTMLInputElement).value);
            const quantity: number = Number((document.getElementById("txtNewQuantity") as HTMLInputElement).value);
            const amount: number = Number((document.getElementById("txtNewAmount") as HTMLInputElement).value);

            if (itemId == 0 || itemId === undefined)
                this.validationErrors.push("Item is required.");
            if (measurementId == 0 || measurementId === undefined)
                this.validationErrors.push("Uom is required.");
            if (quantity == 0 || quantity === undefined)
                this.validationErrors.push("Quantity is required.");
            if (amount == 0 || amount === undefined)
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
        this.purchaseInvoice.referenceNo = refNo;
    }

    changedVendor(vendorId: number) {
        this.purchaseInvoice.vendorId = vendorId;
    }

    changedPaymentTerm(paymentTermId: number) {
        this.purchaseInvoice.paymentTermId = paymentTermId;
    }

    changedInvoiceDate(date: Date) {
        this.purchaseInvoice.invoiceDate = date;
    }

    addLineItem(id: number, itemId: number, measurementId: number, 
        quantity: number, amount: number, discount: number, code: string) {
        const newLineItem = new PurchaseInvoiceLine(id, itemId, measurementId, quantity, amount, discount, code);
        this.purchaseInvoice.purchaseInvoiceLines.push(extendObservable(newLineItem, newLineItem));        
    }

    removeLineItem(row: number) {
        this.purchaseInvoice.purchaseInvoiceLines.splice(row, 1);
    }

    updateLineItem(row: number, targetProperty: keyof PurchaseInvoiceLine, value: string | number) {
        if (this.purchaseInvoice.purchaseInvoiceLines.length > 0)
            (this.purchaseInvoice.purchaseInvoiceLines[row] as Record<keyof PurchaseInvoiceLine, string | number>)[targetProperty] = value;

        this.computeTotals();
    }

    getLineTotal(row: number) {
        let lineSum = 0;
        const lineItem = this.purchaseInvoice.purchaseInvoiceLines[row];
        lineSum = (lineItem.quantity * lineItem.amount) - lineItem.discount;
        return lineSum;
    }

    changedEditMode(editMode: boolean) {
        this.editMode = editMode;
    }

    getPurchaseInvoiceStatus(statusId: number) {
        let status = "";
        if (statusId === 0)
            status = "Draft";
        else if (statusId === 1)
            status = "Open";
        else if (statusId === 2)
            status = "Paid";
        this.purchaseInvoiceStatus = status;
    }

    changeItemCode(itemId: number) {

        for (let x = 0; x < this.commonStore.items.length; x++) {
            const lineItem = this.commonStore.items[x] as PurchaseInvoiceLine
            if (lineItem.id === itemId) {
                return lineItem.code;
            }
        }
    }
}