import {observable, extendObservable, makeObservable} from 'mobx';
import axios from "axios";
import Config from '../../Config';

import JournalEntry from './JournalEntry';
import JournalEntryLine from './JournalEntryLine';

import CommonStore from "../Common/CommonStore";

const baseUrl = location.protocol
    + "//" + location.hostname
    + (location.port && ":" + location.port)
    + "/";

export default class JournalEntryStore {
    originalJournalEntry: JournalEntry = new JournalEntry();
    journalEntry: JournalEntry = new JournalEntry();
    commonStore: CommonStore = new CommonStore();
    validationErrors: string[] = [];
    isDirty = false;
    initialized = false;
    editMode = false;

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

        makeObservable(this, {
            validationErrors: observable,
            isDirty: observable,
            initialized: observable,
            editMode: observable,
        });

        const journalEntryId = window.location.search.split("?id=")[1];
        if (journalEntryId !== undefined)
            this.getJournalEntry(parseInt(journalEntryId))
        else
            this.changedEditMode(true);
    }

    changeIsDirty(dirty: boolean) {
        this.isDirty = dirty;
    }

    changeInitialized(initialized: boolean) {
        this.initialized = initialized;
    }

    getJournalEntry(journalEntryId: number) {
        axios.get(Config.API_URL + "financials/journalentry?id=" + journalEntryId)
            .then((result) => {
                for (let i = 0; i < result.data.journalEntryLines.length; i++) {
                    const item = result.data.journalEntryLines[i];
                    this.addLineItem(item.id, item.accountId, item.drCr, item.amount, item.memo);
                }
                this.journalEntry.id = result.data.id;                
                this.journalEntry.voucherType = result.data.voucherType;
                this.journalEntry.journalDate = result.data.journalDate;
                this.journalEntry.memo = result.data.memo;
                this.journalEntry.referenceNo = result.data.referenceNo;
                this.journalEntry.posted = result.data.posted;
                this.journalEntry.readyForPosting = result.data.readyForPosting;

                const nodes = document.getElementById("divJournalEntryForm")!.getElementsByTagName('*');
                for (let i = 0; i < nodes.length; i++) {
                    nodes[i].className += " disabledControl";
                }
            });
    }

    saveNewJournalEntry() {
        if (this.validation() && this.validationErrors.length == 0) {
            axios.post(Config.API_URL + "financials/savejournalentry", JSON.stringify(this.journalEntry),
                {
                    headers: {
                        'Content-type': 'application/json'
                    }
                })
                .then(() => {
                    window.location.href = baseUrl + 'financials/journalentries';
                })
                .catch((error) => {
                    if (axios.isAxiosError(error)) {
                        this.validationErrors.push(`Status: ${error.status} - Message: ${error.response?.data}`);
                      } else {
                        console.error(error);
                        this.validationErrors.push(`Error: ${error}`);
                      }
                })
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
            for (let i = 0; i < this.journalEntry.journalEntryLines.length; i++) {
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
            axios.post(Config.API_URL + "financials/postjournalentry", JSON.stringify(this.journalEntry),
                {
                    headers: {
                        'Content-type': 'application/json'
                    }
                })
                .then(() => {
                    window.location.href = baseUrl + 'financials/journalentries';
                })
                .catch((error) => {
                    if (axios.isAxiosError(error)) {
                        this.validationErrors.push(`Status: ${error.status} - Message: ${error.response?.data}`);
                      } else {
                        console.error(error);
                        this.validationErrors.push(`Error: ${error}`);
                      }
                })
        }
    }

    addLineItem(id: number, accountId: number, drcr: number, amount: number, memo: string) {
        const newLineItem = new JournalEntryLine(id, accountId, drcr, amount, memo);
        this.journalEntry.journalEntryLines.push(extendObservable(newLineItem, newLineItem));
    }

    updateLineItem(row: number, targetProperty: keyof JournalEntryLine, value: string | number) {
        if (this.journalEntry.journalEntryLines.length > 0) {
            (this.journalEntry.journalEntryLines[row] as Record<keyof JournalEntryLine, string | number>)[targetProperty] = value;
        }
    }

    removeLineItem(row: number) {
        this.journalEntry.journalEntryLines.splice(row, 1);
    }

    changedJournalDate(date: Date) {
        this.journalEntry.journalDate = date;
    }

    changedReferenceNo(refNo: string) {
        this.journalEntry.referenceNo = refNo;
    }

    changedMemo(memo: string) {
        this.journalEntry.memo = memo;
    }

    changedVoucherType(type: number) {
        this.journalEntry.voucherType = type;
    }

    changedEditMode(editMode: boolean) {
        this.editMode = editMode;
    }
}