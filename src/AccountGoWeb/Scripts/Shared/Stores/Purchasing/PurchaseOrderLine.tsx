export default class PurchaseOrderLine {
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