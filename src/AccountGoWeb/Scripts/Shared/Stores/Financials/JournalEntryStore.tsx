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

    constructor(journalEntryId) {
        this.commonStore = new CommonStore();
        this.journalEntry = new JournalEntry();
        extendObservable(this.journalEntry, {
            voucherType: this.journalEntry.voucherType,
            journalDate: this.journalEntry.journalDate,
            referenceNo: this.journalEntry.referenceNo,
            memo: this.journalEntry.memo,
            journalEntryLines: []
        });

        if (journalEntryId !== undefined)
        {
            var result = axios.get(Config.apiUrl + "api/financials/journalentry?id=" + journalEntryId);
            result.then(function (result) {
                this.changedJournalDate(result.data.journalDate);
                this.changedVoucherType(result.data.voucherType);
                for (var i = 0; i < result.data.journalEntryLines.length; i++) {
                    var item = result.data.journalEntryLines[i];
                    this.addLineItem(item.accountId, item.drcr, item.amount, item.memo);
                }
            }.bind(this));
        }
    }

    saveNewJournalEntry() {
        console.log(this.journalEntry);
        axios.post(Config.apiUrl + "api/financials/addjournalentry", JSON.stringify(this.journalEntry),
            {
                headers: {
                    'Content-type': 'application/json'
                }
            })
            .then(function (response) {
                console.log(response);
            })
            .catch(function (error) {
                console.log(error);
            })
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