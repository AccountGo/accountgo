import {observable, extendObservable, action} from 'mobx';
import SalesOrder from './SalesOrder';

export default class SalesStore {
    @observable salesOrders: SalesOrder[] = [];

    addSalesOrder(order: SalesOrder) {
        this.salesOrders.push(order);
    }

    fillSalesOrders(orders) {        
        for (var i = 0; i < orders.length; i++)
            this.addSalesOrder(orders[i]);
    }
}