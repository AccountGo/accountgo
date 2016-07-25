import * as React from "react";
import * as ReactDOM from "react-dom";
import {observer} from "mobx-react";
import * as d3 from "d3";
import Config = require("Config");

import SelectVoucherType from "../Shared/Components/SelectVoucherType";
import SelectAccount from "../Shared/Components/SelectAccount";
import SelectDebitCredit from "../Shared/Components/SelectDebitCredit";

import JournalEntryStore from "../Shared/Stores/Financials/JournalEntryStore";

let store = new JournalEntryStore();

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
            <div>
                <div>
                    <label>Date: </label>
                    <input type="date" id="newJournalDate" onChange={this.onChangeJournalDate.bind(this) } defaultValue={store.journalEntry.date} />                 
                </div>
                <div>
                    <label>Vourcher: </label>
                    <SelectVoucherType store={store} controlId="optNewVoucherType" />
                </div>
                <div>
                    <label>Reference No: </label>
                    <input type="text" />
                </div>
                <div>
                    <label>Memo: </label>
                    <input type="text" />
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
                    <td><input type="text" name={i} onChange={this.onChangeAmount.bind(this)} /></td>
                    <td><input type="text" name={i} onChange={this.onChangeMemo.bind(this) } /></td>
                    <td><input type="button" name={i} value="Remove" onClick={this.onClickRemoveLineItem.bind(this) } /></td>
                </tr>
            );
        }
        return (
            <div>
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
                            <td><input type="text" id="txtNewAmount" /></td>                            
                            <td><input type="text" id="txtNewMemo" /></td>                            
                            <td><input type="button" value="Add" onClick={this.addLineItem} /></td>
                        </tr>
                    </tbody>
                    <tfoot>
                        <tr>
                            <td colSpan="8">Count:</td>
                        </tr>
                    </tfoot>
                </table>
            </div>
        );
    }
}

export default class AddJournalEntry extends React.Component<any, {}> {
    render() {
        return (
            <div>
                <div>
                    <JournalEntryHeader />
                </div>
                <div>
                    <JournalEntryLines />
                </div>
                <div>
                    <SaveJournalEntryButton />
                    <CancelJournalEntryButton />
                </div>
            </div>
        );
    }
}

ReactDOM.render(<AddJournalEntry />, document.getElementById("divAddJournalEntry"));