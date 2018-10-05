"use strict";
var __decorate = (this && this.__decorate) || function (decorators, target, key, desc) {
    var c = arguments.length, r = c < 3 ? target : desc === null ? desc = Object.getOwnPropertyDescriptor(target, key) : desc, d;
    if (typeof Reflect === "object" && typeof Reflect.decorate === "function") r = Reflect.decorate(decorators, target, key, desc);
    else for (var i = decorators.length - 1; i >= 0; i--) if (d = decorators[i]) r = (c < 3 ? d(r) : c > 3 ? d(target, key, r) : d(target, key)) || r;
    return c > 3 && r && Object.defineProperty(target, key, r), r;
};
Object.defineProperty(exports, "__esModule", { value: true });
const React = require("react");
const mobx_react_1 = require("mobx-react");
let SelectDebiCredit = class SelectDebiCredit extends React.Component {
    onChangeDebitCredit(e) {
        if (this.props.row !== undefined)
            this.props.store.updateLineItem(this.props.row, "drcr", e.target.value);
    }
    render() {
        var options = [];
        options.push(React.createElement("option", { key: 1, value: "1" }, "Debit"));
        options.push(React.createElement("option", { key: 2, value: "2" }, "Credit"));
        return (React.createElement("select", { id: this.props.controlId, value: this.props.selected, onChange: this.onChangeDebitCredit.bind(this), className: "form-control select2" },
            React.createElement("option", { key: -1, value: "" }),
            options));
    }
};
SelectDebiCredit = __decorate([
    mobx_react_1.observer
], SelectDebiCredit);
exports.default = SelectDebiCredit;
//# sourceMappingURL=SelectDebitCredit.js.map