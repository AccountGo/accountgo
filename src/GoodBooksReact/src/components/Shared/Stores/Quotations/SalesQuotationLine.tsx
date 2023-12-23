export default class SalesQuotationLine {
    id: number = 0;
    itemId: number = 0;
    measurementId: number = 0;
    quantity: number = 0;
    amount: number = 0;
    discount: number = 0;
    code: number = 0;

    constructor(id: number, itemId: number, measurementId: number, 
        quantity: number, amount: number, discount: number, code: number) {
            
        this.id = id;
        this.itemId = itemId;
        this.measurementId = measurementId;
        this.quantity = quantity;
        this.amount = amount;
        this.discount = discount;
        this.code = code;
    }
}