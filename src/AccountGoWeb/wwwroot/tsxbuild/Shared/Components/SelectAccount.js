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
let SelectVoucherType = class SelectVoucherType extends React.Component {
    onChangeAccount(e) {
        if (this.props.row !== undefined)
            this.props.store.updateLineItem(this.props.row, "accountId", e.target.value);
    }
    render() {
        var options = [];
        this.props.store.commonStore.accounts.map(function (account) {
            return (options.push(React.createElement("option", { key: account.id, value: account.id },
                " ",
                account.accountName,
                " ")));
        });
        return (React.createElement("select", { value: this.props.selected, id: this.props.controlId, onChange: this.onChangeAccount.bind(this), className: "form-control select2" },
            React.createElement("option", { key: -1, value: "" }),
            options));
    }
};
SelectVoucherType = __decorate([
    mobx_react_1.observer
], SelectVoucherType);
exports.default = SelectVoucherType;
//# sourceMappingURL=SelectAccount.js.map