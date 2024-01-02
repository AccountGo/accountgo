import * as React from "react";
import * as ReactDOM from "react-dom";
import {observer} from "mobx-react";
import * as accounting from "accounting";

import SelectVendor from "../Shared/Components/SelectVendor";
import SelectPaymentTerm from "../Shared/Components/SelectPaymentTerm";
import SelectLineItem from "../Shared/Components/SelectLineItem";
import SelectLineMeasurement from "../Shared/Components/SelectLineMeasurement";

import PurchaseInvoiceStore from "../Shared/Stores/Purchasing/PurchaseInvoiceStore";
import PurchaseInvoiceLine from "../Shared/Stores/Purchasing/PurchaseInvoiceLine";

const purchId = window.location.search.split("?purchId=")[1];
const invoiceId = window.location.search.split("?invoiceId=")[1];

const store = new PurchaseInvoiceStore(Number(purchId), Number(invoiceId));

// let baseUrl: string = location.protocol
//     + "//" + location.hostname
//     + (location.port && ":" + location.port)
//     + "/";

class ValidationErrors extends React.Component {
    render() {
        if (store.validationErrors !== undefined && store.validationErrors.length > 0) {
            const errors: string[] = [];
            store.validationErrors.map(function (item, index) {
                const errors: React.ReactNode[] = [];
                errors.push(<li key={index}>{item}</li>);
            });
            return (
                <div>
                    <ul>
                        {errors}
                    </ul>
                </div>

            );
        }
        return null;
    }
}
const ObservedValidationErrors = observer(ValidationErrors);

class EditButton extends React.Component {
    onClickEditButton() {
        // Remove " disabledControl" from current className
        const nodes = document.getElementById("divPurchaseInvoiceForm")?.getElementsByTagName('*');
        for (let i = 0; i < nodes!.length; i++) {
            const subStringLength = nodes![i].className.length - " disabledControl".length;
            nodes![i].className = nodes![i].className.substring(0, subStringLength);
        }
        store.changedEditMode(true);
    }
    render() {
        return (
            <a href="#" id="linkEdit" onClick={this.onClickEditButton}
                className={!store.purchaseInvoice.posted && !store.editMode
                    ? "btn"
                    : "btn inactiveLink"}>
                <i className="fa fa-edit"></i>
                Edit
            </a>
        );
    }
}
const ObservedEditButton = observer(EditButton);

class SavePurchaseInvoiceButton extends React.Component{
    saveNewPurchaseInvoice() {
        store.savePurchaseInvoice();
    }

    render() {
        return (
            <input type="button" value="Save" onClick={this.saveNewPurchaseInvoice.bind(this) }
                className={!store.purchaseInvoice.posted && store.editMode
                    ? "btn btn-sm btn-primary btn-flat pull-left"
                    : "btn btn-sm btn-primary btn-flat pull-left inactiveLink"}
                />
        );
    }
}
const ObservedSavePurchaseInvoiceButton = observer(SavePurchaseInvoiceButton);

class CancelPurchaseInvoiceButton extends React.Component {
    cancelOnClick() {
        const baseUrl = location.protocol
            + "//" + location.hostname
            + (location.port && ":" + location.port)
            + "/";

        window.location.href = baseUrl + 'purchasing/purchaseorders';
    }

    render() {
        return (
            <button type="button" className="btn btn-sm btn-default btn-flat pull-left" onClick={ this.cancelOnClick.bind(this) }>
                Close
            </button>
        );
    }
}

class PostButton extends React.Component {
    postOnClick() {
        store.postInvoice();
    }

    render() {
        return (
            <input type="button" value="Post" onClick={ this.postOnClick.bind(this) }
                className={!store.purchaseInvoice.posted && !store.editMode && store.purchaseInvoice.readyForPosting
                    ? "btn btn-sm btn-primary btn-flat btn-danger pull-right"
                    : "btn btn-sm btn-primary btn-flat btn-danger pull-right inactiveLink"} />
        );
    }
}
const ObservedPostButton = observer(PostButton);

class PurchaseInvoiceHeader extends React.Component {
    onChangeInvoiceDate(e: React.ChangeEvent<HTMLInputElement>) {
        store.changedInvoiceDate(new Date(e.target.value));
    }

    onChangeVendor(e: React.ChangeEvent<HTMLInputElement>) {
        store.changedVendor(Number(e.target.value));
    }

    onChangeReferenceNo(e: React.ChangeEvent<HTMLInputElement>) {
        store.changedReferenceNo(e.target.value);
    }
    
    render() {        
        return (
            <div className="box">
                <div className="card-header">
        <a data-toggle="collapse" href="#vendor-info" aria-expanded="true" aria-controls="vendor-info"><i className="fa fa-align-justify"></i></a> Vendor Information
        </div>
                <div className="card-body collapse show row" id="vendor-info">
                    <div className="col-sm-6">
                        <div className="row">
                            <div className="col-sm-2">Vendor</div>
                            <div className="col-sm-10"><SelectVendor store={store} selected={store.purchaseInvoice.vendorId} /></div>
                        </div>
                        <div className="row">
                            <div className="col-sm-2">Payment Term</div>
                            <div className="col-sm-10"><SelectPaymentTerm store={store} selected={store.purchaseInvoice.paymentTermId} /></div>
                        </div>
                    </div>
                    <div className="col-md-6">
                        <div className="row">
                            <div className="col-sm-2">Date</div>
                            <div className="col-sm-10"><input type="date" className="form-control pull-right" onChange={this.onChangeInvoiceDate.bind(this) }
                                value={store.purchaseInvoice.invoiceDate !== undefined ? store.purchaseInvoice.invoiceDate.toISOString().substring(0, 10) : new Date(Date.now()).toISOString().substring(0, 10) } /></div>
                        </div>
                        <div className="row">
                            <div className="col-sm-2">Reference no.</div>
                            <div className="col-sm-10"><input type="text" className="form-control"  value={store.purchaseInvoice.referenceNo || ''} onChange={this.onChangeReferenceNo.bind(this) }  /></div>
                        </div>
                        <div className="row">
                            <div className="col-sm-2">Status</div>
                            <div className="col-sm-10"><label>{store.purchaseInvoiceStatus}</label></div>

                        </div>
                    </div>
                </div>
            </div>
        );
    }
}
const ObservedPurchaseInvoiceHeader = observer(PurchaseInvoiceHeader);

class PurchaseInvoiceLines extends React.Component {
    addLineItem() {

        if (store.validationLine()) {
            const itemId: string = (document.getElementById("optNewItemId") as HTMLInputElement).value;
            const measurementId: string = (document.getElementById("optNewMeasurementId") as HTMLInputElement).value;
            const quantity: string = (document.getElementById("txtNewQuantity") as HTMLInputElement).value;
            const amount: string = (document.getElementById("txtNewAmount") as HTMLInputElement).value;
            const discount: string = (document.getElementById("txtNewDiscount") as HTMLInputElement).value;
            const code = (document.getElementById("txtNewCode") as HTMLInputElement).value;

            store.addLineItem(0, Number(itemId), Number(measurementId), 
            Number(quantity), Number(amount), Number(discount), code);

            (document.getElementById("optNewItemId") as HTMLInputElement).value = "";
            (document.getElementById("txtNewCode") as HTMLInputElement).value = "";
            (document.getElementById("optNewMeasurementId") as HTMLInputElement).value = "";
            (document.getElementById("txtNewQuantity") as HTMLInputElement).value = "1";
            (document.getElementById("txtNewAmount") as HTMLInputElement).value = "";
            (document.getElementById("txtNewDiscount") as HTMLInputElement).value = "";
        }
    }

    onClickRemoveLineItem(i: number) {
        store.removeLineItem(i);
    }

    onChangeQuantity(e: any) {
        store.updateLineItem(e.target.name, "quantity", e.target.value);
    }

    onChangeAmount(e: any) {
        store.updateLineItem(e.target.name, "amount", e.target.value);
    }

    onChangeDiscount(e: any) {
        store.updateLineItem(e.target.name, "discount", e.target.value);
    }

    onChangeItem(e: any) {
        store.updateLineItem(e.target.name, "itemId", e.target.value);
    }

    onChangeCode(e: any) {
        store.updateLineItem(e.target.name, "code", e.target.value);
    }


    onFocusOutItem(e: any, isNew: boolean, i: any) {
        let isExisting = false;
        for (let x = 0; x < store.commonStore.items.length; x++) {
            const lineItem = store.commonStore.items[x] as PurchaseInvoiceLine;
            if (lineItem.code == i.target.value) {
                isExisting = true;
                if (isNew) {
                    (document.getElementById("optNewItemId") as HTMLInputElement).value = lineItem.id.toString();
                    (document.getElementById("optNewMeasurementId") as HTMLInputElement).value = lineItem.measurementId.toString();
                    (document.getElementById("txtNewAmount") as HTMLInputElement).value = lineItem.amount.toString();
                    (document.getElementById("txtNewQuantity") as HTMLInputElement).value = "1";
                    document.getElementById("txtNewCode")!.style.borderColor = "";
                }
                else {
                    store.updateLineItem(e, "itemId", lineItem.id);
                    store.updateLineItem(e, "measurementId", lineItem.measurementId);
                    store.updateLineItem(e, "amount", lineItem.amount);
                    store.updateLineItem(e, "quantity", 1);
                    i.target.style.borderColor = "";
                }
            }
        }

        if (!isExisting)

            if (isNew) {
                (document.getElementById("optNewItemId") as HTMLInputElement).value = "";
                (document.getElementById("optNewMeasurementId") as HTMLInputElement).value = "";
                (document.getElementById("txtNewAmount") as HTMLInputElement).value = "";
                (document.getElementById("txtNewQuantity") as HTMLInputElement).value = "";
                document.getElementById("txtNewCode")!.style.borderColor = '#FF0000';
                //document.getElementById("txtNewCode").appendChild(span);
                // document.getElementById("txtNewCode").style.border = 'solid';
            }
            else {
                //store.updateLineItem(e, "itemId", "");
                //store.updateLineItem(e, "measurementId", "");
                //store.updateLineItem(e, "amount", "");
                //store.updateLineItem(e, "quantity", "");
                i.target.style.borderColor = "red";
                //i.target.appendChild(span);
                // i.target.style.border = "solid";

            }

    }   

    render() {        
        let newLine = 0;
        const lineItems = [];
        let lastIndex = 0;
        for (let i = 0; i < store.purchaseInvoice.purchaseInvoiceLines.length; i++) {
            newLine = newLine + 10;
            lineItems.push(
                <tr key={i}>
                    <td><label>{newLine}</label></td>
                    <td><SelectLineItem store={store} row={i} selected={store.purchaseInvoice.purchaseInvoiceLines[i].itemId} /></td>
                    <td><input type="text" className="form-control" name={i.toString()} value={store.purchaseInvoice.purchaseInvoiceLines[i].itemId} onChange={this.onChangeItem.bind(this) } /></td>
                    <td><SelectLineMeasurement row={i} store={store} selected={store.purchaseInvoice.purchaseInvoiceLines[i].measurementId} /></td>
                    <td><input type="text" className="form-control" name={i.toString()} value={store.purchaseInvoice.purchaseInvoiceLines[i].quantity} onChange={this.onChangeQuantity.bind(this)} /></td>
                    <td><input type="text" className="form-control" name={i.toString()} value={store.purchaseInvoice.purchaseInvoiceLines[i].amount} onChange={this.onChangeAmount.bind(this) } /></td>
                    <td><input type="text" className="form-control" name={i.toString()} value={store.purchaseInvoice.purchaseInvoiceLines[i].discount} onChange={this.onChangeDiscount.bind(this) } /></td>
                    <td>{store.getLineTotal(i)}</td>
                    <td>
                        <button type="button" className="btn btn-box-tool" onClick={this.onClickRemoveLineItem.bind(this, i) }>
                            <i className="fa fa-fw fa-times"></i>
                        </button>
                    </td>
                </tr>
            );
            lastIndex = i;
        }
        return (
            <div className="card">
                <div className="card-header">
        <a data-toggle="collapse" href="#line-items" aria-expanded="true" aria-controls="line-items"><i className="fa fa-align-justify"></i></a> Line Items
        </div>
                <div className="card-body collapse show table-responsive" id="line-items">
                    <table className="table table-hover">
                        <thead>
                            <tr>
                                <td>No</td>
                                <td>Item Id</td>
                                <td>Item Name</td>
                                <td>Measurement</td>
                                <td>Quantity</td>
                                <td>Amount</td>
                                <td>Discount</td>
                                <td>Line Total</td>
                                <td></td>
                            </tr>
                        </thead>
                        <tbody>
                            {lineItems}
                            <tr>
                                <td></td>
                                <td><SelectLineItem store={store} controlId="optNewItemId" /></td>
                                <td><input className="form-control" type="text" id="txtNewCode" onBlur={this.onFocusOutItem.bind(this, lastIndex, true) } /></td>
                                <td><SelectLineMeasurement store={store} controlId="optNewMeasurementId" /></td>
                                <td><input type="text" className="form-control" id="txtNewQuantity" /></td>
                                <td><input type="text" className="form-control" id="txtNewAmount" /></td>
                                <td><input type="text" className="form-control" id="txtNewDiscount" /></td>
                                <td></td>
                                <td>
                                    <button type="button" className="btn btn-box-tool" onClick={this.addLineItem}>
                                        <i className="fa fa-fw fa-check"></i>
                                    </button>
                                </td>
                            </tr>
                        </tbody>
                    </table>
                </div>
            </div>
        );
    }
}
const ObservedPurchaseInvoiceLines = observer(PurchaseInvoiceLines);

class PurchaseInvoiceTotals extends React.Component {
    render() {
        return (
            <div className="card">
                <div className="card-body">
                    <div className="row">
                        <div className="col-md-2"><label>SubTotal: </label></div>
                        <div className="col-md-2">{accounting.formatMoney(store.RTotal, { symbol: "", format: "%s%v" }) }</div>
                        <div className="col-md-2"><label>Tax: </label></div>
                        <div className="col-md-2">{accounting.formatMoney(store.TTotal, { symbol: "", format: "%s%v" }) }</div>
                        <div className="col-md-2"><label>Total: </label></div>
                        <div className="col-md-2">{accounting.formatMoney(store.GTotal, { symbol: "", format: "%s%v" }) }</div>
                    </div>
                </div>
            </div>
        );
    }
}

class PurchaseInvoice extends React.Component {
    render() {
        return (
            <div>
                <div id="divActionsTop">
                    <ObservedEditButton />
                </div>
                <div id="divPurchaseInvoiceForm">
                    <ObservedValidationErrors />
                    <ObservedPurchaseInvoiceHeader />
                    <ObservedPurchaseInvoiceLines />
                    <PurchaseInvoiceTotals />
                </div>
                <div id="divActionsBottom">
                    <ObservedSavePurchaseInvoiceButton />
                    <CancelPurchaseInvoiceButton />
                    <ObservedPostButton />
                </div>
            </div>
            );
    }
}
const ObservedPurchaseInvoice = observer(PurchaseInvoice);

export default ObservedPurchaseInvoice;
