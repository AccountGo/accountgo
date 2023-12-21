export default class PurchaseInvoiceLine {
    id = 0;
    itemId;
    measurementId;
    quantity;
    amount;
    discount;
    remainingQtyToInvoice: number;

    constructor(id, itemId, measurementId, quantity, amount, discount) {
        this.id = id;
        this.itemId = itemId;
        this.measurementId = measurementId;
        this.quantity = quantity;
        this.amount = amount;
        this.discount = discount;
        this.remainingQtyToInvoice = quantity;
    }
}