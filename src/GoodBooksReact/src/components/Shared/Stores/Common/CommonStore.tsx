import {observable} from 'mobx';
import axios from "axios";
import Config  from '../../Config';

export default class CommonStore {
    @observable customers = [];
    @observable paymentTerms = [];
    @observable items = [];
    @observable measurements = [];
    @observable vendors = [];
    @observable accounts = [];
    @observable salesQuotationStatus = [];

    constructor() {
        this.loadCustomersLookup();
        this.loadPaymentTermsLookup();
        this.loadItemsLookup();
        this.loadMeasurementsLookup();
        this.loadVendorsLookup();
        this.loadAccountsLookup();
        this.loadQuotationStatusLookup();
    }

    loadCustomersLookup() {
        let customers: any[] = this.customers;

        axios.get(Config.API_URL + "common/customers")
            .then(function (result) {
                const data = result.data;
                for (var i = 0; i < Object.keys(data).length; i++) {
                    customers.push(data[i]);
                }
            });
    }

    loadPaymentTermsLookup() {
        let paymentTerms: any[] = this.paymentTerms;
        axios.get(Config.API_URL + "common/paymentterms")
            .then(function (result) {
                const data = result.data;
                for (var i = 0; i < Object.keys(data).length; i++) {
                    paymentTerms.push(data[i]);
                }
            });
    }

    loadVendorsLookup() {
        let vendors: any[] = this.vendors;
        axios.get(Config.API_URL + "common/vendors")
            .then(function (result: any) {
                const data = result.data;
                for (var i = 0; i < Object.keys(data).length; i++) {
                    vendors.push(data[i]);
                }
            }.bind(this));
    }

    loadItemsLookup() {
        let items: any[] = this.items;
        axios.get(Config.API_URL + "common/items")
            .then(function (result: any) {
                const data = result.data;
                for (var i = 0; i < Object.keys(data).length; i++) {
                    items.push(data[i]);
                }
            });
    }

    loadMeasurementsLookup() {
        let measurements: any[] = this.measurements;
        axios.get(Config.API_URL + "common/measurements")
            .then(function (result: any) {
                const data = result.data;
                for (var i = 0; i < Object.keys(data).length; i++) {
                    measurements.push(data[i]);
                }
            });
    }

    loadVoucherTypesLookup() {
    }

    loadQuotationStatusLookup() {
        let quotationStatus: any[] = this.salesQuotationStatus;
        axios.get(Config.API_URL + "common/salesquotationstatus")
            .then(function (result: any) {
                const data = result.data;
                for (var i = 0; i < Object.keys(data).length; i++)
                {
                    quotationStatus.push(data[i]);
                }
            })
    }

    loadAccountsLookup() {
        let accounts: any[] = this.accounts; 
        axios.get(Config.API_URL + "common/postingaccounts")
            .then(function (result: any) {
                const data: string[] = result.data;
                for (var i = 0; i < Object.keys(data).length; i++) {
                    accounts.push(data[i]);
                }
            });
    }

    getApplicableTaxes(itemId: number, partyId: number) {
        var result = axios.get(Config.API_URL + "tax/gettax?itemId=" + itemId + "&partyId=" + partyId);
        result.then(function (result: any) {
            return result.data;
        });
    }

    getSalesLineTaxAmount(quantity: number, amount: number, discount: number, taxes: any) {
        var lineTaxTotal = 0;
        amount = (amount * quantity) - discount;
        taxes.map(function (tax: any) {
            lineTaxTotal = lineTaxTotal + (amount - (amount / (1 + (tax.rate / 100))));
        });
        return lineTaxTotal;
    }

    getPurhcaseLineTaxAmount(quantity: number, amount: number, discount: number, taxes: any) {
        var lineTaxTotal = 0;
        amount = (amount * quantity) - discount;
        taxes.map(function (tax: any) {
            lineTaxTotal = lineTaxTotal + (amount - (amount / (1 + (tax.rate / 100))));
        });
        return lineTaxTotal;
    }
}