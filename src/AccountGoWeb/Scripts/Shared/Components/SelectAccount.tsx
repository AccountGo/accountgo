import * as React from "react";
import {observer} from "mobx-react";

@observer
export default class SelectVoucherType extends React.Component<any, {}>{
    onChangeAccount(e) {
        if (this.props.row !== undefined)
            this.props.store.updateLineItem(this.props.row, "accountId", e.target.value);
    }
    render() {
        var options = [];
        this.props.store.commonStore.accounts.map(function (account) {
            return (
                options.push(<option key={ account.id } value={ account.id } > { account.accountName } </option>)
            );
        });
        
        return (
            <select defaultValue = { this.props.selected } id= { this.props.controlId } onChange={this.onChangeAccount.bind(this) } >
                {options}
            </select>
        );
    }
}