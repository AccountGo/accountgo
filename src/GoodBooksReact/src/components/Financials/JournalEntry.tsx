import * as React from "react";
//import * as ReactDOM from "react-dom";
import {observer} from "mobx-react";

import SelectVoucherType from "../Shared/Components/SelectVoucherType";
import SelectAccount from "../Shared/Components/SelectAccount";
import SelectDebitCredit from "../Shared/Components/SelectDebitCredit";

import JournalEntryStore from "../Shared/Stores/Financials/JournalEntryStore";

const store = new JournalEntryStore();

class ValidationErrors extends React.Component {
    render() {
        if (store.validationErrors !== undefined && store.validationErrors.length > 0) {
            const errors: string[] = [];
            store.validationErrors.map(function (item: any, index: number) {
                const errors: React.ReactNode[] = [];
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
const ObservedValidationErrors = observer(ValidationErrors);

class EditButton extends React.Component {
    onClickEditButton() {
        // Remove " disabledControl" from current className
        const nodes = document.getElementById("divJournalEntryForm")?.getElementsByTagName('*');
        for (let i = 0; i < nodes!.length; i++) {
            const subStringLength = nodes![i].className.length - " disabledControl".length;
            nodes![i].className = nodes![i].className.substring(0, subStringLength);
        }
        store.changedEditMode(true)
    }
    render() {
        return (
            <a href="#" id="linkEdit" onClick={this.onClickEditButton}
                className={!store.journalEntry.posted && !store.editMode
                    ? "btn"
                    : "btn inactiveLink"}>
                <i className="fa fa-edit"></i>
                Edit
            </a>
        );
    }
}
const ObservedEditButton = observer(EditButton);

class SaveJournalEntryButton extends React.Component {
    onClickSaveNewJournalEntry() {
        store.saveNewJournalEntry();
    }
    render() {
        return (
            <input type="button" value="Save" onClick={this.onClickSaveNewJournalEntry.bind(this) }
                className={!store.journalEntry.posted && store.editMode
                    ? "btn btn-sm btn-primary btn-flat pull-left"
                    : "btn btn-sm btn-primary btn-flat pull-left inactiveLink"}
                />
        );
    }
}
const ObservedSaveJournalEntryButton = observer(SaveJournalEntryButton);

class CancelJournalEntryButton extends React.Component {
    cancelOnClick() {
        const baseUrl = location.protocol
            + "//" + location.hostname
            + (location.port && ":" + location.port)
            + "/";

        window.location.href = baseUrl + 'financials/journalentries';
    }

    render() {
        return (
            <input type="button" onClick={ this.cancelOnClick.bind(this) } id="btnCancel"
                className="btn btn-sm btn-default btn-flat pull-left" value="Cancel" />
        );
    }
}

class PostJournalEntryButton extends React.Component {
    postOnClick() {
        store.postJournal();
    }

    render() {
        return (
            <input type="button" value="Post" onClick={ this.postOnClick.bind(this) }
                className={!store.journalEntry.posted && store.journalEntry.readyForPosting && !store.editMode
                    ? "btn btn-sm btn-primary btn-flat btn-danger pull-right"
                    : "btn btn-sm btn-primary btn-flat btn-danger pull-right inactiveLink"} />
        );
    }
}
const ObservedPostJournalEntryButton = observer(PostJournalEntryButton);

class JournalEntryHeader extends React.Component {
    onChangeJournalDate(e: React.ChangeEvent<HTMLInputElement>) {
        const dateValue = new Date(e.target.value);
        store.changedJournalDate(dateValue);
    }
    
    onChangeReferenceNo(e: React.ChangeEvent<HTMLInputElement>) {
        store.changedReferenceNo(e.target.value);
    }

    onChangeMemo(e: React.ChangeEvent<HTMLInputElement>) {
        store.changedMemo(e.target.value);
    }

    render() {
        return (
            <div className="card">
                <div className="card-header">
        <a data-toggle="collapse" href="#general" aria-expanded="true" aria-controls="general"><i className="fa fa-align-justify"></i></a> General
        </div>
                <div className="card-body collapse show row" id="general">
                    <div className="col-sm-6">
                        <div className="row">
                            <div className="col-sm-3">Date</div>
                            <div className="col-sm-9"><input type="date" className="form-control" id="newJournalDate" onChange={this.onChangeJournalDate.bind(this) }
                                value={store.journalEntry.journalDate !== undefined ? store.journalEntry.journalDate.toISOString().substring(0, 10) : new Date(Date.now()).toISOString().substring(0, 10) } /></div>
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
                            <div className="col-sm-10"><input type="checkbox" readOnly checked={store.journalEntry.posted === true ? true : false} /></div>
                        </div>
                    </div>
                </div>
            </div>
        );
    }
}
const ObservedJournalEntryHeader = observer(JournalEntryHeader);

class JournalEntryLines extends React.Component {

    onChangeAmount(e: any) {
        store.updateLineItem(e.target.name, "amount", e.target.value);
    }

    onChangeMemo(e: any) {
        store.updateLineItem(e.target.name, "memo", e.target.value);
    }

    onClickRemoveLineItem(i: number) {
        store.removeLineItem(i);
    }
    
    addLineItem() {
        const accountId: string = (document.getElementById("optNewAccountId") as HTMLInputElement).value;
        const drcr: string = (document.getElementById("optNewDebitCredit") as HTMLInputElement).value;
        const amount: string = (document.getElementById("txtNewAmount") as HTMLInputElement).value;
        const memo: string = (document.getElementById("txtNewMemo") as HTMLInputElement).value;

        store.addLineItem(0, Number(accountId), Number(drcr), Number(amount), memo);
        
        (document.getElementById("txtNewAmount") as HTMLInputElement).value = "0";
        (document.getElementById("txtNewMemo") as HTMLInputElement).value = "";
    }
    render() {
        const lineItems: JSX.Element[] = [];
        for (let i = 0; i < store.journalEntry.journalEntryLines.length; i++) {
            lineItems.push(
                <tr key={i}>
                    <td><SelectAccount store={store} row={i} selected={store.journalEntry.journalEntryLines[i].accountId} /></td>
                    <td><SelectDebitCredit store={store} row={i} selected={store.journalEntry.journalEntryLines[i].drcr} /></td>
                    <td><input type="text" className="form-control" name={i.toString()} onChange={this.onChangeAmount.bind(this)} value={store.journalEntry.journalEntryLines[i].amount} /></td>
                    <td><input type="text" className="form-control" name={i.toString()} onChange={this.onChangeMemo.bind(this) } value={store.journalEntry.journalEntryLines[i].memo || ''} /></td>
                    <td>         
                        <button type="button" className="btn btn-box-tool" onClick={this.onClickRemoveLineItem.bind(this, i) }>
                            <i className="fa fa-fw fa-times"></i> 
                        </button>              
                    </td>
                </tr>
            );
        }
        return (
            <div className="card">
                <div className="card-header">
        <a data-toggle="collapse" href="#line-items" aria-expanded="true" aria-controls="line-items"><i className="fa fa-align-justify"></i></a> Line Items
        </div>
                <div className="card-body collapse show table-responsive" id="line-items">
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
                                    <button type="button" className="btn btn-box-tool" onClick={this.addLineItem}>
                                        <i className="fa fa-fw fa-check"></i>
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
const ObservedJournalEntryLines = observer(JournalEntryLines);

class JournalEntry extends React.Component {
    render() {
        return (
            <div>
                <div id="divActionsTop">
                    <ObservedEditButton />
                </div>
                <div id="divJournalEntryForm">
                    <ObservedValidationErrors />
                    <ObservedJournalEntryHeader />
                    <ObservedJournalEntryLines />
                </div>
                <div id="divActionsBottom">
                    <ObservedSaveJournalEntryButton />
                    <CancelJournalEntryButton />
                    <ObservedPostJournalEntryButton />
                </div>
            </div>
        );
    }
}
const ObservedJournalEntry = observer(JournalEntry);

export default ObservedJournalEntry;

