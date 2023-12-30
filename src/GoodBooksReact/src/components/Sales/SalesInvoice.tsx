import * as React from "react";
import * as ReactDOM from "react-dom";
import {observer} from "mobx-react";
// import from "accounting" npm package
import * as accounting from "accounting";

import SelectCustomer from "../Shared/Components/SelectCustomer";
import SelectPaymentTerm from "../Shared/Components/SelectPaymentTerm";
import SelectLineItem from "../Shared/Components/SelectLineItem";
import SelectLineMeasurement from "../Shared/Components/SelectLineMeasurement";
import SalesInvoiceLine from "../Shared/Stores/Sales/SalesInvoiceLine";
import SalesInvoiceStore from "../Shared/Stores/Sales/SalesInvoiceStore";

let orderId = window.location.search.split("?orderId=")[1];
let invoiceId = window.location.search.split("?invoiceId=")[1];

let store = new SalesInvoiceStore(orderId, invoiceId);

class ValidationErrors extends React.Component<any, {}>{
    render() {
        if (store.validationErrors !== undefined && store.validationErrors.length > 0) {
            var errors: any = [];
            store.validationErrors.map(function (item: any, index: any) {
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

class EditButton extends React.Component<any, {}>{
    onClickEditButton() {
        // Remove " disabledControl" from current className
        var nodes = document.getElementById("divSalesInvoiceForm")!.getElementsByTagName('*');
        for (var i = 0; i < nodes.length; i++) {
            var subStringLength = nodes[i].className.length - " disabledControl".length;
            nodes[i].className = nodes[i].className.substring(0, subStringLength);
        }
        store.changedEditMode(true)
    }
    render() {
        return (
            <a href="#" id="linkEdit" onClick={this.onClickEditButton}
                className={!store.salesInvoice.posted && !store.editMode 
                    ? "btn"
                    : "btn inactiveLink"}>
                <i className="fa fa-edit"></i>
                Edit
            </a>
        );
    }
}
const ObservedEditButton = observer(EditButton);

class SaveInvoiceButton extends React.Component<any, {}>{
    saveNewSalesInvoice() {
        store.saveNewSalesInvoice();
    }
    onChangeReferenceNo(e: any) {
        store.changedReferenceNo(e.target.value);
    }
    render() {
        return (
            <input type="button" value="Save" onClick={this.saveNewSalesInvoice.bind(this) }
                className={!store.salesInvoice.posted && store.editMode 
                    ? "btn btn-sm btn-primary btn-flat pull-left"
                    : "btn btn-sm btn-primary btn-flat pull-left inactiveLink"}
                />
            );
    }
}
const ObservedSaveInvoiceButton = observer(SaveInvoiceButton);

class CancelInvoiceButton extends React.Component<any, {}>{
    cancelOnClick() {
        let baseUrl = location.protocol
            + "//" + location.hostname
            + (location.port && ":" + location.port)
            + "/";

        window.location.href = baseUrl + 'sales/salesinvoices';
    }

    render() {
        return (
            <button type="button" className="btn btn-sm btn-default btn-flat pull-left" onClick={ this.cancelOnClick.bind(this) }>
                Close
            </button>
        );
    }
}
const ObservedCancelInvoiceButton = observer(CancelInvoiceButton);

class PrintButton extends React.Component<any, {}>{
    printOnClick() {
        store.printInvoice();
    }

    render() {
        return (

            <input type="button" value="Print" onClick={ this.printOnClick.bind(this) }
                className={store.salesInvoice.posted 
                    ? "btn btn-sm btn-primary btn-flat pull-right"
                    : "btn btn-sm btn-primary btn-flat pull-right inactiveLink"} />
        );
    }
}
const ObservedPrintButton = observer(PrintButton);

class PostButton extends React.Component<any, {}>{
    postOnClick() {
        store.postInvoice();
    }
  
    render() {
        return (
        
            <input type="button" value="Post" onClick={ this.postOnClick.bind(this) }
                className={!store.salesInvoice.posted && store.salesInvoice.readyForPosting && !store.editMode 
                    ? "btn btn-sm btn-primary btn-flat btn-danger pull-right"
                    : "btn btn-sm btn-primary btn-flat btn-danger pull-right inactiveLink"} />
        );
    }
}
const ObservedPostButton = observer(PostButton);

class SalesInvoiceHeader extends React.Component<any, {}>{
    onChangeInvoiceDate(e: any) {
        store.changedInvoiceDate(e.target.value);
    }
    onChangeReferenceNo(e: any) {
        store.changedReferenceNo(e.target.value);
    }
    render() {        
        return (
            <div className="card">
                <div className="card-header">
                <a data-toggle="collapse" href="#customer-info" aria-expanded="true" aria-controls="customer-info"><i className="fa fa-align-justify"></i></a> Customer Information
                </div>
                <div className="card-body collapse show row" id="customer-info">
                    <div className="col-md-6">
                        <div className="row">
                            <div className="col-sm-2">Customer</div>
                            <div className="col-sm-10"><SelectCustomer store={store} selected={store.salesInvoice.customerId} /></div>
                        </div>
                        <div className="row">
                            <div className="col-sm-2">Payment Term</div>
                            <div className="col-sm-10"><SelectPaymentTerm store={store} selected={store.salesInvoice.paymentTermId} /></div>
                        </div>
                    </div>
                    <div className="col-md-6">
                        <div className="row">
                            <div className="col-sm-2">Date</div>
                            <div className="col-sm-10"><input type="date" className="form-control pull-right" onChange={this.onChangeInvoiceDate.bind(this) }
                                value={store.salesInvoice.invoiceDate !== undefined ? store.salesInvoice.invoiceDate.toISOString().substring(0, 10) : new Date(Date.now()).toISOString().substring(0, 10) } /></div>
                        </div>
                        <div className="row">
                            <div className="col-sm-2">Reference no.</div>
                            <div className="col-sm-10"><input type="text" className="form-control"  value={store.salesInvoice.referenceNo || ''} onChange={this.onChangeReferenceNo.bind(this) }  /></div>
                        </div>
                    </div>
</div>
                    </div>
        );
    }
}
const ObservedSalesInvoiceHeader = observer(SalesInvoiceHeader);

class SalesInvoiceLines extends React.Component<any, {}>{
    addLineItem() {

        if (store.validationLine()) {
            var itemId: any = (document.getElementById("optNewItemId") as HTMLInputElement).value;
            var measurementId: any = (document.getElementById("optNewMeasurementId") as HTMLInputElement).value;
            var quantity: any = (document.getElementById("txtNewQuantity") as HTMLInputElement).value;
            var amount: any = (document.getElementById("txtNewAmount") as HTMLInputElement).value;
            var discount: any = (document.getElementById("txtNewDiscount") as HTMLInputElement).value;
            var code: any = (document.getElementById("txtNewCode") as HTMLInputElement).value;

            //console.log(`itemId: ${itemId} | measurementId: ${measurementId} | quantity: ${quantity} | amount: ${amount} | discount: ${discount}`);
            store.addLineItem(0, itemId, measurementId, quantity, amount, discount, code);

            (document.getElementById("optNewItemId") as HTMLInputElement).value = "";
            (document.getElementById("txtNewCode") as HTMLInputElement).value = "";
            (document.getElementById("optNewMeasurementId") as HTMLInputElement).value = "";
            (document.getElementById("txtNewQuantity") as HTMLInputElement).value = "1";
            (document.getElementById("txtNewAmount") as HTMLInputElement).value = "";
            (document.getElementById("txtNewDiscount") as HTMLInputElement).value = "";
        }
    }

    onClickRemoveLineItem(i: any) {
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

    onChangeCode(e: any) {
        store.updateLineItem(e.target.name, "code", e.target.value);
    }

    onFocusOutItem(e: any, isNew: any, i: any) {
        var isExisting = false;
        for (var x = 0; x < store.commonStore.items.length; x++) {
            var lineitem = store.commonStore.items[x] as SalesInvoiceLine
            if (lineitem.code === i.target.value) {
                isExisting = true;
                if (isNew) {
                    (document.getElementById("optNewItemId") as HTMLInputElement).value = lineitem.id.toString();
                    (document.getElementById("optNewMeasurementId") as HTMLInputElement).value = lineitem.measurementId.toString();
                    (document.getElementById("txtNewAmount") as HTMLInputElement).value = lineitem.amount.toString();
                    (document.getElementById("txtNewQuantity") as HTMLInputElement).value = "1";
                    document.getElementById("txtNewCode")!.style.borderColor = "";
                }
                else {
                    store.updateLineItem(e, "itemId", lineitem.id.toString());
                    store.updateLineItem(e, "measurementId", lineitem.measurementId.toString());
                    store.updateLineItem(e, "amount", lineitem.amount.toString());
                    store.updateLineItem(e, "quantity", "1");
                    i.target.style.borderColor = "";
                }
            }
        }

        if (!isExisting) {
            if (isNew) {
                (document.getElementById("optNewItemId") as HTMLInputElement).value = "";
                (document.getElementById("optNewMeasurementId") as HTMLInputElement).value = "";
                (document.getElementById("txtNewAmount") as HTMLInputElement).value = "";
                (document.getElementById("txtNewQuantity") as HTMLInputElement).value = "";
                document.getElementById("txtNewCode")!.style.borderColor = '#FF0000';
            }
            else {
                i.target.style.borderColor = "red";
            }
        }
    }   

    render() {    
        var newLine = 0;    
        var lineItems = [];
        for (var i = 0; i < store.salesInvoice.salesInvoiceLines.length; i++) {
            newLine = newLine + 10;
            lineItems.push(
                <tr key={i}>
                    <td><label>{newLine}</label></td>
                    <td><SelectLineItem store={store} row={i} selected={store.salesInvoice.salesInvoiceLines[i].itemId} /></td>
                    <td><input className="form-control" type="text" name={i.toString()} value={store.salesInvoice.salesInvoiceLines[i].code} onBlur={this.onFocusOutItem.bind(this, i, false) } onChange={this.onChangeCode.bind(this) } /></td>
                    <td><SelectLineMeasurement row={i} store={store} selected={store.salesInvoice.salesInvoiceLines[i].measurementId} /></td>
                    <td><input type="text" className="form-control" name={i.toString()} value={store.salesInvoice.salesInvoiceLines[i].quantity} onChange={this.onChangeQuantity.bind(this)} /></td>
                    <td><input type="text" className="form-control" name={i.toString()} value={store.salesInvoice.salesInvoiceLines[i].amount} onChange={this.onChangeAmount.bind(this) } /></td>
                    <td><input type="text" className="form-control" name={i.toString()} value={store.salesInvoice.salesInvoiceLines[i].discount} onChange={this.onChangeDiscount.bind(this) } /></td>
                    <td>{store.getLineTotal(i) }</td>
                    <td>
                        <button type="button" className="btn btn-box-tool" onClick={this.onClickRemoveLineItem.bind(this, i) }>
                            <i className="fa fa-fw fa-times"></i>
                        </button>
                    </td>
                </tr>
            );
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
                                <td>Code</td>
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
                                <td><input className="form-control" type="text" id="txtNewQuantity" /></td>
                                <td><input className="form-control" type="text" id="txtNewAmount" /></td>
                                <td><input className="form-control" type="text" id="txtNewDiscount" /></td>
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
const ObservedSalesInvoiceLines = observer(SalesInvoiceLines);

class SalesInvoiceTotals extends React.Component<any, {}>{
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
const ObservedSalesInvoiceTotals = observer(SalesInvoiceTotals);

class SalesInvoice extends React.Component<any, {}> {
    render() {
        return (
            <div>
                <div id="divActionsTop">
                    <ObservedEditButton />
                </div>
                <div id="divSalesInvoiceForm">
                    <ObservedValidationErrors />
                    <ObservedSalesInvoiceHeader />
                    <ObservedSalesInvoiceLines />
                    <ObservedSalesInvoiceTotals />
                </div>
                <div id="divActionsBottom">
                    <ObservedSaveInvoiceButton />
                    <ObservedCancelInvoiceButton />
                    <ObservedPrintButton />
                    <ObservedPostButton />
                </div>
            </div>
            );
    }
}
const ObservedSalesInvoice = observer(SalesInvoice);

export default ObservedSalesInvoice;

ReactDOM.render(<ObservedSalesInvoice />, document.getElementById("divSalesInvoice"));