System.register(['angular2/core', 'angular2/router', './dashboard/dashboard', './navigation/navigation', './accounts-receivable/sales-quotations', './accounts-receivable/sales-orders', './accounts-receivable/sales-receipts', './accounts-receivable/sales-invoices', './accounts-receivable/sales-order-form', './accounts-payable/purchase-orders', './accounts-payable/purchase-invoices', './inventory-and-control/items', './inventory-and-control/inventory', './financials/general-ledgers', './financials/journal-entries', './financials/taxes', './administration/setup', './administration/company'], function(exports_1) {
    var __decorate = (this && this.__decorate) || function (decorators, target, key, desc) {
        var c = arguments.length, r = c < 3 ? target : desc === null ? desc = Object.getOwnPropertyDescriptor(target, key) : desc, d;
        if (typeof Reflect === "object" && typeof Reflect.decorate === "function") r = Reflect.decorate(decorators, target, key, desc);
        else for (var i = decorators.length - 1; i >= 0; i--) if (d = decorators[i]) r = (c < 3 ? d(r) : c > 3 ? d(target, key, r) : d(target, key)) || r;
        return c > 3 && r && Object.defineProperty(target, key, r), r;
    };
    var __metadata = (this && this.__metadata) || function (k, v) {
        if (typeof Reflect === "object" && typeof Reflect.metadata === "function") return Reflect.metadata(k, v);
    };
    var core_1, router_1, dashboard_1, navigation_1, sales_quotations_1, sales_orders_1, sales_receipts_1, sales_invoices_1, sales_order_form_1, purchase_orders_1, purchase_invoices_1, items_1, inventory_1, general_ledgers_1, journal_entries_1, taxes_1, setup_1, company_1;
    var App;
    return {
        setters:[
            function (core_1_1) {
                core_1 = core_1_1;
            },
            function (router_1_1) {
                router_1 = router_1_1;
            },
            function (dashboard_1_1) {
                dashboard_1 = dashboard_1_1;
            },
            function (navigation_1_1) {
                navigation_1 = navigation_1_1;
            },
            function (sales_quotations_1_1) {
                sales_quotations_1 = sales_quotations_1_1;
            },
            function (sales_orders_1_1) {
                sales_orders_1 = sales_orders_1_1;
            },
            function (sales_receipts_1_1) {
                sales_receipts_1 = sales_receipts_1_1;
            },
            function (sales_invoices_1_1) {
                sales_invoices_1 = sales_invoices_1_1;
            },
            function (sales_order_form_1_1) {
                sales_order_form_1 = sales_order_form_1_1;
            },
            function (purchase_orders_1_1) {
                purchase_orders_1 = purchase_orders_1_1;
            },
            function (purchase_invoices_1_1) {
                purchase_invoices_1 = purchase_invoices_1_1;
            },
            function (items_1_1) {
                items_1 = items_1_1;
            },
            function (inventory_1_1) {
                inventory_1 = inventory_1_1;
            },
            function (general_ledgers_1_1) {
                general_ledgers_1 = general_ledgers_1_1;
            },
            function (journal_entries_1_1) {
                journal_entries_1 = journal_entries_1_1;
            },
            function (taxes_1_1) {
                taxes_1 = taxes_1_1;
            },
            function (setup_1_1) {
                setup_1 = setup_1_1;
            },
            function (company_1_1) {
                company_1 = company_1_1;
            }],
        execute: function() {
            App = (function () {
                function App() {
                }
                App = __decorate([
                    core_1.Component({
                        selector: 'app',
                        templateUrl: 'app/app.html',
                        directives: [navigation_1.NavigationComponent, router_1.RouterOutlet]
                    }),
                    router_1.RouteConfig([
                        { path: '/dashboard', component: dashboard_1.DashboardComponent, useAsDefault: true, name: 'Dashboard' },
                        { path: '/accountsreceivable/salesquotations', component: sales_quotations_1.SalesQuotationsComponent, name: 'Sales Quotations' },
                        { path: '/accountsreceivable/salesorders', component: sales_orders_1.SalesOrdersComponent, name: 'Sales Orders' },
                        { path: '/accountsreceivable/salesorders/form', component: sales_order_form_1.SalesOrderFormComponent, name: 'Sales Order Form' },
                        { path: '/accountsreceivable/salesreceipts', component: sales_receipts_1.SalesReceiptsComponent, name: 'Sales Receipts' },
                        { path: '/accountsreceivable/salesinvoices', component: sales_invoices_1.SalesInvoicesComponent, name: 'Sales Invoices' },
                        { path: '/accountspayable/purchaseorders', component: purchase_orders_1.PurchaseOrdersComponent, name: 'Purchase Orders' },
                        { path: '/accountspayable/purchaseinvoices', component: purchase_invoices_1.PurchaseInvoicesComponent, name: 'Purchase Invoices' },
                        { path: '/inventoryanditems/items', component: items_1.ItemsComponent, name: 'Items' },
                        { path: '/inventoryanditems/inventory', component: inventory_1.InventoryComponent, name: 'Inventory' },
                        { path: '/financials/generalledgers', component: general_ledgers_1.GeneralLedgersComponent, name: 'General Ledgers' },
                        { path: '/financials/journalentries', component: journal_entries_1.JournalEntriesComponent, name: 'Journal Entries' },
                        { path: '/financials/taxes', component: taxes_1.TaxesComponent, name: 'Taxes' },
                        { path: '/administration/setup', component: setup_1.SetupComponent, name: 'Company' },
                        { path: '/administration/company', component: company_1.CompanyComponent, name: 'Setup' },
                    ]), 
                    __metadata('design:paramtypes', [])
                ], App);
                return App;
            })();
            exports_1("App", App);
        }
    }
});
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
//# sourceMappingURL=app.js.map