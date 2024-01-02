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

        axios.get(Config.API_URL + "api/common/customers")
            .then(function (result) {
                const data = result.data;
                for (let i = 0; i < Object.keys(data).length; i++) {
                    customers.push(data[i]);
                }
            });
    }

    loadPaymentTermsLookup() {
        const localPaymentTerms: string[] = this.paymentTerms;
        axios.get(Config.API_URL + "api/common/paymentterms")
            .then(function (result) {
                const data = result.data;
                for (let i = 0; i < Object.keys(data).length; i++) {
                    localPaymentTerms.push(data[i]);
                }
            });
    }
   
    loadVendorsLookup() {
        const localVendors: string[] = [];
        
        axios.get(Config.API_URL + "api/common/vendors")
        .then(response => {
            const data = response.data;
            for (let i = 0; i < Object.keys(data).length; i++) {
                localVendors.push(data[i]);
            }
            this.vendors = localVendors;
        })
        .catch(error => {
          console.error(error);
        });   
    }

    loadItemsLookup() {
        const localItems: object[] = [];
        
        axios.get(Config.API_URL + "api/common/items")
        .then(response => {
            const data = response.data;
            for (let i = 0; i < Object.keys(data).length; i++) {
                localItems.push(data[i]);
            }
            this.items = localItems;
        })
        .catch(error => {
          console.error(error);
        });   
    }

    loadMeasurementsLookup() {
        const measurements: string[] = [];
        axios.get(Config.API_URL + "api/common/measurements")
            .then(function (result) {
                const data = result.data;
                for (let i = 0; i < Object.keys(data).length; i++) {
                    measurements.push(data[i]);
                }
            });
    }

    loadQuotationStatusLookup() {
        const quotationStatus: string[] = [];
        axios.get(Config.API_URL + "api/common/salesquotationstatus")
            .then(function (result) {
                const data = result.data;
                for (let i = 0; i < Object.keys(data).length; i++)
                {
                    quotationStatus.push(data[i]);
                }
            })
    }

    loadAccountsLookup() {
        const accounts: string[] = []; 
        axios.get(Config.API_URL + "api/common/postingaccounts")
            .then(function (result) {
                const data: string[] = result.data;
                for (let i = 0; i < Object.keys(data).length; i++) {
                    accounts.push(data[i]);
                }
            });
    }

    getApplicableTaxes(itemId: number, partyId: number) {
        const result = axios.get(Config.API_URL + "tax/gettax?itemId=" + itemId + "&partyId=" + partyId);
        result.then(function (result) {
            return result.data;
        });
    }

    getSalesLineTaxAmount(quantity: number, amount: number, discount: number, taxes: Array<{ rate: number }>) {
        let lineTaxTotal = 0;
        amount = (amount * quantity) - discount;
        taxes.map(function (tax) {
            lineTaxTotal = lineTaxTotal + (amount - (amount / (1 + (tax.rate / 100))));
        });
        return lineTaxTotal;
    }

    getPurhcaseLineTaxAmount(quantity: number, amount: number, discount: number, taxes: Array<{ rate: number }>) {
        let lineTaxTotal = 0;
        amount = (amount * quantity) - discount;
        taxes.map(function (tax) {
            lineTaxTotal = lineTaxTotal + (amount - (amount / (1 + (tax.rate / 100))));
        });
        return lineTaxTotal;
    }
}