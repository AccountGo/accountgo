import * as React from "react";
import {observer} from "mobx-react";

@observer
export default class SelectCustomer extends React.Component<any, {}>{
    onChangeCustomer(e) {
        this.props.store.changedCustomer(e.target.value);
 
        for (var i = 0; i < this.props.store.commonStore.customers.length; i++) {
            if (this.props.store.commonStore.customers[i].id === parseInt(e.target.value)) {
                this.props.store.changedPaymentTerm(this.props.store.commonStore.customers[i].paymentTermId);
            }
            else
            {
                this.props.store.changedPaymentTerm("");
            }
        }
     
    }    
    render() {
        var options = [];
        this.props.store.commonStore.customers.map(function (customer) {
 
            return (
                options.push(<option  key={ customer.id } value={ customer.id }> { customer.name } </option>)
            );
        });
  
        return (
            <select id="optCustomer" value={this.props.selected} onChange={this.onChangeCustomer.bind(this) } className="form-control select2">
                <option key={ -1 } value=""></option>
                {options}
            </select>
        );
    }
}