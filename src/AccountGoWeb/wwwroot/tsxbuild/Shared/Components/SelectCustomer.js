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
let SelectCustomer = class SelectCustomer extends React.Component {
    onChangeCustomer(e) {
        this.props.store.changedCustomer(e.target.value);
        for (var i = 0; i < this.props.store.commonStore.customers.length; i++) {
            if (this.props.store.commonStore.customers[i].id === parseInt(e.target.value)) {
                this.props.store.changedPaymentTerm(this.props.store.commonStore.customers[i].paymentTermId);
            }
            else {
                this.props.store.changedPaymentTerm("");
            }
        }
    }
    render() {
        var options = [];
        this.props.store.commonStore.customers.map(function (customer) {
            return (options.push(React.createElement("option", { key: customer.id, value: customer.id },
                " ",
                customer.name,
                " ")));
        });
        return (React.createElement("select", { id: "optCustomer", value: this.props.selected, onChange: this.onChangeCustomer.bind(this), className: "form-control select2" },
            React.createElement("option", { key: -1, value: "" }),
            options));
    }
};
SelectCustomer = __decorate([
    mobx_react_1.observer
], SelectCustomer);
exports.default = SelectCustomer;
//# sourceMappingURL=SelectCustomer.js.map