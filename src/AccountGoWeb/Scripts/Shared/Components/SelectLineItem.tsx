import * as React from "react";
import {observer} from "mobx-react";

@observer
export default class SelectLineItem extends React.Component<any, {}>{
    onChangeItem(e) {
        this.props.store.updateLineItem(this.props.row, "itemId", e.target.value);
    }

    render() {
        var options = [];
        // TODO: replace with real values;
        //options.push(<option key="1" value="1"> Item #1 </option>);
        //options.push(<option key="2" value="2"> Item #2 </option>);
        //options.push(<option key="3" value="3"> Item #3 </option>);
        this.props.store.commonStore.items.map(function (item) {
            return (
                options.push(<option key={ item.id } value={ item.id } > { item.description } </option>)
            );
        });

        return (
            <select defaultValue={this.props.selected} id={this.props.controlId} onChange={this.onChangeItem.bind(this) }>
                {options}
            </select>
        );
    }
}