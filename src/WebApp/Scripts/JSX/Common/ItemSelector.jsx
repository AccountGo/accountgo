var React = require('react');

var ItemSelector = React.createClass({
    handleChange: function (evt) {
        if (evt.target.id == null)
            this.props.onChange(evt.target.value);
    },
    render: function () {
        var options = this.props.items.map(function (option, index) {
            return <option key={index} value={option.value }>{option.text}</option>
        }, this);
        return (
    <span>

    </span>
        );
    }
});

module.exports = ItemSelector;