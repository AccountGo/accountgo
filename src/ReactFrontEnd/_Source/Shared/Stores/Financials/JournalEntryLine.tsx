export default class JournalEntryLine {
    id: number = 0;
    accountId: number;
    drcr: number;
    amount: number;
    memo: string;

    constructor(id, accountId, drcr, amount, memo) {
        this.id = id;
        this.accountId = accountId;
        this.drcr = drcr;
        this.amount = amount;
        this.memo = memo;
    }
}