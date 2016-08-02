import * as React from "react";
import {observer} from "mobx-react";

@observer
export default class SelectLineItem extends React.Component<any, {}>{
    onChangeItem(e) {
        if (this.props.row !== undefined)
            this.props.store.updateLineItem(this.props.row, "itemId", e.target.value);
    }

    render() {
        var options = [];
        this.props.store.commonStore.items.map(function (item) {
            return (
                options.push(<option key={ item.id } value={ item.id } > { item.description } </option>)
            );
        });

        return (
            <select value={this.props.selected} id={this.props.controlId} onChange={this.onChangeItem.bind(this) } className="form-control select2">
                <option key={ -1 }></option>
                {options}
            </select>
        );
    }
}