var React = require('react');
var ReactDOM = require('react-dom');
var $ = require('jquery');

var SalesOrderRow = React.createClass({
    render: function () {
        return (
            <tr>
                <td>{this.props.salesorder.CustomerName}</td>
                <td>{this.props.salesorder.OrderDate}</td>
                <td>{this.props.salesorder.TotalAmount}</td>
            </tr>
            );
    }
});

var SalesOrdersTable = React.createClass({
    getInitialState: function () {
        return {
            salesorders: []
        }
    },
    componentDidMount: function () {
        $('#tblSalesOrder').DataTable({
            "paging": true,
            "lengthChange": false,
            "searching": false,
            "ordering": true,
            "info": true,
            "autoWidth": false
        });
        $.get(this.props.dataUrl, function (data) {
            if (this.isMounted()) {
                this.setState({
                    salesorders: data
                });
            }
        }.bind(this));
    },

    render: function () {
        var rows = this.state.salesorders.map(function (salesorder) {
            return (
            <SalesOrderRow key={salesorder.Id} salesorder={salesorder}></SalesOrderRow>)
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
<SalesOrdersTable dataUrl="http://localhost:44320/api/sales/getsalesorders" />,
document.getElementById('griddata')
);