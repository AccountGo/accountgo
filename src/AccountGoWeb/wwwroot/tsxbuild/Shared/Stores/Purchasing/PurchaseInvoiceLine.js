"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
class PurchaseInvoiceLine {
    constructor(id, itemId, measurementId, quantity, amount, discount) {
        this.id = 0;
        this.id = id;
        this.itemId = itemId;
        this.measurementId = measurementId;
        this.quantity = quantity;
        this.amount = amount;
        this.discount = discount;
        this.remainingQtyToInvoice = quantity;
    }
}
exports.default = PurchaseInvoiceLine;
//# sourceMappingURL=PurchaseInvoiceLine.js.map