import * as React from "react";
import * as ReactDOM from "react-dom";
import * as axios from "axios";

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
        axios.get('http://localhost:5000/api/sales/getsalesorders').then(function (data) {
            component.setState({
                salesorders: data
            });
        });
    },
    render: function () {
        let component = this;
        var rows = component.state.salesorders.data.map(function (so) {
            return (
                <tr>
                    <td>{so.CustomerName}</td>
                    <td>{so.OrderDate}</td>
                    <td>{so.TotalAmount}</td>
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