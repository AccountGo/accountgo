import {Component} from 'angular2/core';
import {Router} from 'angular2/router';

@Component({
    selector: 'sales-orders',
    templateUrl: 'app/accounts-receivable/sales-orders.html'
})
export class SalesOrdersComponent {
    constructor(private router: Router) {

    }

    addSalesOrder() {
        this.router.navigate(['Sales Order Form'])
    }
}