import * as React from "react";
import * as ReactDOM from "react-dom";
import {observer} from "mobx-react";
import * as d3 from "d3";
import Config = require("Config");

import SelectVoucherType from "../Shared/Components/SelectVoucherType";
import SelectAccount from "../Shared/Components/SelectAccount";
import SelectDebitCredit from "../Shared/Components/SelectDebitCredit";

import JournalEntryStore from "../Shared/Stores/Financials/JournalEntryStore";

let journalEntryId = window.location.search.split("?id=")[1];

let store = new JournalEntryStore(journalEntryId);

class SaveJournalEntryButton extends React.Component<any, {}>{
    onClickSaveNewJournalEntry(e) {
        store.saveNewJournalEntry();
    }
    render() {
        return (
            <input type="button" value="Save" onClick={this.onClickSaveNewJournalEntry.bind(this)} />
        );
    }
}

class CancelJournalEntryButton extends React.Component<any, {}>{
    render() {
        return (
            <input type="button" value="Cancel" />
        );
    }
}

@observer
class JournalEntryHeader extends React.Component<any, {}>{
    onChangeJournalDate(e) {
        store.changedJournalDate(e.target.value);
    }

    render() {
        return (
            <div className="box">
                <div className="box-header with-border">
                    <h3 className="box-title">General</h3>
                    <div className="box-tools pull-right">
                        <button type="button" className="btn btn-box-tool" data-widget="collapse" data-toggle="tooltip" title="Collapse">
                            <i className="fa fa-minus"></i>
                        </button>
                    </div>
                </div>
                <div className="box-body">
                    <div className="col-sm-6">
                        <div className="row">
                            <div className="col-sm-3">Date</div>
                            <div className="col-sm-9"><input type="date" className="form-control" id="newJournalDate" onChange={this.onChangeJournalDate.bind(this) } value={store.journalEntry.date} /></div>
                        </div>
                        <div className="row">
                            <div className="col-sm-3">Voucher</div>
                            <div className="col-sm-9"><SelectVoucherType store={store} controlId="optNewVoucherType" selected={store.journalEntry.voucherType} /></div>
                        </div>
                        <div className="row">
                            <div className="col-sm-3">Reference no</div>
                            <div className="col-sm-9"><input type="text" className="form-control" value={store.journalEntry.referenceNo} /></div>
                        </div>
                        <div className="row">
                            <div className="col-sm-3">Memo</div>
                            <div className="col-sm-9"><input type="text" className="form-control" value={store.journalEntry.memo} /></div>
                        </div>
                    </div>
                </div>
            </div>
        );
    }
}

@observer
class JournalEntryLines extends React.Component<any, {}>{
    onChangeAmount(e) {
        store.updateLineItem(e.target.name, "amount", e.target.value);
    }
    onChangeMemo(e) {
        store.updateLineItem(e.target.name, "memo", e.target.value);
    }
    onClickRemoveLineItem(e) {
        store.removeLineItem(e.target.name);
    }
    addLineItem() {
        var accountId, drcr, amount, memo;
        accountId = (document.getElementById("optNewAccountId") as HTMLInputElement).value;;
        drcr = (document.getElementById("optNewDebitCredit") as HTMLInputElement).value;
        amount = (document.getElementById("txtNewAmount") as HTMLInputElement).value;
        memo = (document.getElementById("txtNewMemo") as HTMLInputElement).value;

        store.addLineItem(accountId, drcr, amount, memo);
        
        (document.getElementById("txtNewAmount") as HTMLInputElement).value = "0";
        (document.getElementById("txtNewMemo") as HTMLInputElement).value = "";
    }
    render() {
        var lineItems = [];
        for (var i = 0; i < store.journalEntry.journalEntryLines.length; i++) {
            lineItems.push(
                <tr key={i}>
                    <td><SelectAccount store={store} row={i} selected={store.journalEntry.journalEntryLines[i].accountId} /></td>
                    <td><SelectDebitCredit store={store} row={i} selected={store.journalEntry.journalEntryLines[i].drcr} /></td>
                    <td><input type="text" className="form-control" name={i} onChange={this.onChangeAmount.bind(this) } /></td>
                    <td><input type="text" className="form-control" name={i} onChange={this.onChangeMemo.bind(this) } /></td>
                    <td><input type="button" name={i} value="Remove" onClick={this.onClickRemoveLineItem.bind(this) } /></td>
                </tr>
            );
        }
        return (
            <div className="box">
                <div className="box-header with-border">
                    <h3 className="box-title">Line Items</h3>
                    <div className="box-tools pull-right">
                        <button type="button" className="btn btn-box-tool" data-widget="collapse" data-toggle="tooltip" title="Collapse">
                            <i className="fa fa-minus"></i>
                        </button>
                    </div>
                </div>
                <div className="box-body table-responsive">
                    <table>
                        <thead>
                            <tr>
                                <td>Account</td>
                                <td>DrCr</td>
                                <td>Amount</td>
                                <td>Memo</td>
                                <td></td>
                            </tr>
                        </thead>
                        <tbody>
                            {lineItems}
                            <tr>
                                <td><SelectAccount store={store} controlId="optNewAccountId" /></td>
                                <td><SelectDebitCredit store={store} controlId="optNewDebitCredit" /></td>
                                <td><input type="text" className="form-control" id="txtNewAmount" /></td>                            
                                <td><input type="text" className="form-control" id="txtNewMemo" /></td>                            
                                <td><input type="button" value="Add" onClick={this.addLineItem} /></td>
                            </tr>
                        </tbody>
                    </table>
                </div>
            </div>
        );
    }
}

export default class JournalEntry extends React.Component<any, {}> {
    render() {
        return (
            <div>
                <JournalEntryHeader />
                <JournalEntryLines />
                <div>
                    <SaveJournalEntryButton />
                    <CancelJournalEntryButton />
                </div>
            </div>
        );
    }
}

ReactDOM.render(<JournalEntry />, document.getElementById("divJournalEntry"));