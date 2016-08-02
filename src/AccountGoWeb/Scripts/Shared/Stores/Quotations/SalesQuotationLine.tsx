export default class SalesQuotationLine {
    id: number;
    itemId: number;
    measurementId: number;
    quantity: number;
    amount: number;
    discount: number;

    constructor(id = 0, itemId, measurementId, quantity, amount, discount) {
        this.id = id;
        this.itemId = itemId;
        this.measurementId = measurementId;
        this.quantity = quantity;
        this.amount = amount;
        this.discount = discount;
    }
}