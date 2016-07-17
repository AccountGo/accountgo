import * as React from "react";
import {observer} from "mobx-react";

@observer
export default class SelectCustomer extends React.Component<any, {}>{
    onChangeCustomer(e) {
        this.props.store.changedCustomer(e.target.value);
    }
    render() {
        var options = [];
        this.props.store.commonStore.customers.map(function (customer) {
            return (
                options.push(<option key={ customer.id } value={ customer.id } > { customer.name } </option>)
            );
        });

        return (
            <select id="optCustomer" onChange={this.onChangeCustomer.bind(this) } >
                {options}
            </select>
        );
    }
}