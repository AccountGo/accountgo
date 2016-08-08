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
    journalEntry;
    commonStore;
    @observable validationErrors;
    @observable isDirty = false;

    constructor(journalEntryId) {
        this.commonStore = new CommonStore();
        this.journalEntry = new JournalEntry();
        extendObservable(this.journalEntry, {
            voucherType: this.journalEntry.voucherType,
            journalDate: this.journalEntry.journalDate,
            referenceNo: this.journalEntry.referenceNo,
            memo: this.journalEntry.memo,
            posted: this.journalEntry.posted,
            journalEntryLines: []
        });

        if (journalEntryId !== undefined) {
            this.getJournalEntry(journalEntryId);
        }
    }

    changeIsDirty() {
        this.isDirty = true;
    }

    async getJournalEntry(journalEntryId) {
        await axios.get(Config.apiUrl + "api/financials/journalentry?id=" + journalEntryId)
            .then(function (result) {
                this.journalEntry.id = result.data.id;
                
                this.journalEntry.voucherType = result.data.voucherType;
                this.journalEntry.date = result.data.journalDate;
                this.journalEntry.memo = result.data.memo === null ? undefined : result.data.memo;
                this.journalEntry.referenceNo = result.data.memo === null ? undefined : result.data.referenceNo;

                for (var i = 0; i < result.data.journalEntryLines.length; i++) {
                    var item = result.data.journalEntryLines[i];
                    this.addLineItem(item.accountId, item.drCr, item.amount, item.memo);
                }

                this.journalEntry.posted = result.data.posted;

                this.isDirty = false;

            }.bind(this));
    }

    async saveNewJournalEntry() {
        if (this.validation() && this.validationErrors.length == 0) {
            await axios.post(Config.apiUrl + "api/financials/savejournalentry", JSON.stringify(this.journalEntry),
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
        if (this.journalEntry.date === undefined)
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
                if (this.journalEntry.journalEntryLines[i].drCr === undefined)
                    this.validationErrors.push("DrCr is required.");
                if (this.journalEntry.journalEntryLines[i].amount === undefined)
                    this.validationErrors.push("Amount is required.");
                if (this.journalEntry.journalEntryLines[i].memo === undefined)
                    this.validationErrors.push("Memo is required.");
            }
        }
        return this.validationErrors.length === 0;
    }

    postJournal(post) {
        this.journalEntry.posted = post;
    }

    addLineItem(accountId, drcr, amount, memo) {
        var newLineItem = new JournalEntryLine(accountId, drcr, amount, memo);
        this.journalEntry.journalEntryLines.push(extendObservable(newLineItem, newLineItem));
        this.changeIsDirty();
    }

    updateLineItem(row, targetProperty, value) {
        if (this.journalEntry.journalEntryLines.length > 0)
            this.journalEntry.journalEntryLines[row][targetProperty] = value;
        this.changeIsDirty();
    }

    removeLineItem(row) {
        this.journalEntry.journalEntryLines.splice(row, 1);
        this.changeIsDirty();
    }

    changedJournalDate(date) {
        this.journalEntry.journalDate = date;
        this.changeIsDirty();
    }

    changedReferenceNo(refNo) {
        this.journalEntry.referenceNo = refNo;
        this.changeIsDirty();
    }

    changedMemo(memo) {
        this.journalEntry.memo = memo;
        this.changeIsDirty();
    }

    changedVoucherType(type) {
        this.journalEntry.voucherType = type;
        this.changeIsDirty();
    }
}