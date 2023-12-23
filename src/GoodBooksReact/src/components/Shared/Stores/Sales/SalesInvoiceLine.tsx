export default class SalesInvoiceLine {
    id: number = 0;
    itemId: number = 0;
    measurementId: number = 0;
    quantity: number = 0;
    amount: number = 0;
    discount: number = 0;
    remainingQtyToInvoice: number = 0;
    code: string = "";

    constructor(id: number, itemId: number, measurementId: number, quantity: number, amount: number, discount: number, code: string) {
        this.id = id;
        this.itemId = itemId;
        this.measurementId = measurementId;
        this.quantity = quantity;
        this.amount = amount;
        this.discount = discount;
        this.remainingQtyToInvoice = quantity;
        this.code = code;
    }
}