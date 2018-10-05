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
let SelectLineMeasurement = class SelectLineMeasurement extends React.Component {
    onChangeMeasurement(e) {
        if (this.props.row !== undefined)
            this.props.store.updateLineItem(this.props.row, "measurementId", e.target.value);
    }
    render() {
        var options = [];
        this.props.store.commonStore.measurements.map(function (measurement) {
            return (options.push(React.createElement("option", { key: measurement.id, value: measurement.id },
                " ",
                measurement.description,
                " ")));
        });
        return (React.createElement("select", { value: this.props.selected, id: this.props.controlId, onChange: this.onChangeMeasurement.bind(this), className: "form-control select2" },
            React.createElement("option", { key: -1 }),
            options));
    }
};
SelectLineMeasurement = __decorate([
    mobx_react_1.observer
], SelectLineMeasurement);
exports.default = SelectLineMeasurement;
//# sourceMappingURL=SelectLineMeasurement.js.map