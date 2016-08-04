import {observable, extendObservable, action} from 'mobx';
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

        if (journalEntryId !== undefined)
        {
            var result = axios.get(Config.apiUrl + "api/financials/journalentry?id=" + journalEntryId);
            result.then(function (result) {
                this.changedJournalDate(result.data.journalDate);
                this.changedVoucherType(result.data.voucherType);
                this.postJournal(result.data.posted);
                this.journalEntry.memo = result.data.memo;
                this.journalEntry.referenceNo = result.data.referenceNo;
                for (var i = 0; i < result.data.journalEntryLines.length; i++) {
                    var item = result.data.journalEntryLines[i];
                    this.addLineItem(item.accountId, item.drCr, item.amount, item.memo);
                }
            }.bind(this));
        }
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

        return this.validationErrors.length === 0;
    }

    postJournal(post) {
        this.journalEntry.posted = post;
    }

    addLineItem(accountId, drcr, amount, memo) {
        var newLineItem = new JournalEntryLine(accountId, drcr, amount, memo);
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

    changedVoucherType(type) {
        this.journalEntry.voucherType = type;
    }
}