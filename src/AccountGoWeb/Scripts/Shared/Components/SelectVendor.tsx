import * as React from "react";
import {observer} from "mobx-react";

@observer
export default class SelectVendor extends React.Component<any, {}>{
    onChangeVendor(e) {
        this.props.store.changedVendor(e.target.value);        
    }
    render() {
        var options = [];
        // TODO: replace with real values;
        //options.push(<option key="1" value="1"> Vendor #1 </option>);
        //options.push(<option key="2" value="2"> Vendor #2 </option>);
        //options.push(<option key="3" value="3"> Vendor #3 </option>);
        this.props.store.commonStore.vendors.map(function (vendor) {
            return (
                options.push(<option key={ vendor.id } value={ vendor.id } > { vendor.name } </option>)
            );
        });

        return (
            <select id="optVendor" onChange={this.onChangeVendor.bind(this)} >
                {options}
            </select>
        );
    }
}