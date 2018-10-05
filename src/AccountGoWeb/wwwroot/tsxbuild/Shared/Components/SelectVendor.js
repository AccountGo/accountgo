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
let SelectVendor = class SelectVendor extends React.Component {
    onChangeVendor(e) {
        this.props.store.changedVendor(e.target.value);
        for (var i = 0; i < this.props.store.commonStore.vendors.length; i++) {
            if (this.props.store.commonStore.vendors[i].id === parseInt(e.target.value)) {
                this.props.store.changedPaymentTerm(this.props.store.commonStore.vendors[i].paymentTermId);
            }
            else {
                this.props.store.changedPaymentTerm("");
            }
        }
    }
    render() {
        var options = [];
        this.props.store.commonStore.vendors.map(function (vendor) {
            return (options.push(React.createElement("option", { key: vendor.id, value: vendor.id },
                " ",
                vendor.name)));
        });
        return (React.createElement("select", { id: "optVendor", value: this.props.selected, onChange: this.onChangeVendor.bind(this), className: "form-control select2" },
            React.createElement("option", { key: -1 }),
            options));
    }
};
SelectVendor = __decorate([
    mobx_react_1.observer
], SelectVendor);
exports.default = SelectVendor;
//# sourceMappingURL=SelectVendor.js.map