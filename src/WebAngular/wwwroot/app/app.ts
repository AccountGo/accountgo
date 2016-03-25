import {Component} from 'angular2/core';
import {RouteConfig, RouterOutlet} from 'angular2/router';

import {DashboardComponent} from './dashboard/dashboard';
import {NavigationComponent} from './navigation/navigation';

import {SalesQuotationsComponent} from './accounts-receivable/sales-quotations';
import {SalesOrdersComponent} from './accounts-receivable/sales-orders';
import {SalesReceiptsComponent} from './accounts-receivable/sales-receipts';
import {SalesInvoicesComponent} from './accounts-receivable/sales-invoices';

import {SalesOrderFormComponent} from './accounts-receivable/sales-order-form';

import {PurchaseOrdersComponent} from './accounts-payable/purchase-orders';
import {PurchaseInvoicesComponent} from './accounts-payable/purchase-invoices';

import {ItemsComponent} from './inventory-and-control/items';
import {InventoryComponent} from './inventory-and-control/inventory';

import {GeneralLedgersComponent} from './financials/general-ledgers';
import {JournalEntriesComponent} from './financials/journal-entries';
import {TaxesComponent} from './financials/taxes';

import {SetupComponent} from './administration/setup';
import {CompanyComponent} from './administration/company';

@Component({
    selector: 'app',
    templateUrl: 'app/app.html',
    directives: [NavigationComponent, RouterOutlet]
})
@RouteConfig([
    { path: '/dashboard', component: DashboardComponent, useAsDefault: true, name: 'Dashboard' },

    { path: '/accountsreceivable/salesquotations', component: SalesQuotationsComponent, name: 'Sales Quotations' },
    { path: '/accountsreceivable/salesorders', component: SalesOrdersComponent, name: 'Sales Orders' },
    { path: '/accountsreceivable/salesorders/form', component: SalesOrderFormComponent, name: 'Sales Order Form' },
    { path: '/accountsreceivable/salesreceipts', component: SalesReceiptsComponent, name: 'Sales Receipts' },
    { path: '/accountsreceivable/salesinvoices', component: SalesInvoicesComponent, name: 'Sales Invoices' },    

    { path: '/accountspayable/purchaseorders', component: PurchaseOrdersComponent, name: 'Purchase Orders' },
    { path: '/accountspayable/purchaseinvoices', component: PurchaseInvoicesComponent, name: 'Purchase Invoices' },

    { path: '/inventoryanditems/items', component: ItemsComponent, name: 'Items' },
    { path: '/inventoryanditems/inventory', component: InventoryComponent, name: 'Inventory' },

    { path: '/financials/generalledgers', component: GeneralLedgersComponent, name: 'General Ledgers' },
    { path: '/financials/journalentries', component: JournalEntriesComponent, name: 'Journal Entries' },
    { path: '/financials/taxes', component: TaxesComponent, name: 'Taxes' },

    { path: '/administration/setup', component: SetupComponent, name: 'Company' },
    { path: '/administration/company', component: CompanyComponent, name: 'Setup' },
])
export class App {

}

//var accountsPayable = new NavigationMenu('Accounts Receivable');
//accountsPayable.items.push(new NavigationMenu('Sales Quotations'));
//accountsPayable.items.push(new NavigationMenu('Sales Orders'));
//accountsPayable.items.push(new NavigationMenu('Sales Receipts'));
//accountsPayable.items.push(new NavigationMenu('Sales Invoices'));

//var accountsReceivable = new NavigationMenu('Accounts Payable');
//accountsReceivable.items.push(new NavigationMenu('Purchase Orders'));
//accountsReceivable.items.push(new NavigationMenu('Purchase Invoices'));

//var inventoryAndControl = new NavigationMenu('Inventory and Control')
//inventoryAndControl.items.push(new NavigationMenu('Items'));
//inventoryAndControl.items.push(new NavigationMenu('Inventory'));

//var financials = new NavigationMenu('Financials')
//financials.items.push(new NavigationMenu('Journal Entries'));
//financials.items.push(new NavigationMenu('General Ledgers'));
//financials.items.push(new NavigationMenu('Taxes'));

//var administration = new NavigationMenu('Administration')
//administration.items.push(new NavigationMenu('Company'));
//administration.items.push(new NavigationMenu('Setup'));

