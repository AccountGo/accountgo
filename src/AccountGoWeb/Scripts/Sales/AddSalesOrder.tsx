import * as React from "react";
import * as ReactDOM from "react-dom";
import {observer} from "mobx-react";
import * as d3 from "d3";
import Config = require("Config");

import SelectCustomer from "../Shared/Components/SelectCustomer";
import SelectPaymentTerm from "../Shared/Components/SelectPaymentTerm";
import SelectLineItem from "../Shared/Components/SelectLineItem";
import SelectLineMeasurement from "../Shared/Components/SelectLineMeasurement";

import SalesOrderStore from "../Shared/Stores/Sales/SalesOrderStore";

let store = new SalesOrderStore();

class SaveOrderButton extends React.Component<any, {}>{
    saveNewSalesOrder(e) {

    }

    render() {
        return (
            <input type="button" value="Save" onClick={this.saveNewSalesOrder.bind(this)} />
            );
    }
}

class CancelOrderButton extends React.Component<any, {}>{
    render() {
        return (
            <input type="button" value="Cancel" />
        );
    }
}

@observer
class SalesOrderHeader extends React.Component<any, {}>{
    onChangeOrderDate(e) {
        store.changedOrderDate(e.target.value);
    }
    render() {        
        return (
            <div>
                <div>
                    <label>Customer: </label>
                    <SelectCustomer store={store} />{ store.salesOrder.customerId }
                </div>
                <div>
                    <label>Order Date: </label>
                    <input type="date" onChange={this.onChangeOrderDate.bind(this) } defaultValue={store.salesOrder.orderDate} />
                </div>
                <div>
                    <label>Payment Term: </label>
                    <SelectPaymentTerm />
                </div>
                <div>
                    <label>Reference No: </label>
                    <input type="text" />
                </div>
            </div>
        );
    }
}

@observer
class SalesOrderLines extends React.Component<any, {}>{
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
        for (var i = 0; i < store.salesOrder.salesOrderLines.length; i++) {
            lineItems.push(
                <tr key={i}>
                    <td><SelectLineItem store={store} row={i} selected={store.salesOrder.salesOrderLines[i].itemId} /></td>
                    <td>{store.salesOrder.salesOrderLines[i].itemId}</td>
                    <td><SelectLineMeasurement row={i} store={store} selected={store.salesOrder.salesOrderLines[i].measurementId} />{store.salesOrder.salesOrderLines[i].measurementId}</td>
                    <td><input type="text" name={i} value={store.salesOrder.salesOrderLines[i].quantity} onChange={this.onChangeQuantity.bind(this)} /></td>
                    <td><input type="text" name={i} value={store.salesOrder.salesOrderLines[i].amount} onChange={this.onChangeAmount.bind(this) } /></td>
                    <td><input type="text" name={i} value={store.salesOrder.salesOrderLines[i].discount} onChange={this.onChangeDiscount.bind(this) } /></td>
                    <td>{store.lineTotal(i)}</td>
                    <td><input type="button" name={i} value="Remove" onClick={this.onClickRemoveLineItem.bind(this) } /></td>
                </tr>
            );
        }
        return (
            <div>
                <table>
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
                            <td><input type="text" id="txtNewQuantity" /></td>
                            <td><input type="text" id="txtNewAmount" /></td>
                            <td><input type="text" id="txtNewDiscount" /></td>
                            <td></td>
                            <td><input type="button" value="Add" onClick={this.addLineItem} /></td>
                        </tr>
                    </tbody>
                    <tfoot>
                        <tr>
                            <td colSpan="8">Count: {store.salesOrder.salesOrderLines.length}</td>
                        </tr>
                    </tfoot>
                </table>
            </div>
        );
    }
}

@observer
class SalesOrderTotals extends React.Component<any, {}>{
    render() {
        return (
            <div>
                <div><label>Running Total: </label></div>
                <div><label>Tax Total: </label></div>
                <div><label>Grand Total: </label> {store.grandTotal() }</div>
            </div>
        );
    }
}

export default class AddSalesOrder extends React.Component<any, {}> {
    render() {
        return (
            <div>
                <div>
                    <SalesOrderHeader />
                </div>
                <hr />
                <div>
                    <SalesOrderLines />
                </div>
                <hr />
                <div>
                    <SalesOrderTotals />
                </div>
                <hr />
                <div>
                    <SaveOrderButton />
                    <CancelOrderButton />
                </div>
            </div>
            );
    }
}

ReactDOM.render(<AddSalesOrder />, document.getElementById("divAddSalesOrder"));