import * as React from "react";
import * as ReactDOM from "react-dom";
import {observer} from "mobx-react";
import Config = require("Config");

import SelectCustomer from "../Shared/Components/SelectCustomer";
import SelectPaymentTerm from "../Shared/Components/SelectPaymentTerm";
import SelectItem from "../Shared/Components/SelectItem";
import SelectMeasurement from "../Shared/Components/SelectMeasurement";

import SalesStore from "../Shared/Stores/Sales/SalesStore";

let store = new SalesStore();

class SaveOrderButton extends React.Component<any, {}>{
    render() {
        return (
            <input type="button" value="Save" />
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
    render() {        
        return (
            <div>
                <div>
                    <label>Customer: </label>
                    <SelectCustomer store={store} />{ store.salesOrder.customerId }
                </div>
                <div>
                    <label>Order Date: </label>
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
        store.addLineItem(1, 1, 1, 0, 0);
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
                    <td><SelectItem store={store} row={i} selected={store.salesOrder.salesOrderLines[i].itemId} /></td>
                    <td>{store.salesOrder.salesOrderLines[i].itemId}</td>
                    <td><SelectMeasurement row={i} store={store} selected={store.salesOrder.salesOrderLines[i].measurementId} />{store.salesOrder.salesOrderLines[i].measurementId}</td>
                    <td><input type="text" name={i} value={store.salesOrder.salesOrderLines[i].quantity} onChange={this.onChangeQuantity.bind(this)} /></td>
                    <td><input type="text" name={i} value={store.salesOrder.salesOrderLines[i].amount} onChange={this.onChangeAmount.bind(this) } /></td>
                    <td><input type="text" name={i} value={store.salesOrder.salesOrderLines[i].discount} onChange={this.onChangeDiscount.bind(this) } /></td>
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
                            <td></td>
                        </tr>
                    </thead>
                    <tbody>
                        {lineItems}
                        <tr>
                            <td><SelectItem /></td>
                            <td>Item Name</td>
                            <td><SelectMeasurement /></td>
                            <td><input type="text" /></td>
                            <td><input type="text" /></td>
                            <td><input type="text" /></td>
                            <td><input type="button" value="Add" onClick={this.addLineItem} /></td>
                        </tr>
                    </tbody>
                    <tfoot>
                        <tr>
                            <td colSpan="7">Coun: {store.salesOrder.salesOrderLines.length}</td>
                        </tr>
                    </tfoot>
                </table>
            </div>
        );
    }
}

@observer
class SalesOrderTotal extends React.Component<any, {}>{
    render() {
        return (
            <div>
                <label>Running Total: </label>
                <label>Tax Total: </label>
                <label>Grand Total: </label> {store.grandTotal()}
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
                    <SalesOrderTotal />
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