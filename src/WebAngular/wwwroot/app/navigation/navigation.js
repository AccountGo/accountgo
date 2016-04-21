System.register(['angular2/core', 'angular2/router'], function(exports_1, context_1) {
    "use strict";
    var __moduleName = context_1 && context_1.id;
    var __decorate = (this && this.__decorate) || function (decorators, target, key, desc) {
        var c = arguments.length, r = c < 3 ? target : desc === null ? desc = Object.getOwnPropertyDescriptor(target, key) : desc, d;
        if (typeof Reflect === "object" && typeof Reflect.decorate === "function") r = Reflect.decorate(decorators, target, key, desc);
        else for (var i = decorators.length - 1; i >= 0; i--) if (d = decorators[i]) r = (c < 3 ? d(r) : c > 3 ? d(target, key, r) : d(target, key)) || r;
        return c > 3 && r && Object.defineProperty(target, key, r), r;
    };
    var __metadata = (this && this.__metadata) || function (k, v) {
        if (typeof Reflect === "object" && typeof Reflect.metadata === "function") return Reflect.metadata(k, v);
    };
    var core_1, router_1;
    var NavigationComponent, NavigationMenu;
    return {
        setters:[
            function (core_1_1) {
                core_1 = core_1_1;
            },
            function (router_1_1) {
                router_1 = router_1_1;
            }],
        execute: function() {
            let NavigationComponent = class NavigationComponent {
                constructor(router) {
                    this.router = router;
                    this.activeMenu = 'Dashboard';
                    this.activeView = 'Dashboard';
                    this.menus = new Array();
                    var accountsPayable = new NavigationMenu('Accounts Receivable');
                    accountsPayable.items.push(new NavigationMenu('Sales Quotations'));
                    accountsPayable.items.push(new NavigationMenu('Sales Orders'));
                    accountsPayable.items.push(new NavigationMenu('Sales Receipts'));
                    accountsPayable.items.push(new NavigationMenu('Sales Invoices'));
                    var accountsReceivable = new NavigationMenu('Accounts Payable');
                    accountsReceivable.items.push(new NavigationMenu('Purchase Orders'));
                    accountsReceivable.items.push(new NavigationMenu('Purchase Invoices'));
                    var inventoryAndControl = new NavigationMenu('Inventory and Control');
                    inventoryAndControl.items.push(new NavigationMenu('Items'));
                    inventoryAndControl.items.push(new NavigationMenu('Inventory'));
                    var financials = new NavigationMenu('Financials');
                    financials.items.push(new NavigationMenu('Journal Entries'));
                    financials.items.push(new NavigationMenu('General Ledgers'));
                    financials.items.push(new NavigationMenu('Taxes'));
                    var administration = new NavigationMenu('Administration');
                    administration.items.push(new NavigationMenu('Company'));
                    administration.items.push(new NavigationMenu('Setup'));
                    this.menus.push(accountsPayable);
                    this.menus.push(accountsReceivable);
                    this.menus.push(inventoryAndControl);
                    this.menus.push(financials);
                    this.menus.push(administration);
                }
                setActiveMenu(displayText) {
                    this.activeMenu = displayText;
                }
                setActiveView(displayText) {
                    this.activeView = displayText;
                    this.router.navigate([displayText]);
                }
            };
            NavigationComponent = __decorate([
                core_1.Component({
                    selector: 'navigation',
                    templateUrl: 'app/navigation/navigation.html'
                }), 
                __metadata('design:paramtypes', [router_1.Router])
            ], NavigationComponent);
            exports_1("NavigationComponent", NavigationComponent);
            class NavigationMenu {
                constructor(displayText, path = null) {
                    this.displayText = displayText;
                    this.path = path;
                    this.items = new Array();
                }
            }
            exports_1("NavigationMenu", NavigationMenu);
        }
    }
});
//# sourceMappingURL=navigation.js.map