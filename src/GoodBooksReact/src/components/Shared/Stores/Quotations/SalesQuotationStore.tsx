import {observable, extendObservable, autorun, makeObservable} from 'mobx';
import axios from "axios";
import Config from '../../Config';

import SalesQuotation from './SalesQuotation';
import SalesQuotationLine from './SalesQuotationLine';

import CommonStore from "../Common/CommonStore";

const baseUrl = location.protocol
    + "//" + location.hostname
    + (location.port && ":" + location.port)
    + "/";

export default class SalesQuotationStore {
    salesQuotation: SalesQuotation;
    commonStore: CommonStore;
    salesQuotationStatus: string = "";
    validationErrors: string[] = [];
    editMode = false;
    
    RTotal = 0;
    GTotal = 0;
    TTotal = 0;

    constructor(quotationId: number) {
        this.commonStore = new CommonStore();
        this.salesQuotation = new SalesQuotation();
        extendObservable(this.salesQuotation, {
            customerId: this.salesQuotation.customerId,
            quotationDate: this.salesQuotation.quotationDate,
            paymentTermId: this.salesQuotation.paymentTermId,
            referenceNo: this.salesQuotation.referenceNo,
            statusId: this.salesQuotation.statusId,
            salesQuotationLines: []
        });

        makeObservable(this, {
            validationErrors: observable,
            salesQuotationStatus: observable,
            editMode: observable,
            RTotal: observable,
            GTotal: observable,
            TTotal: observable,
        });

        autorun(() => this.computeTotals());

        if (quotationId !== undefined) {
            const result = axios.get(Config.API_URL + "sales/quotation?id=" + quotationId);
            result.then((result) => {
                this.salesQuotation.id = result.data.id;
                this.salesQuotation.paymentTermId = result.data.paymentTermId;
                this.salesQuotation.referenceNo = result.data.referenceNo;
                this.salesQuotation.statusId = result.data.statusId;
                this.changedCustomer(result.data.customerId);
                this.getQuotationStatus(result.data.statusId);
                this.changedQuotationDate(result.data.quotationDate);
                for (let i = 0; i < result.data.salesQuotationLines.length; i++) {
                    this.addLineItem(
                        result.data.salesQuotationLines[i].id,
                        result.data.salesQuotationLines[i].itemId,
                        result.data.salesQuotationLines[i].measurementId,
                        result.data.salesQuotationLines[i].quantity,
                        result.data.salesQuotationLines[i].amount,
                        result.data.salesQuotationLines[i].discount,
                        result.data.salesQuotationLines[i].code
                    );
                    this.updateLineItem(i, 'code', Number(this.changeItemCode(result.data.salesQuotationLines[i].itemId)));
                }
                this.computeTotals();
                const nodes = document.getElementById("divSalesQuotationForm")?.getElementsByTagName('*');
                for (let i = 0; nodes && i < nodes.length; i++) {
                    nodes[i].className += " disabledControl";
                }
            });
        }
        else {
            this.changedEditMode(true);
        }
    }

    computeTotals() {
        let rtotal = 0;
        let ttotal = 0;

        for (let i = 0; i < this.salesQuotation.salesQuotationLines.length; i++) {
            const lineItem = this.salesQuotation.salesQuotationLines[i];
            rtotal = rtotal + this.getLineTotal(i);
            axios.get(Config.API_URL + "tax/gettax?itemId=" + lineItem.itemId + "&partyId=" + this.salesQuotation.customerId + "&type=1")
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

    saveNewQuotation() {
        if (this.salesQuotation.quotationDate === undefined)
            this.salesQuotation.quotationDate = new Date(new Date(Date.now()).toISOString().substring(0, 10));

        if (this.validation()) {
            if (this.validationErrors.length === 0) {
                axios.post(Config.API_URL + "sales/savequotation", JSON.stringify(this.salesQuotation),
                    {
                        headers:
                        {
                            'Content-type': 'application/json'
                        }
                    }
                )
                    .then(() => {
                        window.location.href = baseUrl + 'quotations';
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
    }

    bookQuotation() {
        if (this.validation()) {
            if (this.validationErrors.length === 0) {
                axios.post(Config.API_URL + "sales/bookquotation?id=" + String(this.salesQuotation.id),
                    {
                        headers:
                        {
                            'Content-type': 'application/json'
                        }
                    }
                )
                    .then(() => {
                        window.location.href = baseUrl + 'quotations';
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
            for (let i = 0; i < this.salesQuotation.salesQuotationLines.length; i++) {
                if (typeof this.salesQuotation.salesQuotationLines[i].itemId !== 'number' || isNaN(this.salesQuotation.salesQuotationLines[i].itemId))
                    this.validationErrors.push("Item is required.");
                if (typeof this.salesQuotation.salesQuotationLines[i].measurementId !== 'number' || isNaN(this.salesQuotation.salesQuotationLines[i].measurementId))
                    this.validationErrors.push("Uom is required.");
                if (typeof this.salesQuotation.salesQuotationLines[i].quantity !== 'number' || isNaN(this.salesQuotation.salesQuotationLines[i].quantity))
                    this.validationErrors.push("Quantity is required.");
                if (typeof this.salesQuotation.salesQuotationLines[i].amount !== 'number' || isNaN(this.salesQuotation.salesQuotationLines[i].amount))
                    this.validationErrors.push("Amount is required.");
                if (this.getLineTotal(i) === undefined || isNaN(this.getLineTotal(i)))
                    this.validationErrors.push("Invalid data.");
            }
        }

        return this.validationErrors.length === 0;
    }


    getQuotationStatus(statusId: number) {
        let status = "";
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

    //initializedQuotationDate()
    //{
    //    if (this.salesQuotation.quotationDate === undefined || this.salesQuotation.quotationDate === "")
    //        this.salesQuotation.quotationDate = new Date(Date.now()).toISOString().substring(0, 10);
    //}

    validationLine() {
        this.validationErrors = [];
        if (this.salesQuotation.salesQuotationLines !== undefined && this.salesQuotation.salesQuotationLines.length > 0) {
            for (let i = 0; i < this.salesQuotation.salesQuotationLines.length; i++) {
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

    changedCustomer(custId: number) {
        this.salesQuotation.customerId = custId;
    }

    changedPaymentTerm(termId: number) {
        this.salesQuotation.paymentTermId = termId;
    }

    changedQuotationDate(date: Date) {
        this.salesQuotation.quotationDate = date;
    }
    changedReferenceNo(refNo: string) {
        this.salesQuotation.referenceNo = refNo;
    }

    changedQuoteStatus(statusId: number) {
        this.salesQuotation.statusId = statusId;
    }

    changeItemCode(itemId: number) {

        for (let x = 0; x < this.commonStore.items.length; x++) {
            const lineItewm = this.commonStore.items[x] as SalesQuotationLine;
            if (lineItewm.id === itemId) {
                return lineItewm.code;
            }
        }
    }

    addLineItem(id: number, itemId: number, measurementId: number, 
        quantity: number, amount: number, discount: number, code: number) {

        const newLineItem = new SalesQuotationLine(id, itemId, measurementId, quantity, amount, discount, code);
        this.salesQuotation.salesQuotationLines.push(extendObservable(newLineItem, newLineItem));
    }

    removeLineItem(row: number) {
        this.salesQuotation.salesQuotationLines.splice(row, 1);
    }

    updateLineItem(row: number, targetProperty: keyof  SalesQuotationLine, value: string | number) {

        if (this.salesQuotation.salesQuotationLines.length > 0)
            (this.salesQuotation.salesQuotationLines[row] as Record<keyof SalesQuotationLine, string | number>)[targetProperty] = value;

        this.computeTotals();
    }

    getLineTotal(row: number) {
        let lineSum = 0;
        const lineItem = this.salesQuotation.salesQuotationLines[row];
        lineSum = (lineItem.quantity * lineItem.amount) - lineItem.discount;
        return lineSum;
    }

    changedEditMode(editMode: boolean) {
        this.editMode = editMode;
    }


}