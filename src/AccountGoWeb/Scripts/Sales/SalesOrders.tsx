import * as React from "react";
import * as ReactDOM from "react-dom";
import * as axios from "axios";

export default class SalesOrders extends React.Component<{}, {}>{
    componentDidMount() {
        let component = this;
        axios.get('http://localhost:5000/api/sales/getsalesorders').then(function (data) {
            component.setState({ salesorders: data });
            console.log(component.state);
        });
    }
    render() {
        return (<h1>Sales Orders</h1>);
    }
}

ReactDOM.render(
    <SalesOrders />,
    document.getElementById("salesorders")
);