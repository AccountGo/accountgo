﻿import {observable, extendObservable, action} from 'mobx';
import axios from "axios";
import { API_URL } from '../../Config/index';

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
        let customers = this.customers;

        axios.get(API_URL + "common/customers")
            .then(function (result) {
                const data = result.data;
                for (var i = 0; i < Object.keys(data).length; i++) {
                    customers.push(data[i]);
                }
            });
    }

    loadPaymentTermsLookup() {
        let paymentTerms = this.paymentTerms;
        axios.get(API_URL + "common/paymentterms")
            .then(function (result) {
                const data = result.data;
                for (var i = 0; i < Object.keys(data).length; i++) {
                    paymentTerms.push(data[i]);
                }
            });
    }

    loadVendorsLookup() {
        let vendors = this.vendors;
        axios.get(API_URL + "common/vendors")
            .then(function (result) {
                const data = result.data;
                for (var i = 0; i < Object.keys(data).length; i++) {
                    vendors.push(data[i]);
                }
            }.bind(this));
    }

    loadItemsLookup() {
        let items = this.items;
        axios.get(API_URL + "common/items")
            .then(function (result) {
                const data = result.data;
                for (var i = 0; i < Object.keys(data).length; i++) {
                    items.push(data[i]);
                }
            });
    }

    loadMeasurementsLookup() {
        let measurements = this.measurements;
        axios.get(API_URL + "common/measurements")
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
        axios.get(API_URL + "common/salesquotationstatus")
            .then(function (result) {
                const data = result.data;
                for (var i = 0; i < Object.keys(data).length; i++)
                {
                    quotationStatus.push(data[i]);
                }
            })
    }

    loadAccountsLookup() {
        let accounts = this.accounts;
        axios.get(API_URL + "common/postingaccounts")
            .then(function (result) {
                const data = result.data;
                for (var i = 0; i < Object.keys(data).length; i++) {
                    accounts.push(data[i]);
                }
            });
    }

    getApplicableTaxes(itemId: number, partyId: number) {
        var result = axios.get(API_URL + "tax/gettax?itemId=" + itemId + "&partyId=" + partyId);
        result.then(function (result) {
            return result.data;
        });
    }

    getSalesLineTaxAmount(quantity: number, amount: number, discount: number, taxes: any) {
        var lineTaxTotal = 0;
        amount = (amount * quantity) - discount;
        taxes.map(function (tax) {
            lineTaxTotal = lineTaxTotal + (amount - (amount / (1 + (tax.rate / 100))));
        });
        return lineTaxTotal;
    }

    getPurhcaseLineTaxAmount(quantity: number, amount: number, discount: number, taxes: any) {
        var lineTaxTotal = 0;
        amount = (amount * quantity) - discount;
        taxes.map(function (tax) {
            lineTaxTotal = lineTaxTotal + (amount - (amount / (1 + (tax.rate / 100))));
        });
        return lineTaxTotal;
    }
}