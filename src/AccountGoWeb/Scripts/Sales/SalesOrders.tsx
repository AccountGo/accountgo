import * as React from "react";
import * as ReactDOM from "react-dom";
import * as axios from "axios";
import Config = require("Config");

const SalesOrders = React.createClass({
    getInitialState: function () {
        return {
            salesorders: {
                data : []
            }
        }
    },
    componentDidMount: function () {
        let component = this;
        axios.get(Config.apiUrl + '/api/sales/getsalesorders').then(function (data) {
            component.setState({
                salesorders: data
            });
        });
    },
    render: function () {
        let component = this;
        var rows = component.state.salesorders.data.map(function (so, i) {
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
});

ReactDOM.render(
    <SalesOrders />,
    document.getElementById("salesorders")
);