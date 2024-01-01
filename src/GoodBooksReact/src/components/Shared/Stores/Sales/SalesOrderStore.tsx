import {observable, extendObservable, autorun, makeObservable} from 'mobx';
import axios from "axios";
import Config from '../../Config';

import SalesOrder from './SalesOrder';
import SalesOrderLine from './SalesOrderLine';

import CommonStore from "../Common/CommonStore";

const baseUrl = location.protocol
    + "//" + location.hostname
    + (location.port && ":" + location.port)
    + "/";

export default class SalesOrderStore {
    salesOrder;
    commonStore;
    validationErrors: string[] = [];
    salesOrderStatus: string = "";
    salesQuotationStatus: string = "";
    editMode = false;
    hasQuotation = false;

    RTotal = 0;
    GTotal = 0;
    TTotal = 0;
    
    constructor(quotationId: number, orderId: number) {
        this.commonStore = new CommonStore();
        this.salesOrder = new SalesOrder();
        extendObservable(this.salesOrder, {
            customerId: this.salesOrder.customerId,
            orderDate: this.salesOrder.orderDate,
            paymentTermId: this.salesOrder.paymentTermId,
            referenceNo: this.salesOrder.referenceNo,
            statusId: this.salesOrder.statusId,
            salesOrderLines: []
        });

        makeObservable(this, {
            validationErrors: observable,
            salesOrderStatus: observable,
            salesQuotationStatus: observable,
            editMode: observable,
            hasQuotation: observable,
            RTotal: observable,
            GTotal: observable,
            TTotal: observable,
        });

        autorun(() => this.computeTotals());

        if (quotationId !== undefined) {
            const result = axios.get(Config.API_URL + "sales/quotation?id=" + quotationId);
            result.then((result) => {
                this.changedCustomer(result.data.customerId);
                this.salesOrder.paymentTermId = result.data.paymentTermId;
                this.salesOrder.referenceNo = result.data.referenceNo;
                this.changedOrderDate(result.data.quotationDate);
                this.getQuotationStatus(result.data.statusId);
                this.hasQuotation = true; // this variable will serve as the footprint that it has a quotation and the edit button wil be disable.
                //for addition to save quotation to set status closed - order created
                this.salesOrder.quotationId = quotationId;
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
                }
                this.computeTotals();
                const nodes = document.getElementById("divSalesOrderForm")!.getElementsByTagName('*');
                for (let i = 0; i < nodes.length; i++) {
                    nodes[i].className += " disabledControl";
                }
            });
        }
        else if (orderId !== undefined) {
            const result = axios.get(Config.API_URL + "sales/salesorder?id=" + orderId);
            result.then((result) => {
                this.salesOrder.id = result.data.id;
                this.changedCustomer(result.data.customerId);
                this.salesOrder.paymentTermId = result.data.paymentTermId;
                this.salesOrder.referenceNo = result.data.referenceNo;
                this.changedOrderDate(result.data.orderDate);
                this.getOrderStatus(result.data.statusId);
                for (let i = 0; i < result.data.salesOrderLines.length; i++) {
                    this.addLineItem(
                        result.data.salesOrderLines[i].id,
                        result.data.salesOrderLines[i].itemId,
                        result.data.salesOrderLines[i].measurementId,
                        result.data.salesOrderLines[i].quantity,
                        result.data.salesOrderLines[i].amount,
                        result.data.salesOrderLines[i].discount,
                        result.data.salesOrderLines[i].code
                    );
                    this.updateLineItem(i, 'code', this.changeItemCode(result.data.salesOrderLines[i].itemId)!);
                }
                this.computeTotals();
                const nodes = document.getElementById("divSalesOrderForm")!.getElementsByTagName('*');
                for (let i = 0; i < nodes.length; i++) {
                    nodes[i].className += " disabledControl";
                }
            });
        }
        else
        {
            this.changedEditMode(true);
        }
    }

    computeTotals() {
        let rtotal = 0;
        let ttotal = 0;

        for (let i = 0; i < this.salesOrder.salesOrderLines.length; i++) {
            const lineItem = this.salesOrder.salesOrderLines[i];
            rtotal = rtotal + this.getLineTotal(i);
            axios.get(Config.API_URL + "tax/gettax?itemId=" + lineItem.itemId + "&partyId=" + this.salesOrder.customerId + "&type=1")
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

    saveNewSalesOrder() {
        if (this.salesOrder.orderDate === undefined)
            this.salesOrder.orderDate = new Date(new Date(Date.now()).toISOString().substring(0, 10));


        if (this.validation() && this.validationErrors.length === 0) {
            axios.post(Config.API_URL + "sales/savesalesorder", JSON.stringify(this.salesOrder),
                {
                    headers: {
                        'Content-type': 'application/json'
                    }
                })
                .then(() => {
                    window.location.href = baseUrl + 'sales/salesorders';
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
        if (this.salesOrder.customerId === undefined)
            this.validationErrors.push("Customer is required.");
        if (this.salesOrder.paymentTermId === undefined)
            this.validationErrors.push("Payment term is required.");
        if (this.salesOrder.orderDate === undefined)
            this.validationErrors.push("Date is required.");
        if (this.salesOrder.salesOrderLines === undefined || this.salesOrder.salesOrderLines.length < 1)
            this.validationErrors.push("Enter at least 1 line item.");
        if (this.salesOrder.salesOrderLines !== undefined && this.salesOrder.salesOrderLines.length > 0) {
            for (let i = 0; i < this.salesOrder.salesOrderLines.length; i++) {
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

    validationLine() {
        this.validationErrors = [];
        if (this.salesOrder.salesOrderLines !== undefined && this.salesOrder.salesOrderLines.length > 0) {
            for (let i = 0; i < this.salesOrder.salesOrderLines.length; i++) {
                if (this.salesOrder.salesOrderLines[i].itemId === undefined)
                    this.validationErrors.push("Item is required.");
                if (this.salesOrder.salesOrderLines[i].measurementId === undefined)
                    this.validationErrors.push("Uom is required.");
                if (this.salesOrder.salesOrderLines[i].quantity === undefined)
                    this.validationErrors.push("Quantity is required.");
                if (this.salesOrder.salesOrderLines[i].amount === undefined)
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
 

    getOrderStatus(statusId: number) {
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
        else if (statusId === 5)
            status = "Partially Invoiced";
        else if (statusId === 6)
            status = "Fully Invoiced";
        this.salesOrderStatus = status;
    }

    changeItemCode(itemId: number) {
        for (let x = 0; x < this.commonStore.items.length; x++) {
            const lineItem = this.commonStore.items[x] as SalesOrderLine;
            if (lineItem.id === itemId) {
                return lineItem.code;
            }
        }
    }

    changedReferenceNo(refNo: string) {
        this.salesOrder.referenceNo = refNo;
    }

    changedCustomer(custId: number) {
        this.salesOrder.customerId = custId;
    }

    changedOrderDate(date: Date) {
        this.salesOrder.orderDate = date;
    }

    changedPaymentTerm(termId: number) {
        this.salesOrder.paymentTermId = termId;
    }

    addLineItem(id: number, itemId: number, measurementId: number, quantity: number, amount: number, discount: number, code: string) {
        const newLineItem = new SalesOrderLine(id, itemId, measurementId, quantity, amount, discount, code);
        this.salesOrder.salesOrderLines.push(extendObservable(newLineItem, newLineItem));        
    }

    removeLineItem(row: number) {
        this.salesOrder.salesOrderLines.splice(row, 1);
    }

    updateLineItem(row: number, targetProperty: keyof SalesOrderLine, value: string | number) {
        if (this.salesOrder.salesOrderLines.length > 0)
            (this.salesOrder.salesOrderLines[row] as Record<keyof SalesOrderLine, string | number>)[targetProperty] = value;

        this.computeTotals();
    }

    getLineTotal(row: number) {
        let lineSum = 0;
        const lineItem = this.salesOrder.salesOrderLines[row];
        lineSum = (lineItem.quantity * lineItem.amount) - lineItem.discount;
        return lineSum;
    }

    changedEditMode(editMode: boolean) {
        this.editMode = editMode;
    }
}