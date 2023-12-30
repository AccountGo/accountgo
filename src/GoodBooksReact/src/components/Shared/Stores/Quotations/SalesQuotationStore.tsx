import {observable, extendObservable, autorun, makeObservable} from 'mobx';
import axios from "axios";
import Config from '../../Config';

import SalesQuotation from './SalesQuotation';
import SalesQuotationLine from './SalesQuotationLine';

import CommonStore from "../Common/CommonStore";

let baseUrl = location.protocol
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

    constructor(quotationId: any) {
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
            var result = axios.get(Config.API_URL + "sales/quotation?id=" + quotationId);
            result.then((result: any) => {
                this.salesQuotation.id = result.data.id;
                this.salesQuotation.paymentTermId = result.data.paymentTermId;
                this.salesQuotation.referenceNo = result.data.referenceNo;
                this.salesQuotation.statusId = result.data.statusId;
                this.changedCustomer(result.data.customerId);
                this.getQuotationStatus(result.data.statusId);
                this.changedQuotationDate(result.data.quotationDate);
                for (var i = 0; i < result.data.salesQuotationLines.length; i++) {
                    this.addLineItem(
                        result.data.salesQuotationLines[i].id,
                        result.data.salesQuotationLines[i].itemId,
                        result.data.salesQuotationLines[i].measurementId,
                        result.data.salesQuotationLines[i].quantity,
                        result.data.salesQuotationLines[i].amount,
                        result.data.salesQuotationLines[i].discount,
                        result.data.salesQuotationLines[i].code
                    );
                    this.updateLineItem(i, 'code', this.changeItemCode(result.data.salesQuotationLines[i].itemId));
                }
                this.computeTotals();
                var nodes = document.getElementById("divSalesQuotationForm")?.getElementsByTagName('*');
                for (var i = 0; nodes && i < nodes.length; i++) {
                    nodes[i].className += " disabledControl";
                }
            });
        }
        else {
            this.changedEditMode(true);
        }
    }

    computeTotals() {
        var rtotal = 0;
        var ttotal = 0;

        for (var i = 0; i < this.salesQuotation.salesQuotationLines.length; i++) {
            var lineItem = this.salesQuotation.salesQuotationLines[i];
            rtotal = rtotal + this.getLineTotal(i);
            axios.get(Config.API_URL + "tax/gettax?itemId=" + lineItem.itemId + "&partyId=" + this.salesQuotation.customerId + "&type=1")
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
                    .catch((error: any) => {
                        error.data.map((err: any) => {
                            this.validationErrors.push(err);
                        });
                    });
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
                    .catch((error: any) => {
                        error.data.map((err: any) => {
                            this.validationErrors.push(err);
                        });
                    });
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
            for (var i = 0; i < this.salesQuotation.salesQuotationLines.length; i++) {
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

    //initializedQuotationDate()
    //{
    //    if (this.salesQuotation.quotationDate === undefined || this.salesQuotation.quotationDate === "")
    //        this.salesQuotation.quotationDate = new Date(Date.now()).toISOString().substring(0, 10);
    //}

    validationLine() {
        this.validationErrors = [];
        if (this.salesQuotation.salesQuotationLines !== undefined && this.salesQuotation.salesQuotationLines.length > 0) {
            for (var i = 0; i < this.salesQuotation.salesQuotationLines.length; i++) {
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

    changeItemCode(itemId: any) {

        for (var x = 0; x < this.commonStore.items.length; x++) {
            var lineItewm = this.commonStore.items[x] as SalesQuotationLine;
            if (lineItewm.id === parseInt(itemId)) {
                return lineItewm.code;
            }
        }
    }

    addLineItem(id: number, itemId: number, measurementId: number, 
        quantity: number, amount: number, discount: number, code: number) {

        var newLineItem = new SalesQuotationLine(id, itemId, measurementId, quantity, amount, discount, code);
        this.salesQuotation.salesQuotationLines.push(extendObservable(newLineItem, newLineItem));
    }

    removeLineItem(row: number) {
        this.salesQuotation.salesQuotationLines.splice(row, 1);
    }

    updateLineItem(row: any, targetProperty: keyof  SalesQuotationLine, value: any) {

        if (this.salesQuotation.salesQuotationLines.length > 0)
            this.salesQuotation.salesQuotationLines[row][targetProperty] = value;

        this.computeTotals();
    }

    getLineTotal(row: any) {
        let lineSum = 0;
        let lineItem = this.salesQuotation.salesQuotationLines[row];
        lineSum = (lineItem.quantity * lineItem.amount) - lineItem.discount;
        return lineSum;
    }

    changedEditMode(editMode: any) {
        this.editMode = editMode;
    }


}