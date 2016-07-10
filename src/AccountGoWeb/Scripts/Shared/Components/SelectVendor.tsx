import * as React from "react";

export default class SelectVendor extends React.Component<any, {}>{
    onChangeVendor(e) {
        this.props.store.changedVendor(e.target.value);        
    }
    render() {
        var options = [];
        // TODO: replace with real values;
        options.push(<option key="1" value="1"> Vendor #1 </option>);
        options.push(<option key="2" value="2"> Vendor #2 </option>);
        options.push(<option key="3" value="3"> Vendor #3 </option>);

        return (
            <select id="optVendor" onChange={this.onChangeVendor.bind(this)} >
                {options}
            </select>
        );
    }
}