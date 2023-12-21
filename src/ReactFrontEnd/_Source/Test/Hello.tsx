import * as React from "react";
import * as ReactDOM from "react-dom";

interface HelloProps {
    name: string;
}

class Hello extends React.Component<HelloProps, {}> {
    render() {
        return <div>Hello, {this.props.name}. If you can see this message means you have successfully configured reactjs+typescript+webpack+babel-loader.</div>;
    }
}

ReactDOM.render(
    <Hello name="Marvin" />,
    document.getElementById("greeting")
);

export default Hello;