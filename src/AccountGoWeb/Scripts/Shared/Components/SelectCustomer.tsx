import * as React from "react";
import {observer} from "mobx-react";

@observer
export default class SelectCustomer extends React.Component<any, {}>{
    onChangeCustomer(e) {
        this.props.store.changedCustomer(e.target.value);
    }
    render() {
        var options = [];
        // TODO: replace with real values;
        //options.push(<option key="1" value="1"> Customer #1 </option>);
        //options.push(<option key="2" value="2"> Customer #2 </option>);
        //options.push(<option key="3" value="3"> Customer #3 </option>);
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