import {makeObservable, observable} from 'mobx';
import axios from "axios";
import Config  from '../../Config';

export default class CommonStore {
    customers: string[] = [];
    paymentTerms: string[] = [];
    items: object[] = [];
    measurements: string[] = [];
    vendors: string[] = [];
    accounts: string[] = [];
    salesQuotationStatus: string[] = [];

    constructor() {
        this.loadCustomersLookup();
        this.loadPaymentTermsLookup();
        this.loadItemsLookup();
        this.loadMeasurementsLookup();
        this.loadVendorsLookup();
        this.loadAccountsLookup();
        this.loadQuotationStatusLookup();

        makeObservable(this, {
            customers: observable,
            paymentTerms: observable,
            items: observable,
            measurements: observable,
            vendors: observable,
            accounts: observable,
            salesQuotationStatus: observable,
        });
    }

    loadCustomersLookup() {
        const customers: string[] = this.customers;

        axios.get(Config.API_URL + "common/customers")
            .then(function (result) {
                const data = result.data;
                for (let i = 0; i < Object.keys(data).length; i++) {
                    customers.push(data[i]);
                }
            });
    }

    loadPaymentTermsLookup() {
        const paymentTerms: string[] = this.paymentTerms;
        axios.get(Config.API_URL + "common/paymentterms")
            .then(function (result) {
                const data = result.data;
                for (let i = 0; i < Object.keys(data).length; i++) {
                    paymentTerms.push(data[i]);
                }
            });
    }

    loadVendorsLookup() {
        const vendors: string[] = this.vendors;
        axios.get(Config.API_URL + "common/vendors")
            .then(function (result: any) {
                const data = result.data;
                for (let i = 0; i < Object.keys(data).length; i++) {
                    vendors.push(data[i]);
                }
            }.bind(this));
    }

    loadItemsLookup() {
        const items: object[] = this.items;
        axios.get(Config.API_URL + "common/items")
            .then(function (result: any) {
                const data = result.data;
                for (let i = 0; i < Object.keys(data).length; i++) {
                    items.push(data[i]);
                }
            });
    }

    loadMeasurementsLookup() {
        const measurements: string[] = this.measurements;
        axios.get(Config.API_URL + "common/measurements")
            .then(function (result: any) {
                const data = result.data;
                for (let i = 0; i < Object.keys(data).length; i++) {
                    measurements.push(data[i]);
                }
            });
    }

    loadVoucherTypesLookup() {
    }

    loadQuotationStatusLookup() {
        const quotationStatus: string[] = this.salesQuotationStatus;
        axios.get(Config.API_URL + "common/salesquotationstatus")
            .then(function (result: any) {
                const data = result.data;
                for (let i = 0; i < Object.keys(data).length; i++)
                {
                    quotationStatus.push(data[i]);
                }
            })
    }

    loadAccountsLookup() {
        const accounts: string[] = this.accounts; 
        axios.get(Config.API_URL + "common/postingaccounts")
            .then(function (result: any) {
                const data: string[] = result.data;
                for (let i = 0; i < Object.keys(data).length; i++) {
                    accounts.push(data[i]);
                }
            });
    }

    getApplicableTaxes(itemId: number, partyId: number) {
        const result = axios.get(Config.API_URL + "tax/gettax?itemId=" + itemId + "&partyId=" + partyId);
        result.then(function (result: any) {
            return result.data;
        });
    }

    getSalesLineTaxAmount(quantity: number, amount: number, discount: number, taxes: any) {
        let lineTaxTotal = 0;
        amount = (amount * quantity) - discount;
        taxes.map(function (tax: any) {
            lineTaxTotal = lineTaxTotal + (amount - (amount / (1 + (tax.rate / 100))));
        });
        return lineTaxTotal;
    }

    getPurhcaseLineTaxAmount(quantity: number, amount: number, discount: number, taxes: any) {
        let lineTaxTotal = 0;
        amount = (amount * quantity) - discount;
        taxes.map(function (tax: any) {
            lineTaxTotal = lineTaxTotal + (amount - (amount / (1 + (tax.rate / 100))));
        });
        return lineTaxTotal;
    }
}