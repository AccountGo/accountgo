System.register(['angular2/core'], function(exports_1, context_1) {
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
    var core_1;
    var SalesOrderFormComponent, SalesOrder, SalesOrderItem;
    return {
        setters:[
            function (core_1_1) {
                core_1 = core_1_1;
            }],
        execute: function() {
            let SalesOrderFormComponent = class SalesOrderFormComponent {
                constructor() {
                    this.salesOrder = new SalesOrder();
                    this.salesOrder = new SalesOrder();
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
            };
            SalesOrderFormComponent = __decorate([
                core_1.Component({
                    selector: 'sales-order-form',
                    templateUrl: 'app/accounts-receivable/sales-order-form.html'
                }), 
                __metadata('design:paramtypes', [])
            ], SalesOrderFormComponent);
            exports_1("SalesOrderFormComponent", SalesOrderFormComponent);
            class SalesOrder {
                constructor() {
                    this.items = new Array();
                }
            }
            exports_1("SalesOrder", SalesOrder);
            class SalesOrderItem {
            }
            exports_1("SalesOrderItem", SalesOrderItem);
        }
    }
});
//# sourceMappingURL=sales-order-form.js.map