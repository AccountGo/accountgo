export default class JournalEntryLine {
    id: number;
    accountId: number;
    drcr: number;
    amount: number;
    memo: string;

    constructor(accountId, drcr, amount, memo) {
        this.accountId = accountId;
        this.drcr = drcr;
        this.amount = amount;
        this.memo = memo;
    }
}