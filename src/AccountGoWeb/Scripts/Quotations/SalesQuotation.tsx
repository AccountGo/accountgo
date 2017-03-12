import * as React from "react";
import * as ReactDOM from "react-dom";
import {observer} from "mobx-react";
import * as d3 from "d3";
import Config = require("Config");
import {autorun, observable} from 'mobx';
import * as accounting from "accounting";

import SelectCustomer from "../Shared/Components/SelectCustomer";
import SelectPaymentTerm from "../Shared/Components/SelectPaymentTerm";
import SelectLineItem from "../Shared/Components/SelectLineItem";
import SelectLineMeasurement from "../Shared/Components/SelectLineMeasurement";

import SalesQuotationLine from "../Shared/Stores/Quotations/SalesQuotationLine";
import SalesQuotationStore from "../Shared/Stores/Quotations/SalesQuotationStore";


let quotationId = window.location.search.split("?id=")[1];

let store = new SalesQuotationStore(quotationId);


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
class SaveQuotationButton extends React.Component<any, {}>{
    saveNewSalesQuotation(e) {
        store.saveNewQuotation();
    }
    //className = {!store.salesInvoice.posted && store.editMode 
    render() {
        return (
         <input type="button" value="Save" onClick={this.saveNewSalesQuotation.bind(this) }
                className={(store.salesQuotation.statusId == 0 || store.salesQuotation.statusId == undefined) && !store.editMode
                ? "btn btn-sm btn-primary btn-flat pull-left"
                : "btn btn-sm btn-primary btn-flat pull-left inactiveLink"}
            />
            );
    }
}

class CancelQuotationButton extends React.Component<any, {}>{
    cancelOnClick() {
        let baseUrl = location.protocol
            + "//" + location.hostname
            + (location.port && ":" + location.port)
            + "/";

        window.location.href = baseUrl + 'quotations';
    }

    render() {
        return (
            <input type="button" value={(store.editMode ? "Cancel" : "Close") } className="btn btn-sm btn-default btn-flat pull-left" onClick={store.editMode ? store.changedEditMode(false) : this.cancelOnClick.bind(this) }                
            />
        );
    }
}

@observer
class SalesQuotationHeader extends React.Component<any, {}>{
    onChangeQuotationDate(e) {
        store.changedQuotationDate(e.target.value);
    }

 

    onChangeCustomer(e) {
        alert('');
    }

    onChangeReferenceNo(e) {
        store.changedReferenceNo(e.target.value);
    }


    render() {
        return (
            <div className="box">
                <div className="box-header with-border">
                    <h3 className="box-title">Customer Information - <span>{store.salesQuotation.customerId}</span></h3>
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
                            <div className="col-sm-10"><SelectCustomer store={store} selected={store.salesQuotation.customerId} /></div>
                        </div>
                        <div className="row">
                            <div className="col-sm-2">Payment Term</div>
                            <div className="col-sm-10"><SelectPaymentTerm store={store} selected={store.salesQuotation.paymentTermId} /></div>
                        </div>
                    </div>
                    <div className="col-md-6">
                        <div className="row">
                            <div className="col-sm-2">Date</div>
                            <div className="col-sm-10"><input type="date" className="form-control pull-right"  onChange={this.onChangeQuotationDate.bind(this) } 
                                value={store.salesQuotation.quotationDate !== undefined ? store.salesQuotation.quotationDate.substring(0, 10) : new Date(Date.now()).toISOString().substring(0, 10) } /></div>
 
                        </div>
                        <div className="row">
                            <div className="col-sm-2">Reference no.</div>
                            <div className="col-sm-10"><input type="text" className="form-control"  value={store.salesQuotation.referenceNo || ''} onChange={this.onChangeReferenceNo.bind(this) }  /></div>

                        </div>
                        <div className="row">
                            <div className="col-sm-2">Status</div>
                            <div className="col-sm-10"><label>{store.salesQuotationStatus}</label></div>

                        </div>
                    </div>
                </div>
            </div>
        );
    }
}

@observer
class SalesQuotationLines extends React.Component<any, {}>{
   


    addLineItem() {

        if (store.validationLine()) {

            var itemId, measurementId, quantity, amount, discount, code;
            itemId = (document.getElementById("optNewItemId") as HTMLInputElement).value;

            measurementId = (document.getElementById("optNewMeasurementId") as HTMLInputElement).value;
            quantity = (document.getElementById("txtNewQuantity") as HTMLInputElement).value;
            amount = (document.getElementById("txtNewAmount") as HTMLInputElement).value;
            discount = (document.getElementById("txtNewDiscount") as HTMLInputElement).value;
            code = (document.getElementById("txtNewCode") as HTMLInputElement).value;
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
        
    @observable lineNo = 0;
 
 
    render() {
        var newLine = 0;
        var lineItems = [];

        for (var i = 0; i < store.salesQuotation.salesQuotationLines.length; i++) {
            newLine = newLine + 10;
            //var initialCode = this.onloadCode(store.salesQuotation.salesQuotationLines[i].itemId); // this is for initial value of code


            lineItems.push(
                <tr key={i}>
                    <td><label>{newLine}</label></td>
                    <td><SelectLineItem store={store} row={i} selected={store.salesQuotation.salesQuotationLines[i].itemId} /></td>
                    <td><input className="form-control" type="text" name={i.toString()} value={store.salesQuotation.salesQuotationLines[i].code} onBlur={this.onFocusOutItem.bind(this, i, false) } onChange={this.onChangeCode.bind(this) } /></td>
                    <td><SelectLineMeasurement row={i} store={store} selected={store.salesQuotation.salesQuotationLines[i].measurementId} /></td>
                    <td><input className="form-control" type="text" name={i.toString()} value={store.salesQuotation.salesQuotationLines[i].quantity} onChange={this.onChangeQuantity.bind(this) } /></td>
                    <td><input className="form-control" type="text" name={i.toString()} value={store.salesQuotation.salesQuotationLines[i].amount} onChange={this.onChangeAmount.bind(this) } /></td>
                    <td><input className="form-control" type="text" name={i.toString()} value={store.salesQuotation.salesQuotationLines[i].discount} onChange={this.onChangeDiscount.bind(this) } /></td>
                    <td>{store.getLineTotal(i) }</td>
                    <td>
                        <button type="button" className="btn btn-box-tool" onClick={this.onClickRemoveLineItem.bind(this, i) }>
                            <i className="fa fa-fw fa-times"></i>
                        </button>
                    </td>
                </tr>
            );
           //autorun(() =>  this.lineNo = newLine);
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
                                <td>Item</td>
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

@observer
class SalesQuotationTotals extends React.Component<any, {}>{
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
class BookButton extends React.Component<any, {}>{
    bookOnClick(e) {
        store.bookQuotation();
    }

    render() {
        return (

            <input type="button" value="Book" onClick={ this.bookOnClick.bind(this) }
                className={store.salesQuotation.statusId == 0 && !store.editMode
                    ? "btn btn-sm btn-primary btn-flat btn-danger pull-right"
                    : "btn btn-sm btn-primary btn-flat btn-danger pull-right inactiveLink"} />
        );
    }
}

@observer
class EditButton extends React.Component<any, {}> {
    onClickEditButton() {
        // Remove " disabledControl" from current className
        var nodes = document.getElementById("divSalesQuotationForm").getElementsByTagName('*');
        for (var i = 0; i < nodes.length; i++) {
            var subStringLength = nodes[i].className.length - " disabledControl".length;
            nodes[i].className = nodes[i].className.substring(0, subStringLength);
        }

        store.changedEditMode(true);
    }
    render() {
        return (
            <a href="#" id="linkEdit" onClick={this.onClickEditButton} 
                className={store.salesQuotation.statusId == 0 && !store.editMode
                    ? "btn"
                    : "btn inactiveLink"}>
                <i className="fa fa-edit"></i>
                Edit
            </a>
        );
    }

}

export default class SalesQuotation extends React.Component<any, {}> {
    render() {
        return (
            <div>
                <div id="divActionsTop">
                    <EditButton/>
                </div>
                <div id="divSalesQuotationForm">
                    <ValidationErrors />
                    <SalesQuotationHeader />
                    <SalesQuotationLines />
                    <SalesQuotationTotals />
                </div>
                <div>
                    <SaveQuotationButton />
                    <CancelQuotationButton />
                    <BookButton />
                </div>
            </div>
        );
    }
}


ReactDOM.render(<SalesQuotation />, document.getElementById("divSalesQuotation"));


 