import * as React from "react";
import * as ReactDOM from "react-dom";
import {observer} from "mobx-react";
import * as accounting from "accounting";

import SelectVendor from "../Shared/Components/SelectVendor";
import SelectPaymentTerm from "../Shared/Components/SelectPaymentTerm";
import SelectLineItem from "../Shared/Components/SelectLineItem";
import SelectLineMeasurement from "../Shared/Components/SelectLineMeasurement";

import PurchaseOrderStore from "../Shared/Stores/Purchasing/PurchaseOrderStore";
import PurchaseOrderLine from "../Shared/Stores/Purchasing/PurchaseOrderLine";

const purchId = window.location.search.split("?purchId=")[1];

const store = new PurchaseOrderStore(purchId);

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

class PurchaseOrderHeader extends React.Component {
    onChangeOrderDate(e: React.ChangeEvent<HTMLInputElement>) {
        store.changedOrderDate(e.target.value);
    }

    onChangeReferenceNo(e: React.ChangeEvent<HTMLInputElement>) {
        store.changedReferenceNo(e.target.value);
    }

    render() {        
        return (
            <div className="card">
                <div className="card-header">
        <a data-toggle="collapse" href="#vendor-info" aria-expanded="true" aria-controls="vendor-info"><i className="fa fa-align-justify"></i></a> Vendor Information
        </div>
                <div className="card-body collapse show row" id="vendor-info">
                    <div className="col-sm-6">
                        <div className="row">
                            <div className="col-sm-2">Vendor</div>
                            <div className="col-sm-10"><SelectVendor store={store} selected={store.purchaseOrder.vendorId} /></div>
                        </div>
                        <div className="row">
                            <div className="col-sm-2">Payment Term</div>
                            <div className="col-sm-10"><SelectPaymentTerm store={store} selected={store.purchaseOrder.paymentTermId} /></div>
                        </div>
                    </div>
                    <div className="col-sm-6">
                        <div className="row">
                            <div className="col-sm-2">Date</div>
                            <div className="col-sm-10">
                                <input type="date" className="form-control pull-right" onChange={this.onChangeOrderDate.bind(this) } step={7}
                                    value={store.purchaseOrder.orderDate !== undefined ? store.purchaseOrder.orderDate.toString().substring(0, 10) : new Date(Date.now()).toISOString().substring(0, 10) } /></div>
                        </div>                        
                        <div className="row">
                            <div className="col-sm-2">Reference no.</div>
                            <div className="col-sm-10"><input type="text" className="form-control"  value={store.purchaseOrder.referenceNo || ''} onChange={this.onChangeReferenceNo.bind(this) }  /></div>
                        </div>
                        <div className="row">
                            <div className="col-sm-2">Status</div>
                            <div className="col-sm-10"><label>{store.purchaseOrderStatus}</label></div>

                        </div>
                    </div>
                </div>
            </div>
        );
    }
}
const ObservedPurchaseOrderHeader = observer(PurchaseOrderHeader);

class PurchaseOrderLines extends React.Component {
    addLineItem() {

        if (store.validationLine()) {
            const itemId: string = (document.getElementById("optNewItemId") as HTMLInputElement).value;
            const measurementId: string = (document.getElementById("optNewMeasurementId") as HTMLInputElement).value;
            const quantity: string = (document.getElementById("txtNewQuantity") as HTMLInputElement).value;
            const amount: string = (document.getElementById("txtNewAmount") as HTMLInputElement).value;
            const discount: string = (document.getElementById("txtNewDiscount") as HTMLInputElement).value;
            const code: string = (document.getElementById("txtNewCode") as HTMLInputElement).value;

            store.addLineItem(0, Number(itemId), Number(measurementId), Number(quantity), Number(amount), Number(discount), code);

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

    onChangeCode(e: any) {
        store.updateLineItem(e.target.name, "code", e.target.value);
    }

    onFocusOutItem(e: any, isNew: boolean, i: any) {
        let isExisting = false;
        for (let x = 0; x < store.commonStore.items.length; x++) {
            const lineItem = store.commonStore.items[x] as PurchaseOrderLine;
            if (lineItem.code == i.target.value) {
                isExisting = true;
                if (isNew) {
                    (document.getElementById("optNewItemId") as HTMLInputElement).value = lineItem.id.toString();
                    (document.getElementById("optNewMeasurementId") as HTMLInputElement).value = lineItem.measurementId;
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
        const lineItems: JSX.Element[] = [];
        let lastIndex: number = 0;
        for (let i = 0; i < store.purchaseOrder.purchaseOrderLines.length; i++) {
            newLine = newLine + 10;
            lineItems.push(
                <tr key={i}>
                    <td><label>{newLine}</label></td>
                    <td><SelectLineItem store={store} row={i} selected={store.purchaseOrder.purchaseOrderLines[i].itemId} /></td>
                    <td><input className="form-control" type="text" name={i.toString()} value={store.purchaseOrder.purchaseOrderLines[i].code} onBlur={this.onFocusOutItem.bind(this, i, false) } onChange={this.onChangeCode.bind(this) } /></td>
                    <td><SelectLineMeasurement row={i} store={store} selected={store.purchaseOrder.purchaseOrderLines[i].measurementId} /></td>
                    <td><input type="text" className="form-control" name={i.toString()} value={store.purchaseOrder.purchaseOrderLines[i].quantity} onChange={this.onChangeQuantity.bind(this)} /></td>
                    <td><input type="text" className="form-control" name={i.toString()} value={store.purchaseOrder.purchaseOrderLines[i].amount} onChange={this.onChangeAmount.bind(this) } /></td>
                    <td><input type="text" className="form-control" name={i.toString()} value={store.purchaseOrder.purchaseOrderLines[i].discount} onChange={this.onChangeDiscount.bind(this) } /></td>
                    <td>{store.getLineTotal(i) }</td>
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
                <div className="card-body collapse show table-responsive">
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
                                <td><input className="form-control" type="text" id="txtNewCode" onBlur={this.onFocusOutItem.bind(this, lastIndex, true) } /></td>
                                <td><SelectLineMeasurement store={store} controlId="optNewMeasurementId" /></td>
                                <td><input className="form-control" type="text" id="txtNewQuantity" defaultValue="1" /></td>
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
const ObservedPurchaseOrderLines = observer(PurchaseOrderLines);

class PurchaseOrderTotals extends React.Component {
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
const ObservedPurchaseOrderTotals = observer(PurchaseOrderTotals);

class EditButton extends React.Component {
    onClickEditButton() {
        // Remove " disabledControl" from current className
        const nodes = document.getElementById("divPurchaseOrderForm")?.getElementsByTagName('*');
        for (let i = 0; i < nodes!.length; i++) {
            const subStringLength = nodes![i].className.length - " disabledControl".length;
            nodes![i].className = nodes![i].className.substring(0, subStringLength);
        }

        store.changedEditMode(true);
    }
    render() {
        return (
            <a href="#" id="linkEdit" onClick={this.onClickEditButton}
                className={store.purchaseOrder.statusId == 0 && !store.editMode
                    ? "btn"
                    : "btn inactiveLink"}>
                <i className="fa fa-edit"></i>
                Edit
            </a>
        );
    }

}
const ObservedEditButton = observer(EditButton);

class SavePurchaseOrderButton extends React.Component {
    saveNewPurchaseOrder() {
        store.savePurchaseOrder();
    }

    render() {
        return (
            <input type="button" className="btn btn-sm btn-primary btn-flat pull-left" value="Save" onClick={this.saveNewPurchaseOrder.bind(this) } />
        );
    }
}

class CancelPurchaseOrderButton extends React.Component {
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

class AddPurchaseOrder extends React.Component  {
    render() {
        return (
            <div>
                <div id="divActionsTop">
                    <ObservedEditButton/>
                </div>
                <div id="divPurchaseOrderForm">
                <ObservedValidationErrors />
                <ObservedPurchaseOrderHeader />
                <ObservedPurchaseOrderLines />
                <ObservedPurchaseOrderTotals />
                </div>
                <div>
                    <SavePurchaseOrderButton />
                    <CancelPurchaseOrderButton />
                </div>
            </div>
            );
    }
}
const ObservedAddPurchaseOrder = observer(AddPurchaseOrder);

export default ObservedAddPurchaseOrder;

ReactDOM.render(<ObservedAddPurchaseOrder />, document.getElementById("divPurchaseOrder"));