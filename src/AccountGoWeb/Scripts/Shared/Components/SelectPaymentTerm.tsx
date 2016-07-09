import * as React from "react";

export default class SelectPaymentTerm extends React.Component<any, {}>{
    render() {
        var options = [];
        var options = [];
        // TODO: replace with real values;
        options.push(<option key="1" value="1"> Payment Term #1 </option>);
        options.push(<option key="2" value="2"> Payment Term #2 </option>);
        options.push(<option key="3" value="3"> Payment Term #3 </option>);
        return (
            <select>
                {options}
            </select>
        );
    }
}