export default class SalesQuotationLine {
    id;
    itemId;
    measurementId;
    quantity;
    amount;
    discount;

    constructor(id = 0, itemId, measurementId, quantity, amount, discount) {
        this.id = id;
        this.itemId = itemId;
        this.measurementId = measurementId;
        this.quantity = quantity;
        this.amount = amount;
        this.discount = discount;
    }
}