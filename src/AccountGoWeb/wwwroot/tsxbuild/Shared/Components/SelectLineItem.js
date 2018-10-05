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
let SelectLineItem = class SelectLineItem extends React.Component {
    onChangeItem(e) {
        if (this.props.row !== undefined)
            this.props.store.updateLineItem(this.props.row, "itemId", e.target.value);
        if (e.target.value == "") {
            if (this.props.row === undefined) {
                document.getElementById("optNewMeasurementId").value = "";
                document.getElementById("txtNewAmount").value = "";
                document.getElementById("txtNewCode").value = "";
                document.getElementById("txtNewQuantity").value = "";
                return;
            }
        }
        for (var i = 0; i < this.props.store.commonStore.items.length; i++) {
            if (this.props.store.commonStore.items[i].id === parseInt(e.target.value)) {
                if (this.props.row !== undefined) {
                    this.props.store.updateLineItem(this.props.row, "measurementId", this.props.store.commonStore.items[i].sellMeasurementId);
                    this.props.store.updateLineItem(this.props.row, "amount", this.props.store.commonStore.items[i].price);
                    this.props.store.updateLineItem(this.props.row, "code", this.props.store.commonStore.items[i].code);
                    this.props.store.updateLineItem(this.props.row, "quantity", 1);
                }
                else {
                    document.getElementById("optNewMeasurementId").value = this.props.store.commonStore.items[i].sellMeasurementId;
                    document.getElementById("txtNewAmount").value = this.props.store.commonStore.items[i].price;
                    document.getElementById("txtNewCode").value = this.props.store.commonStore.items[i].code;
                    document.getElementById("txtNewQuantity").value = "1";
                }
            }
        }
    }
    render() {
        var options = [];
        this.props.store.commonStore.items.map(function (item) {
            return (options.push(React.createElement("option", { key: item.id, value: item.id },
                " ",
                item.description,
                " ")));
        });
        return (React.createElement("select", { value: this.props.selected, id: this.props.controlId, onChange: this.onChangeItem.bind(this), className: "form-control select2" },
            React.createElement("option", { key: -1 }),
            options));
    }
};
SelectLineItem = __decorate([
    mobx_react_1.observer
], SelectLineItem);
exports.default = SelectLineItem;
//# sourceMappingURL=SelectLineItem.js.map