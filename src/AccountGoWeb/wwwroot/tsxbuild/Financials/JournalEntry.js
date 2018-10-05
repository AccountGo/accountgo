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
const SelectVoucherType_1 = require("../Shared/Components/SelectVoucherType");
const SelectAccount_1 = require("../Shared/Components/SelectAccount");
const SelectDebitCredit_1 = require("../Shared/Components/SelectDebitCredit");
const JournalEntryStore_1 = require("../Shared/Stores/Financials/JournalEntryStore");
const JournalEntryUIStore_1 = require("../Shared/Stores/Financials/JournalEntryUIStore");
let store = new JournalEntryStore_1.default();
let uiStore = new JournalEntryUIStore_1.default(store);
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
        var nodes = document.getElementById("divJournalEntryForm").getElementsByTagName('*');
        for (var i = 0; i < nodes.length; i++) {
            var subStringLength = nodes[i].className.length - " disabledControl".length;
            nodes[i].className = nodes[i].className.substring(0, subStringLength);
        }
        store.changedEditMode(true);
    }
    render() {
        return (React.createElement("a", { href: "#", id: "linkEdit", onClick: this.onClickEditButton, className: !store.journalEntry.posted && !store.editMode
                ? "btn"
                : "btn inactiveLink" },
            React.createElement("i", { className: "fa fa-edit" }),
            "Edit"));
    }
};
EditButton = __decorate([
    mobx_react_1.observer
], EditButton);
let SaveJournalEntryButton = class SaveJournalEntryButton extends React.Component {
    onClickSaveNewJournalEntry(e) {
        store.saveNewJournalEntry();
    }
    render() {
        return (React.createElement("input", { type: "button", value: "Save", onClick: this.onClickSaveNewJournalEntry.bind(this), className: !store.journalEntry.posted && store.editMode
                ? "btn btn-sm btn-primary btn-flat pull-left"
                : "btn btn-sm btn-primary btn-flat pull-left inactiveLink" }));
    }
};
SaveJournalEntryButton = __decorate([
    mobx_react_1.observer
], SaveJournalEntryButton);
class CancelJournalEntryButton extends React.Component {
    cancelOnClick() {
        let baseUrl = location.protocol
            + "//" + location.hostname
            + (location.port && ":" + location.port)
            + "/";
        window.location.href = baseUrl + 'financials/journalentries';
    }
    render() {
        return (React.createElement("input", { type: "button", onClick: this.cancelOnClick.bind(this), id: "btnCancel", className: "btn btn-sm btn-default btn-flat pull-left", value: "Cancel" }));
    }
}
let PostJournalEntryButton = class PostJournalEntryButton extends React.Component {
    postOnClick(e) {
        store.postJournal();
    }
    render() {
        return (React.createElement("input", { type: "button", value: "Post", onClick: this.postOnClick.bind(this), className: !store.journalEntry.posted && store.journalEntry.readyForPosting && !store.editMode
                ? "btn btn-sm btn-primary btn-flat btn-danger pull-right"
                : "btn btn-sm btn-primary btn-flat btn-danger pull-right inactiveLink" }));
    }
};
PostJournalEntryButton = __decorate([
    mobx_react_1.observer
], PostJournalEntryButton);
let JournalEntryHeader = class JournalEntryHeader extends React.Component {
    onChangeJournalDate(e) {
        store.changedJournalDate(e.target.value);
    }
    onChangeReferenceNo(e) {
        store.changedReferenceNo(e.target.value);
    }
    onChangeMemo(e) {
        store.changedMemo(e.target.value);
    }
    render() {
        return (React.createElement("div", { className: "box" },
            React.createElement("div", { className: "box-header with-border" },
                React.createElement("h3", { className: "box-title" }, "General"),
                React.createElement("div", { className: "box-tools pull-right" },
                    React.createElement("button", { type: "button", className: "btn btn-box-tool", "data-widget": "collapse", "data-toggle": "tooltip", title: "Collapse" },
                        React.createElement("i", { className: "fa fa-minus" })))),
            React.createElement("div", { className: "box-body" },
                React.createElement("div", { className: "col-sm-6" },
                    React.createElement("div", { className: "row" },
                        React.createElement("div", { className: "col-sm-3" }, "Date"),
                        React.createElement("div", { className: "col-sm-9" },
                            React.createElement("input", { type: "date", className: "form-control", id: "newJournalDate", onChange: this.onChangeJournalDate.bind(this), value: store.journalEntry.journalDate !== undefined ? store.journalEntry.journalDate.substring(0, 10) : new Date(Date.now()).toISOString().substring(0, 10) }))),
                    React.createElement("div", { className: "row" },
                        React.createElement("div", { className: "col-sm-3" }, "Voucher"),
                        React.createElement("div", { className: "col-sm-9" },
                            React.createElement(SelectVoucherType_1.default, { store: store, controlId: "optNewVoucherType", selected: store.journalEntry.voucherType }))),
                    React.createElement("div", { className: "row" },
                        React.createElement("div", { className: "col-sm-3" }, "Reference no"),
                        React.createElement("div", { className: "col-sm-9" },
                            React.createElement("input", { type: "text", className: "form-control", value: store.journalEntry.referenceNo || '', onChange: this.onChangeReferenceNo.bind(this) }))),
                    React.createElement("div", { className: "row" },
                        React.createElement("div", { className: "col-sm-3" }, "Memo"),
                        React.createElement("div", { className: "col-sm-9" },
                            React.createElement("input", { type: "text", className: "form-control", value: store.journalEntry.memo || '', onChange: this.onChangeMemo.bind(this) })))),
                React.createElement("div", { className: "col-sm-6" },
                    React.createElement("div", { className: "row" },
                        React.createElement("div", { className: "col-sm-2" }, "Posted"),
                        React.createElement("div", { className: "col-sm-10" },
                            React.createElement("input", { type: "checkbox", readOnly: true, checked: store.journalEntry.posted === true ? true : false })))))));
    }
};
JournalEntryHeader = __decorate([
    mobx_react_1.observer
], JournalEntryHeader);
let JournalEntryLines = class JournalEntryLines extends React.Component {
    onChangeAmount(e) {
        store.updateLineItem(e.target.name, "amount", e.target.value);
    }
    onChangeMemo(e) {
        store.updateLineItem(e.target.name, "memo", e.target.value);
    }
    onClickRemoveLineItem(i, e) {
        store.removeLineItem(i);
    }
    addLineItem() {
        var accountId, drcr, amount, memo;
        accountId = document.getElementById("optNewAccountId").value;
        ;
        drcr = document.getElementById("optNewDebitCredit").value;
        amount = document.getElementById("txtNewAmount").value;
        memo = document.getElementById("txtNewMemo").value;
        store.addLineItem(0, accountId, drcr, amount, memo);
        document.getElementById("txtNewAmount").value = "0";
        document.getElementById("txtNewMemo").value = "";
    }
    render() {
        var lineItems = [];
        for (var i = 0; i < store.journalEntry.journalEntryLines.length; i++) {
            lineItems.push(React.createElement("tr", { key: i },
                React.createElement("td", null,
                    React.createElement(SelectAccount_1.default, { store: store, row: i, selected: store.journalEntry.journalEntryLines[i].accountId })),
                React.createElement("td", null,
                    React.createElement(SelectDebitCredit_1.default, { store: store, row: i, selected: store.journalEntry.journalEntryLines[i].drcr })),
                React.createElement("td", null,
                    React.createElement("input", { type: "text", className: "form-control", name: i.toString(), onChange: this.onChangeAmount.bind(this), value: store.journalEntry.journalEntryLines[i].amount })),
                React.createElement("td", null,
                    React.createElement("input", { type: "text", className: "form-control", name: i.toString(), onChange: this.onChangeMemo.bind(this), value: store.journalEntry.journalEntryLines[i].memo || '' })),
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
                React.createElement("table", null,
                    React.createElement("thead", null,
                        React.createElement("tr", null,
                            React.createElement("td", null, "Account"),
                            React.createElement("td", null, "DrCr"),
                            React.createElement("td", null, "Amount"),
                            React.createElement("td", null, "Memo"),
                            React.createElement("td", null))),
                    React.createElement("tbody", null,
                        lineItems,
                        React.createElement("tr", null,
                            React.createElement("td", null,
                                React.createElement(SelectAccount_1.default, { store: store, controlId: "optNewAccountId" })),
                            React.createElement("td", null,
                                React.createElement(SelectDebitCredit_1.default, { store: store, controlId: "optNewDebitCredit" })),
                            React.createElement("td", null,
                                React.createElement("input", { type: "text", className: "form-control", id: "txtNewAmount" })),
                            React.createElement("td", null,
                                React.createElement("input", { type: "text", className: "form-control", id: "txtNewMemo" })),
                            React.createElement("td", null,
                                React.createElement("button", { type: "button", className: "btn btn-box-tool", onClick: this.addLineItem },
                                    React.createElement("i", { className: "fa fa-fw fa-check" })))))))));
    }
};
JournalEntryLines = __decorate([
    mobx_react_1.observer
], JournalEntryLines);
let JournalEntry = class JournalEntry extends React.Component {
    render() {
        return (React.createElement("div", null,
            React.createElement("div", { id: "divActionsTop" },
                React.createElement(EditButton, null)),
            React.createElement("div", { id: "divJournalEntryForm" },
                React.createElement(ValidationErrors, null),
                React.createElement(JournalEntryHeader, null),
                React.createElement(JournalEntryLines, null)),
            React.createElement("div", { id: "divActionsBottom" },
                React.createElement(SaveJournalEntryButton, null),
                React.createElement(CancelJournalEntryButton, null),
                React.createElement(PostJournalEntryButton, null))));
    }
};
JournalEntry = __decorate([
    mobx_react_1.observer
], JournalEntry);
exports.default = JournalEntry;
ReactDOM.render(React.createElement(JournalEntry, null), document.getElementById("divJournalEntry"));
//# sourceMappingURL=JournalEntry.js.map