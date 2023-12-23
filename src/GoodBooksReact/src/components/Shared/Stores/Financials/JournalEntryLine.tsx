export default class JournalEntryLine {
    id: number = 0;
    accountId: number = 0;
    drcr: number = 0;
    amount: number = 0;
    memo: string = "";

    constructor(id: number, accountId: number, drcr: number, amount: number, memo: string) {
        this.id = id;
        this.accountId = accountId;
        this.drcr = drcr;
        this.amount = amount;
        this.memo = memo;
    }
}