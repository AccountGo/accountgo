export default class SalesOrderLine {
    id: number;
    itemId: number;
    measurementId: number;
    quantity: number;
    amount: number;
    discount: number;
    code: number;
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