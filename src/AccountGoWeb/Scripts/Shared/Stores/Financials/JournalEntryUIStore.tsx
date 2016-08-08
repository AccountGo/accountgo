import {observable, autorun, reaction} from 'mobx';
import * as mobx from "mobx";

import JournalEntryStore from "./JournalEntryStore";

export default class JournalEntryUIStore {    
    store;
    
    constructor(store: JournalEntryStore) {
        this.store = store; 
    }
}

