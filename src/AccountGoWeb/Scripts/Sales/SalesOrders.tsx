import * as React from "react";
import * as ReactDOM from "react-dom";
import * as axios from "axios";
import {observer} from "mobx-react";
import Config = require("Config");
import SalesStore from "../Shared/Stores/Sales/SalesStore";

let store = new SalesStore();

const SalesOrders = observer(React.createClass({
    componentDidMount: function () {
        axios.get(Config.apiUrl + '/api/sales/getsalesorders').then(function (result) {
            store.fillSalesOrders(result.data);
        });
    },
    render: function () {
        var rows = store.salesOrders.map(function (so : any, i) {
            return (
                <tr key={i}>
                     <td>{so.customerName}</td>
                     <td>{so.orderDate}</td>
                     <td>{so.totalAmount}</td>
                </tr>
            )
        });
        return (
            <div class="table-responsive">
                <table class="table table-bordered" id="tblSalesOrder">
                    <thead>
                        <tr class="success">
                            <td>Customer Name</td>
                            <td>Order Date</td>
                            <td>Amount</td>
                        </tr>
                    </thead>
                    <tbody>
                        {rows}
                    </tbody>
                </table>
            </div>
        )
    }
}));

ReactDOM.render(
    <SalesOrders />,
    document.getElementById("salesorders")
);