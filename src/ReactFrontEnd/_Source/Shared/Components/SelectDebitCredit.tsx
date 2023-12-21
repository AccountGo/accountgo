import * as React from "react";
import {observer} from "mobx-react";

@observer
export default class SelectDebiCredit extends React.Component<any, {}>{
    onChangeDebitCredit(e) {
        if (this.props.row !== undefined)
            this.props.store.updateLineItem(this.props.row, "drcr", e.target.value);
    }
    render() {
        var options = [];
        options.push(<option key={1} value="1">Debit</option>);
        options.push(<option key={2} value="2">Credit</option>);

        return (
            <select id={this.props.controlId} value={this.props.selected} onChange={this.onChangeDebitCredit.bind(this) } className="form-control select2" >
                <option key={ -1 } value=""></option>
                {options}
            </select>
        );
    }
}