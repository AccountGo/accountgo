var React = require('react');
var ReactDOM = require('react-dom');

var $ = require('jquery');

var CustomerSelector = require('../Common/CustomerSelector.jsx');
var ItemSelector = require('../Common/ItemSelector.jsx');

function copy(obj) {
    var newObj = {};
    for (var key in obj) {
        if (obj.hasOwnProperty(key)) {
            newObj[key] = obj[key];
        }
    }
    return newObj;
}

var SalesOrderItemsCell = React.createClass({
    handleChange: function (evt) {
        this.props.onChange(evt.target.value);
    },
    render: function () {
        return (
<input value={this.props.value} onChange={this.handleChange} />
        )
    }
});


var SalesOrderItemsRow = React.createClass({
    onRowDeleteEvent: function () {
        this.props.onRowDelete();
    },
    handleChange: function (prop, val) {
        this.props.onCellChange(prop, val);
    },
    render: function () {
        return (
            <tr>
                <td>
<ItemSelector selectedValue={this.props.lineItem.itemId} items={this.props.items} onChange={this.handleChange.bind(null, "itemId")} />
                </td>
                <td>
<SalesOrderItemsCell value={this.props.lineItem.quantity} onChange={this.handleChange.bind(null, "quantity")} />
                </td>
                <td>
                    <input type='button' id='btnDeleteLineItem' value='Delete' onClick={this.onRowDeleteEvent} />
                </td>
            </tr>
            );
    }
});

var SalesOrderItems = React.createClass({
    handleAddLineItem: function () {
        var e = document.getElementById("optItemId");
        var selectedItem = e.options[e.selectedIndex].value;

        var item = {
            itemId: selectedItem,
            quantity: document.getElementById('txtQuantity').value
        };
        this.props.lineItems.push(item);
        this.props.updatelineItems(this.props.lineItems);
    },
    render: function () {
        var items = this.props.lineItems.map(function (lineItem, index) {
            return (
            <SalesOrderItemsRow key={index} lineItem={lineItem } items={this.props.items} onRowDelete={this.props.onRowDelete.bind(null, index)} onCellChange={this.props.onCellChange.bind(null, index)} />
            );
        }, this);

        return (
            <table>
                <thead>
                <tr>
                    <td>Item</td>
                    <td>Quantity</td>
                    <td></td>
                </tr>
                </thead>
                <tbody>
                    {items}
                </tbody>
                <tfoot>
                    <tr>
                        <td>
<ItemSelector id='optItemId' items={this.props.items} />
                        </td>
                        <td>
<input type="text" id="txtQuantity" />
                        </td>
                        <td>
<input type='button' id='btnAdd' value='Save' onClick={this.handleAddLineItem} />
                        </td>
                    </tr>
                </tfoot>
            </table>
            );
    }
});

var SalesOrderHeader = React.createClass({
    render: function () {
        return (
                <div>
                    Order Date: {this.props.header.orderDate}
                    <br />
                    Customer :
<CustomerSelector selectedValue={this.props.header.customerId} customers={this.props.customers} id='optCustomer' />
                </div>
            );
    }
});

var SalesOrderForm = React.createClass({
    getInitialState: function () {
        return {
            salesOrder: {},
            lineItems: [],
            customers: [],
            items: []
        }
    },
    handleAddLineItem: function (newlineItems) {
        this.setState({ lineItems: newlineItems });
    },
    handleOnSubmit: function (e) {
        e.preventDefault();

        this.state.salesOrder.lineItems = this.state.lineItems;
        // set values before submitting
        this.state.salesOrder.customerId = document.getElementById('optCustomer').value;

        $.ajax({
            type: 'POST',
            url: 'sales/addsalesorder',
            data: this.state.salesOrder,
            success: this.handleSubmitSuccess,
            error: this.handleSubmitFailure,
            dataType: 'json'
        });
    },
    handleSubmitSuccess: function (data) {
        //if (data.status == 'ok') {
        //    var posts = this.state.posts || [];
        //    posts.push(data.object)

        //    this.setState({
        //        alertMessage: this.props.successMessage || 'Post submitted',
        //        alertClass: 'alert-box success radius',
        //        showAlert: true,
        //        posts: posts,
        //        showPostBox: false,
        //        showPostLink: true
        //    });
        //}
        //else {
        //    this.setState({
        //        alertMessage: data.message || 'There was an error submitting your post',
        //        alertClass: 'alert-box alert radius',
        //        showAlert: true
        //    });
        //}
    },
    handleSubmitFailure: function (xhr, ajaxOptions, thrownError) {
        //this.setState({
        //    alertMessage: thrownError || 'There was an error submitting your post',
        //    alertClass: 'alert-box alert radius',
        //    showAlert: true
        //});
    },
    handleRowDelete(index) {
        //"_" is sometimes used to represent an unused argument. Here, it's the current item in the array.
        this.setState({
            lineItems: this.state.lineItems.filter((_, i) => i !== index)
        });
    },
    handleLineItemCellChange: function (rowIdx, prop, val) {
        //console.log(rowIdx + ':' + prop + ':' + val);
        var row = copy(this.state.lineItems[rowIdx]);
        row[prop] = val;
        var rows = this.state.lineItems.slice();
        rows[rowIdx] = row;
        this.setState({ lineItems: rows });
    },
    componentDidMount: function () {
        $.get(this.props.modelUrl, function (data) {
            if (this.isMounted()) {
                this.setState({
                    salesOrder: data,
                    lineItems: data.lineItems,
                    customers: data.customers,
                    items: data.items
                });
            }
        }.bind(this));
    },
    render: function () {
        return (
            <form id='frmAddSalesOrder' onSubmit={this.handleOnSubmit} action='sales/addsalesorder'>
                <SalesOrderHeader header={this.state.salesOrder} customers={this.state.customers} />
                <SalesOrderItems lineItems={this.state.lineItems} items={this.state.items} updatelineItems={this.handleAddLineItem} onRowDelete={this.handleRowDelete} onCellChange={this.handleLineItemCellChange} />
                <SaveSalesOrderForm />
            </form>
        )
    },
});

var SaveSalesOrderForm = React.createClass({
    render: function () {
        return (
            <input type='submit' id='btnSave' value='Save Order' />
            );
    }
});

ReactDOM.render(
<SalesOrderForm modelUrl='sales/getaddsalesordermodel' />, document.getElementById('addsalesorderform'));