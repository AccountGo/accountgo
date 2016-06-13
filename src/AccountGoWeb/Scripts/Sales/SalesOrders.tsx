import * as React from "react";
import * as ReactDOM from "react-dom";

export default  class SalesOrders extends React.Component<{}, {}>{
    render() {
        return (<h1>Sales Orders</h1>);
    }
}

ReactDOM.render(
    <SalesOrders />,
    document.getElementById("salesorders")
);