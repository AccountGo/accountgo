export default class JournalEntryLine {
    id;
    accountId;
    drcr;
    amount;
    memo;

    constructor(accountId, drcr, amount, memo) {
        this.accountId = accountId;
        this.drcr = drcr;
        this.amount = amount;
        this.memo = memo;
    }
}