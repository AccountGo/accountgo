import * as React from "react";
import * as ReactDOM from "react-dom";
import * as axios from "axios";
import {observer} from "mobx-react";
import Config = require("Config");
import SalesOrderStore from "../Shared/Stores/Sales/SalesOrderStore";

let store = new SalesOrderStore();

const SalesOrders = observer(React.createClass({
    componentDidMount: function () {
        axios.get(Config.apiUrl + '/api/sales/getsalesorders').then(function (result) {
            //store.fillSalesOrders(result.data);
        });
    },
    render: function () {
        var rows = [];
        //var rows = store.salesOrders.map(function (so : any, i) {
        //    return (
        //        <tr key={i}>
        //             <td>{so.customerName}</td>
        //             <td>{so.orderDate}</td>
        //             <td>{so.totalAmount}</td>
        //        </tr>
        //    )
        //});
        return (
            <div>
                <div>
                </div>
                <div>
                    <table>
                        <thead>
                            <tr>
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
            </div>
        )
    }
}));

ReactDOM.render(
    <SalesOrders />,
    document.getElementById("salesorders")
);