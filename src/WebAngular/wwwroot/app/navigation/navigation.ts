import {Component} from 'angular2/core';
import {Router} from 'angular2/router';
@Component({
    selector: 'navigation',
    templateUrl: 'app/navigation/navigation.html'
})
export class NavigationComponent {
    activeMenu: string = 'Dashboard';
    activeView: string = 'Dashboard';
    menus: Array<NavigationMenu> = new Array<NavigationMenu>();

    constructor(private router: Router) {
        var accountsPayable = new NavigationMenu('Accounts Receivable');
        accountsPayable.items.push(new NavigationMenu('Sales Quotations'));
        accountsPayable.items.push(new NavigationMenu('Sales Orders'));
        accountsPayable.items.push(new NavigationMenu('Sales Receipts'));
        accountsPayable.items.push(new NavigationMenu('Sales Invoices'));

        var accountsReceivable = new NavigationMenu('Accounts Payable');
        accountsReceivable.items.push(new NavigationMenu('Purchase Orders'));
        accountsReceivable.items.push(new NavigationMenu('Purchase Invoices'));

        var inventoryAndControl = new NavigationMenu('Inventory and Control')
        inventoryAndControl.items.push(new NavigationMenu('Items'));
        inventoryAndControl.items.push(new NavigationMenu('Inventory'));

        var financials = new NavigationMenu('Financials')
        financials.items.push(new NavigationMenu('Journal Entries'));
        financials.items.push(new NavigationMenu('General Ledgers'));
        financials.items.push(new NavigationMenu('Taxes'));

        var administration = new NavigationMenu('Administration')
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
        this.activeView = displayText
        this.router.navigate([displayText]);
    }
}

export class NavigationMenu {    
    items: Array<NavigationMenu> = new Array<NavigationMenu>();

    constructor(public displayText: string, public path: string = null) {

    }
}