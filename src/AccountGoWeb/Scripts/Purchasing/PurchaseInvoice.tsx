import * as React from "react";
import * as ReactDOM from "react-dom";
import {observer} from "mobx-react";
import * as accounting from "accounting";

import Config = require("Config");

import SelectVendor from "../Shared/Components/SelectVendor";
import SelectPaymentTerm from "../Shared/Components/SelectPaymentTerm";
import SelectLineItem from "../Shared/Components/SelectLineItem";
import SelectLineMeasurement from "../Shared/Components/SelectLineMeasurement";

import PurchaseInvoiceStore from "../Shared/Stores/Purchasing/PurchaseInvoiceStore";

let purchId = window.location.search.split("?purchId=")[1];
let invoiceId = window.location.search.split("?invoiceId=")[1];

let store = new PurchaseInvoiceStore(purchId, invoiceId);

let baseUrl = location.protocol
    + "//" + location.hostname
    + (location.port && ":" + location.port)
    + "/";

@observer
class ValidationErrors extends React.Component<any, {}>{
    render() {
        if (store.validationErrors !== undefined && store.validationErrors.length > 0) {
            var errors = [];
            store.validationErrors.map(function (item, index) {
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

@observer
class EditButton extends React.Component<any, {}>{
    onClickEditButton() {
        // Remove " disabledControl" from current className
        var nodes = document.getElementById("divPurchaseInvoiceForm").getElementsByTagName('*');
        for (var i = 0; i < nodes.length; i++) {
            var subStringLength = nodes[i].className.length - " disabledControl".length;
            nodes[i].className = nodes[i].className.substring(0, subStringLength);
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

@observer
class SavePurchaseInvoiceButton extends React.Component<any, {}>{
    saveNewPurchaseInvoice(e) {
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

class CancelPurchaseInvoiceButton extends React.Component<any, {}>{
    cancelOnClick() {
        let baseUrl = location.protocol
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

@observer
class PostButton extends React.Component<any, {}>{
    postOnClick(e) {
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

@observer
class PurchaseInvoiceHeader extends React.Component<any, {}>{
    onChangeInvoiceDate(e) {
        store.changedInvoiceDate(e.target.value);
    }
    onChangeVendor(e) {
        store.changedVendor(e.target.value);
    }
    onChangeReferenceNo(e) {
        store.changedReferenceNo(e.target.value);
    }
    render() {        
        return (
            <div className="box">
                <div className="box-header with-border">
                    <h3 className="box-title">Vendor Information</h3>
                    <div className="box-tools pull-right">
                        <button type="button" className="btn btn-box-tool" data-widget="collapse" data-toggle="tooltip" title="Collapse">
                            <i className="fa fa-minus"></i>
                        </button>
                    </div>
                </div>
                <div className="box-body">
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
                                value={store.purchaseInvoice.invoiceDate !== undefined ? store.purchaseInvoice.invoiceDate.substring(0, 10) : new Date(Date.now()).toISOString().substring(0, 10) } /></div>
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

@observer
class PurchaseInvoiceLines extends React.Component<any, {}>{
    addLineItem() {

        if (store.validationLine()) {

            var itemId, measurementId, quantity, amount, discount;
            itemId = (document.getElementById("optNewItemId") as HTMLInputElement).value;
            measurementId = (document.getElementById("optNewMeasurementId") as HTMLInputElement).value;
            quantity = (document.getElementById("txtNewQuantity") as HTMLInputElement).value;
            amount = (document.getElementById("txtNewAmount") as HTMLInputElement).value;
            discount = (document.getElementById("txtNewDiscount") as HTMLInputElement).value;

            //console.log(`itemId: ${itemId} | measurementId: ${measurementId} | quantity: ${quantity} | amount: ${amount} | discount: ${discount}`);
            store.addLineItem(0, itemId, measurementId, quantity, amount, discount);

            (document.getElementById("optNewItemId") as HTMLInputElement).value = "";
            (document.getElementById("txtNewCode") as HTMLInputElement).value = "";
            (document.getElementById("optNewMeasurementId") as HTMLInputElement).value = "";
            (document.getElementById("txtNewQuantity") as HTMLInputElement).value = "1";
            (document.getElementById("txtNewAmount") as HTMLInputElement).value = "";
            (document.getElementById("txtNewDiscount") as HTMLInputElement).value = "";
        }
    }

    onClickRemoveLineItem(i, e) {
        store.removeLineItem(i);
    }

    onChangeQuantity(e) {
        store.updateLineItem(e.target.name, "quantity", e.target.value);
    }

    onChangeAmount(e) {
        store.updateLineItem(e.target.name, "amount", e.target.value);
    }

    onChangeDiscount(e) {
        store.updateLineItem(e.target.name, "discount", e.target.value);
    }

    onChangeItem(e) {
        store.updateLineItem(e.target.name, "itemId", e.target.value);
    }

    onChangeCode(e) {
        store.updateLineItem(e.target.name, "code", e.target.value);
    }


    onFocusOutItem(e, isNew, i) {

        var isExisting = false;
        for (var x = 0; x < store.commonStore.items.length; x++) {
            if (store.commonStore.items[x].code == i.target.value) {
                isExisting = true;
                if (isNew) {
                    (document.getElementById("optNewItemId") as HTMLInputElement).value = store.commonStore.items[x].id;
                    (document.getElementById("optNewMeasurementId") as HTMLInputElement).value = store.commonStore.items[x].sellMeasurementId;
                    (document.getElementById("txtNewAmount") as HTMLInputElement).value = store.commonStore.items[x].price;
                    (document.getElementById("txtNewQuantity") as HTMLInputElement).value = "1";
                    document.getElementById("txtNewCode").style.borderColor = "";
                }
                else {
                    store.updateLineItem(e, "itemId", store.commonStore.items[x].id);
                    store.updateLineItem(e, "measurementId", store.commonStore.items[x].sellMeasurementId);
                    store.updateLineItem(e, "amount", store.commonStore.items[x].price);
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
                document.getElementById("txtNewCode").style.borderColor = '#FF0000';
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
        var newLine = 0;
        var lineItems = [];
        for (var i = 0; i < store.purchaseInvoice.purchaseInvoiceLines.length; i++) {
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
        }
        return (
            <div className="box">
                <div className="box-header with-border">
                    <h3 className="box-title">Line Items</h3>
                    <div className="box-tools pull-right">
                        <button type="button" className="btn btn-box-tool" data-widget="collapse" data-toggle="tooltip" title="Collapse">
                            <i className="fa fa-minus"></i>
                        </button>
                    </div>
                </div>
                <div className="box-body table-responsive">
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
                                <td><input className="form-control" type="text" id="txtNewCode" onBlur={this.onFocusOutItem.bind(this, i, true) } /></td>
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

@observer
class PurchaseInvoiceTotals extends React.Component<any, {}>{
    render() {
        return (
            <div className="box">
                <div className="box-body">
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

@observer
export default class PurchaseInvoice extends React.Component<any, {}> {
    render() {
        return (
            <div>
                <div id="divActionsTop">
                    <EditButton />
                </div>
                <div id="divPurchaseInvoiceForm">
                    <ValidationErrors />
                    <PurchaseInvoiceHeader />
                    <PurchaseInvoiceLines />
                    <PurchaseInvoiceTotals />
                </div>
                <div id="divActionsBottom">
                    <SavePurchaseInvoiceButton />
                    <CancelPurchaseInvoiceButton />
                    <PostButton />
                </div>
            </div>
            );
    }
}

ReactDOM.render(<PurchaseInvoice />, document.getElementById("divPurchaseInvoice"));