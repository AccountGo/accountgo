"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
class SalesInvoiceLine {
    constructor(id, itemId, measurementId, quantity, amount, discount, code) {
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
exports.default = SalesInvoiceLine;
//# sourceMappingURL=SalesInvoiceLine.js.map