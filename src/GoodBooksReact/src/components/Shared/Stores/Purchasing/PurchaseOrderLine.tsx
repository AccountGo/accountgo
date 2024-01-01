export default class PurchaseOrderLine {
    id = 0;
    itemId: number;
    measurementId: number;
    quantity: number;
    amount: number;
    discount: number;
    code: string;

    constructor(id: number, itemId: number, measurementId: number, quantity: number, amount: number, discount: number, code: string) {
        this.id = id;
        this.itemId = itemId;
        this.measurementId = measurementId;
        this.quantity = quantity;
        this.amount = amount;
        this.discount = discount;
        this.code = code;
    }
}