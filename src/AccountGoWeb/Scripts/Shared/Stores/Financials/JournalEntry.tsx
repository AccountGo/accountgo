import JournalEntryLine from "./JournalEntryLine";

export default class JournalEntry {
    id: number;  
    voucherType: number;
    journalDate: Date;
    referenceNo: string;
    memo: string;
    journalEntryLines: JournalEntryLine[] = [];
}