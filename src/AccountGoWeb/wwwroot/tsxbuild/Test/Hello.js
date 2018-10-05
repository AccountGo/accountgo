"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
const React = require("react");
const ReactDOM = require("react-dom");
class Hello extends React.Component {
    render() {
        return React.createElement("div", null,
            "Hello, ",
            this.props.name,
            ". If you can see this message means you have successfully configured reactjs+typescript+webpack+babel-loader.");
    }
}
ReactDOM.render(React.createElement(Hello, { name: "Marvin" }), document.getElementById("greeting"));
exports.default = Hello;
//# sourceMappingURL=Hello.js.map