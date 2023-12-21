export default class SalesInvoiceLine {
    id: number;
    itemId: number;
    measurementId: number;
    quantity: number;
    amount: number;
    discount: number;
    remainingQtyToInvoice: number;
    code: string;

    constructor(id, itemId, measurementId, quantity, amount, discount, code) {
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