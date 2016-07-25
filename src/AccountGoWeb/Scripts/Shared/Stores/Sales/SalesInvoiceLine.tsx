export default class SalesInvoiceLine {
    itemId;
    measurementId;
    quantity;
    amount;
    discount;

    constructor(itemId, measurementId, quantity, amount, discount) {
        this.itemId = itemId;
        this.measurementId = measurementId;
        this.quantity = quantity;
        this.amount = amount;
        this.discount = discount;
    }
}