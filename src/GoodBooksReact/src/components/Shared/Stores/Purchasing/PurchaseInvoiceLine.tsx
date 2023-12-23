export default class PurchaseInvoiceLine {
    id: number = 0;
    itemId: any;
    measurementId: any;
    quantity: number;
    amount: number;
    discount: number;
    remainingQtyToInvoice: number;
    code: any;

    constructor(id: number, itemId: any, measurementId: any, 
        quantity: number, amount: number, discount: number, code: any) {
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