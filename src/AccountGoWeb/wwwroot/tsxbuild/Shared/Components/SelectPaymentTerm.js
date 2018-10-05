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
let SelectPaymentTerm = class SelectPaymentTerm extends React.Component {
    onChangePaymentTerm(e) {
        this.props.store.changedPaymentTerm(e.target.value);
    }
    render() {
        var options = [];
        this.props.store.commonStore.paymentTerms.map(function (term) {
            return (options.push(React.createElement("option", { key: term.id, value: term.id },
                " ",
                term.description,
                " ")));
        });
        return (React.createElement("select", { id: "optPaymentTerm", value: this.props.selected, onChange: this.onChangePaymentTerm.bind(this), className: "form-control select2" },
            React.createElement("option", { key: -1, value: "" }),
            options));
    }
};
SelectPaymentTerm = __decorate([
    mobx_react_1.observer
], SelectPaymentTerm);
exports.default = SelectPaymentTerm;
//# sourceMappingURL=SelectPaymentTerm.js.map