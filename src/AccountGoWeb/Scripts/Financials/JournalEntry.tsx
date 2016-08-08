import * as React from "react";
import * as ReactDOM from "react-dom";
import {observer} from "mobx-react";
import {autorun, reaction, toJS, intercept} from 'mobx';
import * as d3 from "d3";
import Config = require("Config");

import SelectVoucherType from "../Shared/Components/SelectVoucherType";
import SelectAccount from "../Shared/Components/SelectAccount";
import SelectDebitCredit from "../Shared/Components/SelectDebitCredit";

import JournalEntryStore from "../Shared/Stores/Financials/JournalEntryStore";
import JournalEntryUIStore from "../Shared/Stores/Financials/JournalEntryUIStore";

let journalEntryId = window.location.search.split("?id=")[1];

let store = new JournalEntryStore();
let uiStore = new JournalEntryUIStore(store);

@observer
class ValidationErrors extends React.Component<any, {}>{
    render() {
        if (store.validationErrors !== undefined && store.validationErrors.length > 0) {
            var errors = [];
            store.validationErrors.map(function (item, index) {
                errors.push(<li key={index}>{item}</li>);
            });
            return (
                <div>
                    <ul>
                        {errors}
                    </ul>
                </div>

            );
        }
        return null;
    }
}

@observer
class SaveJournalEntryButton extends React.Component<any, {}>{
    onClickSaveNewJournalEntry(e) {
        store.saveNewJournalEntry();
    }
    render() {
        return (
            <input type="button" className={store.isDirty ? "btn btn-sm btn-primary btn-flat pull-left" : "btn btn-sm btn-primary btn-flat pull-left disabled"} value="Save" onClick={this.onClickSaveNewJournalEntry.bind(this) } />
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
            <input type="button" className="btn btn-sm btn-default btn-flat pull-left" value="Cancel" onClick={ this.cancelOnClick.bind(this) } id="btnCancel" />
        );
    }
}

@observer
class PostJournalEntryButton extends React.Component<any, {}>{
    postOnClick(e) {
        store.postJournal();
    }

    render() {
        return (
            <input type="button" value="Post" onClick={ this.postOnClick.bind(this) } className={store.isDirty || store.journalEntry.id == 0 ? "btn btn-sm btn-danger btn-flat pull-right disabled" : "btn btn-sm btn-danger btn-flat pull-right"} />
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
                                value={store.journalEntry.journalDate !== undefined ? store.journalEntry.journalDate.substring(0, 10) : new Date(Date.now()).toISOString().substring(0, 10) } /></div>
                        </div>
                        <div className="row">
                            <div className="col-sm-3">Voucher</div>
                            <div className="col-sm-9"><SelectVoucherType store={store} controlId="optNewVoucherType" selected={store.journalEntry.voucherType} /></div>
                        </div>
                        <div className="row">                            
                            <div className="col-sm-3">Reference no</div>
                            <div className="col-sm-9"><input type="text" className="form-control" value={store.journalEntry.referenceNo || ''} /* || '' fix the issue about uncontrolled input warning when using React 15*/ onChange={this.onChangeReferenceNo.bind(this)} /></div>
                        </div>
                        <div className="row">
                            <div className="col-sm-3">Memo</div>
                            <div className="col-sm-9"><input type="text" className="form-control" value={store.journalEntry.memo || ''} onChange={this.onChangeMemo.bind(this) } /></div>
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

        store.addLineItem(0, accountId, drcr, amount, memo);
        
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
                    <td><input type="text" className="form-control" name={i} onChange={this.onChangeMemo.bind(this) } value={store.journalEntry.journalEntryLines[i].memo || ''} /></td>
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

@observer
export default class JournalEntry extends React.Component<any, {}> {
    componentDidMount() {
        if (journalEntryId !== undefined) {
            store.getJournalEntry(parseInt(journalEntryId));
        }
        else {
            store.changeInitialized(true);
            store.changeIsDirty(true);
        }

        autorun(() => this.trackchange());
    }

    trackchange() {
        if (store.journalEntry.posted) {
            var nodes = document.getElementById("divJournalEntry").getElementsByTagName('*');
            for (var i = 0; i < nodes.length; i++) {
                if (nodes[i].id === "btnCancel")
                    continue;
                nodes[i].setAttribute("disabled", "disabled");
            }
        }

        if (store.initialized && store.isDirty === false) {
            if (store.journalEntry.referenceNo !== store.originalJournalEntry.referenceNo
                || store.journalEntry.memo !== store.originalJournalEntry.memo)
            {
                store.changeIsDirty(true);
            }
        }
    }

    render() {
        return (
            <div id="divJournalEntry">
                <ValidationErrors />
                <JournalEntryHeader />
                <JournalEntryLines />
                <div>
                    <SaveJournalEntryButton />
                    <CancelJournalEntryButton />
                    <PostJournalEntryButton />
                </div>
            </div>
        );
    }
}

ReactDOM.render(<JournalEntry />, document.getElementById("divJournalEntry"));