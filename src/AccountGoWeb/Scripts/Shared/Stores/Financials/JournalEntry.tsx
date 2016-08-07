import JournalEntryLine from "./JournalEntryLine";

export default class JournalEntry {
    id: number;  
    voucherType: number;
    journalDate: Date;
    referenceNo: string;
    memo: string;
    posted: boolean;
    journalEntryLines: JournalEntryLine[] = [];

    constructor() {
        this.id = 0;
        this.posted = false;
    }
}