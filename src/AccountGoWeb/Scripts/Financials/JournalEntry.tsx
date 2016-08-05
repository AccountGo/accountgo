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
            <input type="button" className="btn btn-sm btn-primary btn-flat pull-left" value="Save" onClick={this.onClickSaveNewJournalEntry.bind(this) } />
        );
    }
}

class CancelJournalEntryButton extends React.Component<any, {}>{
    cancelOnClick() {
        let baseUrl = location.protocol
            + "//" + location.hostname
            + (location.port && ":" + location.port)
            + "/";

        window.location.href = baseUrl + 'financials/journalentries';
    }

    render() {
        return (
            <input type="button" className="btn btn-sm btn-default btn-flat pull-left" value="Cancel" onClick={ this.cancelOnClick.bind(this) } />
        );
    }
}

class PostJournalEntryButton extends React.Component<any, {}>{
    postOnClick(e) {
    }

    render() {
        return (
            <input type="button" className="btn btn-sm btn-danger btn-flat pull-right" value="Post" onClick={ this.postOnClick.bind(this) } />
        );
    }
}

@observer
class JournalEntryHeader extends React.Component<any, {}>{
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
                            <div className="col-sm-9"><input type="date" className="form-control" id="newJournalDate" onChange={this.onChangeJournalDate.bind(this) }
                                value={store.journalEntry.date !== undefined ? store.journalEntry.date.substring(0, 10) : new Date(Date.now()).toISOString().substring(0, 10) } /></div>
                        </div>
                        <div className="row">
                            <div className="col-sm-3">Voucher</div>
                            <div className="col-sm-9"><SelectVoucherType store={store} controlId="optNewVoucherType" selected={store.journalEntry.voucherType} /></div>
                        </div>
                        <div className="row">
                            <div className="col-sm-3">Reference no</div>
                            <div className="col-sm-9"><input type="text" className="form-control" value={store.journalEntry.referenceNo} onChange={this.onChangeReferenceNo.bind(this)} /></div>
                        </div>
                        <div className="row">
                            <div className="col-sm-3">Memo</div>
                            <div className="col-sm-9"><input type="text" className="form-control" value={store.journalEntry.memo} onChange={this.onChangeMemo.bind(this) } /></div>
                        </div>
                    </div>
                    <div className="col-sm-6">
                        <div className="row">
                            <div className="col-sm-2">Posted</div>
                            <div className="col-sm-10"><input type="checkbox" readOnly checked={store.journalEntry.posted === true ? "checked" : ""} /></div>
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
                    <td><input type="text" className="form-control" name={i} onChange={this.onChangeAmount.bind(this) } value={store.journalEntry.journalEntryLines[i].amount} /></td>
                    <td><input type="text" className="form-control" name={i} onChange={this.onChangeMemo.bind(this) } value={store.journalEntry.journalEntryLines[i].memo === null ? undefined : store.journalEntry.journalEntryLines[i].memo} /></td>
                    <td>         
                        <button type="button" className="btn btn-box-tool">
                            <i className="fa fa-fw fa-times" name={i} onClick={this.onClickRemoveLineItem.bind(this) }></i> 
                        </button>              
                    </td>
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
                                <td>
                                    <button type="button" className="btn btn-box-tool">
                                        <i className="fa fa-fw fa-check" name={i} onClick={this.addLineItem}></i>
                                    </button>
                                </td>
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
                    <PostJournalEntryButton />
                </div>
                <div>
                    <span>This form still under development.</span>
                </div>
            </div>
        );
    }
}

ReactDOM.render(<JournalEntry />, document.getElementById("divJournalEntry"));