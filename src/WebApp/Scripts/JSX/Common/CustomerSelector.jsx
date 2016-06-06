var React = require('react');

var CustomerSelector = React.createClass({
    handleChange: function (e) {
        console.log(e.target.value);
    },
    render: function () {
        var options = [];
        //for (var i = 0; i < customers.length; i++) {
        //    options.push(<option key={i} value={customers[i].value }>{customers[i].text}</option>);
        //}
        console.log(this.props.customers);
        var options = this.props.customers.map(function (option, index) {
            return (
                <option key={index} value={option.value}>{option.text}</option>
            )
        }, this);

        return (
    <span>
        <select defaultValue={this.props.selectedValue} onChange={this.handleChange} id={this.props.id}>
            {options}
        </select>
    </span>
        );
    }
});

module.exports = CustomerSelector;