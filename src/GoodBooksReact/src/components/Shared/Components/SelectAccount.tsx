import * as React from "react";
import {observer} from "mobx-react";

class SelectVoucherType extends React.Component<any, {}>{
    onChangeAccount(e: any) {
        if (this.props.row !== undefined)
            this.props.store.updateLineItem(this.props.row, "accountId", e.target.value);
    }
    render() {
        var options: any = [];
        this.props.store.commonStore.accounts.map(function (account: any) {
            return (
                options.push(<option key={ account.id } value={ account.id } > { account.accountName } </option>)
            );
        });
        
        return (
            <select value = { this.props.selected } id= { this.props.controlId } onChange={this.onChangeAccount.bind(this) } className="form-control select2" >
                <option key={ -1 } value=""></option>
                {options}
            </select>
        );
    }
}
const ObservedSelectVoucherType = observer(SelectVoucherType);

export default ObservedSelectVoucherType;
