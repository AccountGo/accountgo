import * as React from "react";
import * as ReactDOM from "react-dom";
import {observer} from "mobx-react";

import Config = require("Config");

import SelectCustomer from "../Shared/Components/SelectCustomer";
import SelectPaymentTerm from "../Shared/Components/SelectPaymentTerm";
import SelectLineItem from "../Shared/Components/SelectLineItem";
import SelectLineMeasurement from "../Shared/Components/SelectLineMeasurement";

import SalesInvoiceStore from "../Shared/Stores/Sales/SalesInvoiceStore";

let store = new SalesInvoiceStore();

class SaveInvoiceButton extends React.Component<any, {}>{
    saveNewSalesInvoice(e) {

    }

    render() {
        return (
            <input type="button" value="Save" onClick={this.saveNewSalesInvoice.bind(this)} />
            );
    }
}

class CancelInvoiceButton extends React.Component<any, {}>{
    render() {
        return (
            <input type="button" value="Cancel" />
        );
    }
}

@observer
class SalesInvoiceHeader extends React.Component<any, {}>{
    onChangeInvoiceDate(e) {
        store.changedInvoiceDate(e.target.value);
    }
    render() {        
        return (
            <div className="box">
                <div className="box-header with-border">
                    <h3 className="box-title">Customer Information</h3>
                    <div className="box-tools pull-right">
                        <button type="button" className="btn btn-box-tool" data-widget="collapse" data-toggle="tooltip" title="Collapse">
                            <i className="fa fa-minus"></i>
                        </button>
                    </div>
                </div>
                <div className="box-body">
                    <div className="col-md-6">
                        <div className="row">
                            <div className="col-sm-2">Customer</div>
                            <div className="col-sm-10"><SelectCustomer store={store} /></div>
                        </div>
                        <div className="row">
                            <div className="col-sm-2">Payment Term</div>
                            <div className="col-sm-10"><SelectPaymentTerm store={store} /></div>
                        </div>
                    </div>
                    <div className="col-md-6">
                        <div className="row">
                            <div className="col-sm-2">Date</div>
                            <div className="col-sm-10"><input type="date" className="form-control pull-right" onChange={this.onChangeInvoiceDate.bind(this) } defaultValue={store.salesInvoice.invoiceDate} /></div>
                        </div>
                        <div className="row">
                            <div className="col-sm-2">Reference no.</div>
                            <div className="col-sm-10"><input type="text" className="form-control" /></div>
                        </div>
                    </div>
                </div>
            </div>
        );
    }
}

@observer
class SalesInvoiceLines extends React.Component<any, {}>{
    addLineItem() {
        var itemId, measurementId, quantity, amount, discount;
        itemId = (document.getElementById("optNewItemId") as HTMLInputElement).value;
        measurementId = (document.getElementById("optNewMeasurementId") as HTMLInputElement).value;
        quantity = (document.getElementById("txtNewQuantity") as HTMLInputElement).value;
        amount = (document.getElementById("txtNewAmount") as HTMLInputElement).value;
        discount = (document.getElementById("txtNewDiscount") as HTMLInputElement).value;

        console.log(`itemId: ${itemId} | measurementId: ${measurementId} | quantity: ${quantity} | amount: ${amount} | discount: ${discount}`);
        store.addLineItem(itemId, measurementId, quantity, amount, discount);

        (document.getElementById("txtNewQuantity") as HTMLInputElement).value = "1";
        (document.getElementById("txtNewAmount") as HTMLInputElement).value = "0";
        (document.getElementById("txtNewDiscount") as HTMLInputElement).value = "0";
    }

    onClickRemoveLineItem(e) {
        store.removeLineItem(e.target.name);
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

    render() {        
        var lineItems = [];
        for (var i = 0; i < store.salesInvoice.salesInvoiceLines.length; i++) {
            lineItems.push(
                <tr key={i}>
                    <td><SelectLineItem store={store} row={i} selected={store.salesInvoice.salesInvoiceLines[i].itemId} /></td>
                    <td>{store.salesInvoice.salesInvoiceLines[i].itemId}</td>
                    <td><SelectLineMeasurement row={i} store={store} selected={store.salesInvoice.salesInvoiceLines[i].measurementId} />{store.salesInvoice.salesInvoiceLines[i].measurementId}</td>
                    <td><input type="text" className="form-control" name={i} value={store.salesInvoice.salesInvoiceLines[i].quantity} onChange={this.onChangeQuantity.bind(this)} /></td>
                    <td><input type="text" className="form-control" name={i} value={store.salesInvoice.salesInvoiceLines[i].amount} onChange={this.onChangeAmount.bind(this) } /></td>
                    <td><input type="text" className="form-control" name={i} value={store.salesInvoice.salesInvoiceLines[i].discount} onChange={this.onChangeDiscount.bind(this) } /></td>
                    <td>{store.lineTotal(i)}</td>
                    <td><input type="button" name={i} value="Remove" onClick={this.onClickRemoveLineItem.bind(this) } /></td>
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
                                <td><SelectLineItem store={store} controlId="optNewItemId" /></td>
                                <td>Item Name</td>
                                <td><SelectLineMeasurement store={store} controlId="optNewMeasurementId" /></td>
                                <td><input className="form-control" type="text" id="txtNewQuantity" /></td>
                                <td><input className="form-control" type="text" id="txtNewAmount" /></td>
                                <td><input className="form-control" type="text" id="txtNewDiscount" /></td>
                                <td></td>
                                <td><input type="button" value="Add" onClick={this.addLineItem} /></td>
                            </tr>
                        </tbody>
                    </table>
                </div>
            </div>
        );
    }
}

@observer
class SalesInvoiceTotals extends React.Component<any, {}>{
    render() {
        return (
            <div className="box">
                <div className="box-body">
                    <div className="row">
                        <div className="col-md-2"><label>Running Total: </label></div>
                        <div className="col-md-2">{0}</div>
                        <div className="col-md-2"><label>Tax Total: </label></div>
                        <div className="col-md-2">{0}</div>
                        <div className="col-md-2"><label>Grand Total: </label></div>
                        <div className="col-md-2">{store.grandTotal() }</div>
                    </div>
                </div>
            </div>
        );
    }
}

export default class AddSalesInvoice extends React.Component<any, {}> {
    render() {
        return (
            <div>
                <SalesInvoiceHeader />
                <SalesInvoiceLines />
                <SalesInvoiceTotals />
                <div>
                    <SaveInvoiceButton />
                    <CancelInvoiceButton />
                </div>
            </div>
            );
    }
}

ReactDOM.render(<AddSalesInvoice />, document.getElementById("divAddSalesInvoice"));