import * as React from "react";
import {observer} from "mobx-react";

@observer
export default class SelectVoucherType extends React.Component<any, {}>{
    onChangeVoucherType(e) {
        this.props.store.changedVoucherType(e.target.value);
    }

    render() {
        var options = [];
        options.push(<option key="1" value="1"> Opening Balances</option>);
        options.push(<option key="2" value="2"> Closing Entries </option>);
        options.push(<option key="3" value="3"> Adjustment Entries </option>);
        options.push(<option key="4" value="4"> Correction Entries </option>);
        options.push(<option key="5" value="5"> Transfer Entries </option>);

        return (
            <select id={this.props.controlId} value={this.props.selected} onChange={this.onChangeVoucherType.bind(this) } className="form-control select2">
                <option key={ -1 } value=""></option>
                {options}
            </select>
        );
    }
}