import {Component} from 'angular2/core';

@Component({
    selector: 'sales-order-form',
    templateUrl: 'app/accounts-receivable/sales-order-form.html'
})
export class SalesOrderFormComponent {
    salesOrder: SalesOrder = new SalesOrder()
    action: string;

    constructor() {
        this.salesOrder = new SalesOrder()
        this.salesOrder.date = new Date();
        this.salesOrder.store = '';
        this.salesOrder.customer = '';
        this.salesOrder.priceType = '';

        this.salesOrder.items.push({ itemCode: 'test', itemName: 'test', qty: 1, unit: 1, price: 1, amount: 1, discount: 1 });
    }

    addItem() {
        this.action = 'Add';
    }

    editItem() {
        this.action = 'Edit';
    }
}

export class SalesOrder {
    date: Date;
    store: string;
    customer: string;
    priceType: string;
    refs: string;
    items: Array<SalesOrderItem> = new Array<SalesOrderItem>();
}

export class SalesOrderItem {
    itemCode: string;
    itemName: string;
    qty: number;
    unit: number;
    price: number;
    amount: number;
    discount: number;
}