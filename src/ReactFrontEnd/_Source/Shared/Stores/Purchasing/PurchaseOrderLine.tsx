export default class PurchaseOrderLine {
    id = 0;
    itemId;
    measurementId;
    quantity;
    amount;
    discount;
    code;

    constructor(id, itemId, measurementId, quantity, amount, discount, code) {
        this.id = id;
        this.itemId = itemId;
        this.measurementId = measurementId;
        this.quantity = quantity;
        this.amount = amount;
        this.discount = discount;
        this.code = code;
    }
}