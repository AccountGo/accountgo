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
let SelectQuotationStatus = class SelectQuotationStatus extends React.Component {
    onChangeQuoteStatus(e) {
        this.props.store.changedQuoteStatus(e.target.value);
    }
    render() {
        var options = [];
        this.props.store.commonStore.salesQuotationStatus.map(function (item) {
            return (options.push(React.createElement("option", { key: item.id, value: item.id },
                " ",
                item.description,
                " ")));
        });
        return (React.createElement("select", { value: this.props.selected, id: this.props.controlId, onChange: this.onChangeQuoteStatus.bind(this), className: "form-control select2" },
            React.createElement("option", { key: -1 }),
            options));
    }
};
SelectQuotationStatus = __decorate([
    mobx_react_1.observer
], SelectQuotationStatus);
exports.default = SelectQuotationStatus;
//# sourceMappingURL=SelectQuotationStatus.js.map