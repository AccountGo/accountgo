import * as React from "react";

export default class SelectLineItem extends React.Component<any, {}>{
    onChangeItem(e) {
        this.props.store.updateLineItem(this.props.row, "itemId", e.target.value);
    }

    render() {
        var options = [];
        // TODO: replace with real values;
        options.push(<option key="1" value="1"> Item #1 </option>);
        options.push(<option key="2" value="2"> Item #2 </option>);
        options.push(<option key="3" value="3"> Item #3 </option>);
        return (
            <select defaultValue={this.props.selected} id={this.props.controlId} onChange={this.onChangeItem.bind(this) }>
                {options}
            </select>
        );
    }
}