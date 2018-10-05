"use strict";
var __decorate = (this && this.__decorate) || function (decorators, target, key, desc) {
    var c = arguments.length, r = c < 3 ? target : desc === null ? desc = Object.getOwnPropertyDescriptor(target, key) : desc, d;
    if (typeof Reflect === "object" && typeof Reflect.decorate === "function") r = Reflect.decorate(decorators, target, key, desc);
    else for (var i = decorators.length - 1; i >= 0; i--) if (d = decorators[i]) r = (c < 3 ? d(r) : c > 3 ? d(target, key, r) : d(target, key)) || r;
    return c > 3 && r && Object.defineProperty(target, key, r), r;
};
Object.defineProperty(exports, "__esModule", { value: true });
const mobx_1 = require("mobx");
const axios = require("axios");
const Config = require("Config");
class CommonStore {
    constructor() {
        this.customers = [];
        this.paymentTerms = [];
        this.items = [];
        this.measurements = [];
        this.vendors = [];
        this.accounts = [];
        this.salesQuotationStatus = [];
        this.loadCustomersLookup();
        this.loadPaymentTermsLookup();
        this.loadItemsLookup();
        this.loadMeasurementsLookup();
        this.loadVendorsLookup();
        this.loadAccountsLookup();
        this.loadQuotationStatusLookup();
    }
    loadCustomersLookup() {
        let customers = this.customers;
        axios.get(Config.apiUrl + "api/common/customers")
            .then(function (result) {
            const data = result.data;
            for (var i = 0; i < Object.keys(data).length; i++) {
                customers.push(data[i]);
            }
        });
    }
    loadPaymentTermsLookup() {
        let paymentTerms = this.paymentTerms;
        axios.get(Config.apiUrl + "api/common/paymentterms")
            .then(function (result) {
            const data = result.data;
            for (var i = 0; i < Object.keys(data).length; i++) {
                paymentTerms.push(data[i]);
            }
        });
    }
    loadVendorsLookup() {
        let vendors = this.vendors;
        axios.get(Config.apiUrl + "api/common/vendors")
            .then(function (result) {
            const data = result.data;
            for (var i = 0; i < Object.keys(data).length; i++) {
                vendors.push(data[i]);
            }
        }.bind(this));
    }
    loadItemsLookup() {
        let items = this.items;
        axios.get(Config.apiUrl + "api/common/items")
            .then(function (result) {
            const data = result.data;
            for (var i = 0; i < Object.keys(data).length; i++) {
                items.push(data[i]);
            }
        });
    }
    loadMeasurementsLookup() {
        let measurements = this.measurements;
        axios.get(Config.apiUrl + "api/common/measurements")
            .then(function (result) {
            const data = result.data;
            for (var i = 0; i < Object.keys(data).length; i++) {
                measurements.push(data[i]);
            }
        });
    }
    loadVoucherTypesLookup() {
    }
    loadQuotationStatusLookup() {
        let quotationStatus = this.salesQuotationStatus;
        axios.get(Config.apiUrl + "api/common/salesquotationstatus")
            .then(function (result) {
            const data = result.data;
            for (var i = 0; i < Object.keys(data).length; i++) {
                quotationStatus.push(data[i]);
            }
        });
    }
    loadAccountsLookup() {
        let accounts = this.accounts;
        axios.get(Config.apiUrl + "api/common/postingaccounts")
            .then(function (result) {
            const data = result.data;
            for (var i = 0; i < Object.keys(data).length; i++) {
                accounts.push(data[i]);
            }
        });
    }
    getApplicableTaxes(itemId, partyId) {
        var result = axios.get(Config.apiUrl + "api/tax/gettax?itemId=" + itemId + "&partyId=" + partyId);
        result.then(function (result) {
            return result.data;
        });
    }
    getSalesLineTaxAmount(quantity, amount, discount, taxes) {
        var lineTaxTotal = 0;
        amount = (amount * quantity) - discount;
        taxes.map(function (tax) {
            lineTaxTotal = lineTaxTotal + (amount - (amount / (1 + (tax.rate / 100))));
        });
        return lineTaxTotal;
    }
    getPurhcaseLineTaxAmount(quantity, amount, discount, taxes) {
        var lineTaxTotal = 0;
        amount = (amount * quantity) - discount;
        taxes.map(function (tax) {
            lineTaxTotal = lineTaxTotal + (amount - (amount / (1 + (tax.rate / 100))));
        });
        return lineTaxTotal;
    }
}
__decorate([
    mobx_1.observable
], CommonStore.prototype, "customers", void 0);
__decorate([
    mobx_1.observable
], CommonStore.prototype, "paymentTerms", void 0);
__decorate([
    mobx_1.observable
], CommonStore.prototype, "items", void 0);
__decorate([
    mobx_1.observable
], CommonStore.prototype, "measurements", void 0);
__decorate([
    mobx_1.observable
], CommonStore.prototype, "vendors", void 0);
__decorate([
    mobx_1.observable
], CommonStore.prototype, "accounts", void 0);
__decorate([
    mobx_1.observable
], CommonStore.prototype, "salesQuotationStatus", void 0);
exports.default = CommonStore;
//# sourceMappingURL=CommonStore.js.map