import * as React from "react";
import {observer} from "mobx-react";

@observer
export default class SelectVendor extends React.Component<any, {}>{
    onChangeVendor(e) {
        this.props.store.changedVendor(e.target.value);
        console.log(this.props.store.purchaseInvoice.vendorId); 
    }
    render() {
        var options = [];
        this.props.store.commonStore.vendors.map(function (vendor) {
            return (
            options.push(<option key={ vendor.id } value={ vendor.id }> { vendor.name }</option>)
            );
        });

        return (
            <select id="optVendor" value={this.props.selected} onChange={this.onChangeVendor.bind(this) } className="form-control select2">
                <option key={ -1 }></option>
                {options}
            </select>
        );
    }
}