import * as React from "react";

export default class SelectCustomer extends React.Component<any, {}>{
    onChangeCustomer(e) {
        this.props.store.changedCustomer(e.target.value);        
    }
    render() {
        var options = [];
        // TODO: replace with real values;
        options.push(<option key="1" value="1"> Customer #1 </option>);
        options.push(<option key="2" value="2"> Customer #2 </option>);
        options.push(<option key="3" value="3"> Customer #3 </option>);

        return (
            <select id="optCustomer" onChange={this.onChangeCustomer.bind(this)} >
                {options}
            </select>
        );
    }
}