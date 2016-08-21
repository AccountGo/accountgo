import {observable, extendObservable, action, intercept, reaction} from 'mobx';
import * as axios from "axios";
import * as d3 from "d3";

import Config = require("Config");

import JournalEntry from './JournalEntry';
import JournalEntryLine from './JournalEntryLine';

import CommonStore from "../Common/CommonStore";

let baseUrl = location.protocol
    + "//" + location.hostname
    + (location.port && ":" + location.port)
    + "/";

export default class JournalEntryStore {
    originalJournalEntry;
    journalEntry;
    commonStore;
    @observable validationErrors;
    @observable isDirty = false;
    @observable initialized = false;
    @observable editMode = false;

    constructor() {
        this.commonStore = new CommonStore();
        this.journalEntry = new JournalEntry();
        extendObservable(this.journalEntry, {
            voucherType: this.journalEntry.voucherType,
            journalDate: this.journalEntry.journalDate,
            referenceNo: this.journalEntry.referenceNo,
            memo: this.journalEntry.memo,
            posted: this.journalEntry.posted,
            readyForPosting: this.journalEntry.readyForPosting,
            journalEntryLines: this.journalEntry.journalEntryLines
        });

        let journalEntryId = window.location.search.split("?id=")[1];
        if (journalEntryId !== undefined)
            this.getJournalEntry(parseInt(journalEntryId))
        else
            this.changedEditMode(true);
    }

    changeIsDirty(dirty) {
        this.isDirty = dirty;
    }

    changeInitialized(initialized) {
        this.initialized = initialized;
    }

    getJournalEntry(journalEntryId: number) {
        axios.get(Config.apiUrl + "api/financials/journalentry?id=" + journalEntryId)
            .then(function (result) {
                for (var i = 0; i < result.data.journalEntryLines.length; i++) {
                    var item = result.data.journalEntryLines[i];
                    this.addLineItem(item.id, item.accountId, item.drCr, item.amount, item.memo);
                }
                this.journalEntry.id = result.data.id;                
                this.journalEntry.voucherType = result.data.voucherType;
                this.journalEntry.journalDate = result.data.journalDate;
                this.journalEntry.memo = result.data.memo;
                this.journalEntry.referenceNo = result.data.referenceNo;
                this.journalEntry.posted = result.data.posted;
                this.journalEntry.readyForPosting = result.data.readyForPosting;

                var nodes = document.getElementById("divJournalEntryForm").getElementsByTagName('*');
                for (var i = 0; i < nodes.length; i++) {
                    nodes[i].className += " disabledControl";
                }
            }.bind(this));
    }

    saveNewJournalEntry() {
        if (this.validation() && this.validationErrors.length == 0) {
            axios.post(Config.apiUrl + "api/financials/savejournalentry", JSON.stringify(this.journalEntry),
                {
                    headers: {
                        'Content-type': 'application/json'
                    }
                })
                .then(function (response) {
                    window.location.href = baseUrl + 'financials/journalentries';
                })
                .catch(function (error) {
                    error.data.map(function (err) {
                        this.validationErrors.push(err);
                    }.bind(this));
                }.bind(this))
        }
    }

    validation()
    {
        this.validationErrors = [];
        if (this.journalEntry.journalDate === undefined)
            this.validationErrors.push("Date is required.");
        if (this.journalEntry.voucherType === undefined)
            this.validationErrors.push("Voucher type is required.");
        if (this.journalEntry.referenceNo === undefined)
            this.validationErrors.push("Reference no is required.");
        if (this.journalEntry.memo === undefined)
            this.validationErrors.push("Memo no is required.");
        if (this.journalEntry.journalEntryLines.length < 2)
            this.validationErrors.push("You need at lest 2 journal entry lines for debit and credit.");
        if (this.journalEntry.journalEntryLines !== undefined && this.journalEntry.journalEntryLines.length > 0) {
            for (var i = 0; i < this.journalEntry.journalEntryLines.length; i++) {
                if (this.journalEntry.journalEntryLines[i].accountId === undefined)
                    this.validationErrors.push("Account is required.");
                if (this.journalEntry.journalEntryLines[i].drcr === undefined)
                    this.validationErrors.push("DrCr is required.");
                if (this.journalEntry.journalEntryLines[i].amount === undefined)
                    this.validationErrors.push("Amount is required.");
                if (this.journalEntry.journalEntryLines[i].memo === undefined)
                    this.validationErrors.push("Memo is required.");
            }
        }
        return this.validationErrors.length === 0;
    }

    postJournal() {
        if (this.validation() && this.validationErrors.length == 0) {
            axios.post(Config.apiUrl + "api/financials/postjournalentry", JSON.stringify(this.journalEntry),
                {
                    headers: {
                        'Content-type': 'application/json'
                    }
                })
                .then(function (response) {
                    window.location.href = baseUrl + 'financials/journalentries';
                })
                .catch(function (error) {
                    error.data.map(function (err) {
                        this.validationErrors.push(err);
                    }.bind(this));
                }.bind(this))
        }
    }

    addLineItem(id, accountId, drcr, amount, memo) {
        var newLineItem = new JournalEntryLine(id, accountId, drcr, amount, memo);
        this.journalEntry.journalEntryLines.push(extendObservable(newLineItem, newLineItem));
    }

    updateLineItem(row, targetProperty, value) {
        if (this.journalEntry.journalEntryLines.length > 0)
            this.journalEntry.journalEntryLines[row][targetProperty] = value;
    }

    removeLineItem(row) {
        this.journalEntry.journalEntryLines.splice(row, 1);
    }

    changedJournalDate(date) {
        this.journalEntry.journalDate = date;
    }

    changedReferenceNo(refNo) {
        this.journalEntry.referenceNo = refNo;
    }

    changedMemo(memo) {
        this.journalEntry.memo = memo;
    }

    changedVoucherType(type) {
        this.journalEntry.voucherType = type;
    }

    changedEditMode(editMode) {
        this.editMode = editMode;
    }
}