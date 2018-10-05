"use strict";
var __decorate = (this && this.__decorate) || function (decorators, target, key, desc) {
    var c = arguments.length, r = c < 3 ? target : desc === null ? desc = Object.getOwnPropertyDescriptor(target, key) : desc, d;
    if (typeof Reflect === "object" && typeof Reflect.decorate === "function") r = Reflect.decorate(decorators, target, key, desc);
    else for (var i = decorators.length - 1; i >= 0; i--) if (d = decorators[i]) r = (c < 3 ? d(r) : c > 3 ? d(target, key, r) : d(target, key)) || r;
    return c > 3 && r && Object.defineProperty(target, key, r), r;
};
Object.defineProperty(exports, "__esModule", { value: true });
const React = require("react");
const ReactDOM = require("react-dom");
const mobx_react_1 = require("mobx-react");
const accounting = require("accounting");
const SelectCustomer_1 = require("../Shared/Components/SelectCustomer");
const SelectPaymentTerm_1 = require("../Shared/Components/SelectPaymentTerm");
const SelectLineItem_1 = require("../Shared/Components/SelectLineItem");
const SelectLineMeasurement_1 = require("../Shared/Components/SelectLineMeasurement");
const SalesInvoiceStore_1 = require("../Shared/Stores/Sales/SalesInvoiceStore");
let orderId = window.location.search.split("?orderId=")[1];
let invoiceId = window.location.search.split("?invoiceId=")[1];
let store = new SalesInvoiceStore_1.default(orderId, invoiceId);
let baseUrl = location.protocol
    + "//" + location.hostname
    + (location.port && ":" + location.port)
    + "/";
let ValidationErrors = class ValidationErrors extends React.Component {
    render() {
        if (store.validationErrors !== undefined && store.validationErrors.length > 0) {
            var errors = [];
            store.validationErrors.map(function (item, index) {
                errors.push(React.createElement("li", { key: index }, item));
            });
            return (React.createElement("div", null,
                React.createElement("ul", null, errors)));
        }
        return null;
    }
};
ValidationErrors = __decorate([
    mobx_react_1.observer
], ValidationErrors);
let EditButton = class EditButton extends React.Component {
    onClickEditButton() {
        var nodes = document.getElementById("divSalesInvoiceForm").getElementsByTagName('*');
        for (var i = 0; i < nodes.length; i++) {
            var subStringLength = nodes[i].className.length - " disabledControl".length;
            nodes[i].className = nodes[i].className.substring(0, subStringLength);
        }
        store.changedEditMode(true);
    }
    render() {
        return (React.createElement("a", { href: "#", id: "linkEdit", onClick: this.onClickEditButton, className: !store.salesInvoice.posted && !store.editMode
                ? "btn"
                : "btn inactiveLink" },
            React.createElement("i", { className: "fa fa-edit" }),
            "Edit"));
    }
};
EditButton = __decorate([
    mobx_react_1.observer
], EditButton);
let SaveInvoiceButton = class SaveInvoiceButton extends React.Component {
    saveNewSalesInvoice(e) {
        store.saveNewSalesInvoice();
    }
    onChangeReferenceNo(e) {
        store.changedReferenceNo(e.target.value);
    }
    render() {
        return (React.createElement("input", { type: "button", value: "Save", onClick: this.saveNewSalesInvoice.bind(this), className: !store.salesInvoice.posted && store.editMode
                ? "btn btn-sm btn-primary btn-flat pull-left"
                : "btn btn-sm btn-primary btn-flat pull-left inactiveLink" }));
    }
};
SaveInvoiceButton = __decorate([
    mobx_react_1.observer
], SaveInvoiceButton);
class CancelInvoiceButton extends React.Component {
    cancelOnClick() {
        let baseUrl = location.protocol
            + "//" + location.hostname
            + (location.port && ":" + location.port)
            + "/";
        window.location.href = baseUrl + 'sales/salesinvoices';
    }
    render() {
        return (React.createElement("button", { type: "button", className: "btn btn-sm btn-default btn-flat pull-left", onClick: this.cancelOnClick.bind(this) }, "Close"));
    }
}
let PrintButton = class PrintButton extends React.Component {
    printOnClick(e) {
        store.printInvoice();
    }
    render() {
        return (React.createElement("input", { type: "button", value: "Print", onClick: this.printOnClick.bind(this), className: store.salesInvoice.posted
                ? "btn btn-sm btn-primary btn-flat pull-right"
                : "btn btn-sm btn-primary btn-flat pull-right inactiveLink" }));
    }
};
PrintButton = __decorate([
    mobx_react_1.observer
], PrintButton);
let PostButton = class PostButton extends React.Component {
    postOnClick(e) {
        store.postInvoice();
    }
    render() {
        return (React.createElement("input", { type: "button", value: "Post", onClick: this.postOnClick.bind(this), className: !store.salesInvoice.posted && store.salesInvoice.readyForPosting && !store.editMode
                ? "btn btn-sm btn-primary btn-flat btn-danger pull-right"
                : "btn btn-sm btn-primary btn-flat btn-danger pull-right inactiveLink" }));
    }
};
PostButton = __decorate([
    mobx_react_1.observer
], PostButton);
let SalesInvoiceHeader = class SalesInvoiceHeader extends React.Component {
    onChangeInvoiceDate(e) {
        store.changedInvoiceDate(e.target.value);
    }
    onChangeReferenceNo(e) {
        store.changedReferenceNo(e.target.value);
    }
    render() {
        return (React.createElement("div", { className: "box" },
            React.createElement("div", { className: "box-header with-border" },
                React.createElement("h3", { className: "box-title" }, "Customer Information"),
                React.createElement("div", { className: "box-tools pull-right" },
                    React.createElement("button", { type: "button", className: "btn btn-box-tool", "data-widget": "collapse", "data-toggle": "tooltip", title: "Collapse" },
                        React.createElement("i", { className: "fa fa-minus" })))),
            React.createElement("div", { className: "box-body" },
                React.createElement("div", { className: "col-md-6" },
                    React.createElement("div", { className: "row" },
                        React.createElement("div", { className: "col-sm-2" }, "Customer"),
                        React.createElement("div", { className: "col-sm-10" },
                            React.createElement(SelectCustomer_1.default, { store: store, selected: store.salesInvoice.customerId }))),
                    React.createElement("div", { className: "row" },
                        React.createElement("div", { className: "col-sm-2" }, "Payment Term"),
                        React.createElement("div", { className: "col-sm-10" },
                            React.createElement(SelectPaymentTerm_1.default, { store: store, selected: store.salesInvoice.paymentTermId })))),
                React.createElement("div", { className: "col-md-6" },
                    React.createElement("div", { className: "row" },
                        React.createElement("div", { className: "col-sm-2" }, "Date"),
                        React.createElement("div", { className: "col-sm-10" },
                            React.createElement("input", { type: "date", className: "form-control pull-right", onChange: this.onChangeInvoiceDate.bind(this), value: store.salesInvoice.invoiceDate !== undefined ? store.salesInvoice.invoiceDate.substring(0, 10) : new Date(Date.now()).toISOString().substring(0, 10) }))),
                    React.createElement("div", { className: "row" },
                        React.createElement("div", { className: "col-sm-2" }, "Reference no."),
                        React.createElement("div", { className: "col-sm-10" },
                            React.createElement("input", { type: "text", className: "form-control", value: store.salesInvoice.referenceNo || '', onChange: this.onChangeReferenceNo.bind(this) })))))));
    }
};
SalesInvoiceHeader = __decorate([
    mobx_react_1.observer
], SalesInvoiceHeader);
let SalesInvoiceLines = class SalesInvoiceLines extends React.Component {
    addLineItem() {
        if (store.validationLine()) {
            var itemId, measurementId, quantity, amount, discount, code;
            itemId = document.getElementById("optNewItemId").value;
            measurementId = document.getElementById("optNewMeasurementId").value;
            quantity = document.getElementById("txtNewQuantity").value;
            amount = document.getElementById("txtNewAmount").value;
            discount = document.getElementById("txtNewDiscount").value;
            code = document.getElementById("txtNewCode").value;
            store.addLineItem(0, itemId, measurementId, quantity, amount, discount, code);
            document.getElementById("optNewItemId").value = "";
            document.getElementById("txtNewCode").value = "";
            document.getElementById("optNewMeasurementId").value = "";
            document.getElementById("txtNewQuantity").value = "1";
            document.getElementById("txtNewAmount").value = "";
            document.getElementById("txtNewDiscount").value = "";
        }
    }
    onClickRemoveLineItem(i, e) {
        store.removeLineItem(i);
    }
    onChangeQuantity(e) {
        store.updateLineItem(e.target.name, "quantity", e.target.value);
    }
    onChangeAmount(e) {
        store.updateLineItem(e.target.name, "amount", e.target.value);
    }
    onChangeDiscount(e) {
        store.updateLineItem(e.target.name, "discount", e.target.value);
    }
    onChangeCode(e) {
        store.updateLineItem(e.target.name, "code", e.target.value);
    }
    onFocusOutItem(e, isNew, i) {
        var isExisting = false;
        for (var x = 0; x < store.commonStore.items.length; x++) {
            if (store.commonStore.items[x].code == i.target.value) {
                isExisting = true;
                if (isNew) {
                    document.getElementById("optNewItemId").value = store.commonStore.items[x].id;
                    document.getElementById("optNewMeasurementId").value = store.commonStore.items[x].sellMeasurementId;
                    document.getElementById("txtNewAmount").value = store.commonStore.items[x].price;
                    document.getElementById("txtNewQuantity").value = "1";
                    document.getElementById("txtNewCode").style.borderColor = "";
                }
                else {
                    store.updateLineItem(e, "itemId", store.commonStore.items[x].id);
                    store.updateLineItem(e, "measurementId", store.commonStore.items[x].sellMeasurementId);
                    store.updateLineItem(e, "amount", store.commonStore.items[x].price);
                    store.updateLineItem(e, "quantity", 1);
                    i.target.style.borderColor = "";
                }
            }
        }
        if (!isExisting)
            if (isNew) {
                document.getElementById("optNewItemId").value = "";
                document.getElementById("optNewMeasurementId").value = "";
                document.getElementById("txtNewAmount").value = "";
                document.getElementById("txtNewQuantity").value = "";
                document.getElementById("txtNewCode").style.borderColor = '#FF0000';
            }
            else {
                i.target.style.borderColor = "red";
            }
    }
    render() {
        var newLine = 0;
        var lineItems = [];
        for (var i = 0; i < store.salesInvoice.salesInvoiceLines.length; i++) {
            newLine = newLine + 10;
            lineItems.push(React.createElement("tr", { key: i },
                React.createElement("td", null,
                    React.createElement("label", null, newLine)),
                React.createElement("td", null,
                    React.createElement(SelectLineItem_1.default, { store: store, row: i, selected: store.salesInvoice.salesInvoiceLines[i].itemId })),
                React.createElement("td", null,
                    React.createElement("input", { className: "form-control", type: "text", name: i.toString(), value: store.salesInvoice.salesInvoiceLines[i].code, onBlur: this.onFocusOutItem.bind(this, i, false), onChange: this.onChangeCode.bind(this) })),
                React.createElement("td", null,
                    React.createElement(SelectLineMeasurement_1.default, { row: i, store: store, selected: store.salesInvoice.salesInvoiceLines[i].measurementId })),
                React.createElement("td", null,
                    React.createElement("input", { type: "text", className: "form-control", name: i.toString(), value: store.salesInvoice.salesInvoiceLines[i].quantity, onChange: this.onChangeQuantity.bind(this) })),
                React.createElement("td", null,
                    React.createElement("input", { type: "text", className: "form-control", name: i.toString(), value: store.salesInvoice.salesInvoiceLines[i].amount, onChange: this.onChangeAmount.bind(this) })),
                React.createElement("td", null,
                    React.createElement("input", { type: "text", className: "form-control", name: i.toString(), value: store.salesInvoice.salesInvoiceLines[i].discount, onChange: this.onChangeDiscount.bind(this) })),
                React.createElement("td", null, store.getLineTotal(i)),
                React.createElement("td", null,
                    React.createElement("button", { type: "button", className: "btn btn-box-tool", onClick: this.onClickRemoveLineItem.bind(this, i) },
                        React.createElement("i", { className: "fa fa-fw fa-times" })))));
        }
        return (React.createElement("div", { className: "box" },
            React.createElement("div", { className: "box-header with-border" },
                React.createElement("h3", { className: "box-title" }, "Line Items"),
                React.createElement("div", { className: "box-tools pull-right" },
                    React.createElement("button", { type: "button", className: "btn btn-box-tool", "data-widget": "collapse", "data-toggle": "tooltip", title: "Collapse" },
                        React.createElement("i", { className: "fa fa-minus" })))),
            React.createElement("div", { className: "box-body table-responsive" },
                React.createElement("table", { className: "table table-hover" },
                    React.createElement("thead", null,
                        React.createElement("tr", null,
                            React.createElement("td", null, "No"),
                            React.createElement("td", null, "Item Id"),
                            React.createElement("td", null, "Code"),
                            React.createElement("td", null, "Measurement"),
                            React.createElement("td", null, "Quantity"),
                            React.createElement("td", null, "Amount"),
                            React.createElement("td", null, "Discount"),
                            React.createElement("td", null, "Line Total"),
                            React.createElement("td", null))),
                    React.createElement("tbody", null,
                        lineItems,
                        React.createElement("tr", null,
                            React.createElement("td", null),
                            React.createElement("td", null,
                                React.createElement(SelectLineItem_1.default, { store: store, controlId: "optNewItemId" })),
                            React.createElement("td", null,
                                React.createElement("input", { className: "form-control", type: "text", id: "txtNewCode", onBlur: this.onFocusOutItem.bind(this, i, true) })),
                            React.createElement("td", null,
                                React.createElement(SelectLineMeasurement_1.default, { store: store, controlId: "optNewMeasurementId" })),
                            React.createElement("td", null,
                                React.createElement("input", { className: "form-control", type: "text", id: "txtNewQuantity" })),
                            React.createElement("td", null,
                                React.createElement("input", { className: "form-control", type: "text", id: "txtNewAmount" })),
                            React.createElement("td", null,
                                React.createElement("input", { className: "form-control", type: "text", id: "txtNewDiscount" })),
                            React.createElement("td", null),
                            React.createElement("td", null,
                                React.createElement("button", { type: "button", className: "btn btn-box-tool", onClick: this.addLineItem },
                                    React.createElement("i", { className: "fa fa-fw fa-check" })))))))));
    }
};
SalesInvoiceLines = __decorate([
    mobx_react_1.observer
], SalesInvoiceLines);
let SalesInvoiceTotals = class SalesInvoiceTotals extends React.Component {
    render() {
        return (React.createElement("div", { className: "box" },
            React.createElement("div", { className: "box-body" },
                React.createElement("div", { className: "row" },
                    React.createElement("div", { className: "col-md-2" },
                        React.createElement("label", null, "SubTotal: ")),
                    React.createElement("div", { className: "col-md-2" }, accounting.formatMoney(store.RTotal, { symbol: "", format: "%s%v" })),
                    React.createElement("div", { className: "col-md-2" },
                        React.createElement("label", null, "Tax: ")),
                    React.createElement("div", { className: "col-md-2" }, accounting.formatMoney(store.TTotal, { symbol: "", format: "%s%v" })),
                    React.createElement("div", { className: "col-md-2" },
                        React.createElement("label", null, "Total: ")),
                    React.createElement("div", { className: "col-md-2" }, accounting.formatMoney(store.GTotal, { symbol: "", format: "%s%v" }))))));
    }
};
SalesInvoiceTotals = __decorate([
    mobx_react_1.observer
], SalesInvoiceTotals);
let SalesInvoice = class SalesInvoice extends React.Component {
    render() {
        return (React.createElement("div", null,
            React.createElement("div", { id: "divActionsTop" },
                React.createElement(EditButton, null)),
            React.createElement("div", { id: "divSalesInvoiceForm" },
                React.createElement(ValidationErrors, null),
                React.createElement(SalesInvoiceHeader, null),
                React.createElement(SalesInvoiceLines, null),
                React.createElement(SalesInvoiceTotals, null)),
            React.createElement("div", { id: "divActionsBottom" },
                React.createElement(SaveInvoiceButton, null),
                React.createElement(CancelInvoiceButton, null),
                React.createElement(PrintButton, null),
                React.createElement(PostButton, null))));
    }
};
SalesInvoice = __decorate([
    mobx_react_1.observer
], SalesInvoice);
exports.default = SalesInvoice;
ReactDOM.render(React.createElement(SalesInvoice, null), document.getElementById("divSalesInvoice"));
//# sourceMappingURL=SalesInvoice.js.map