import JournalEntryStore from "./JournalEntryStore";

export default class JournalEntryUIStore {    
    store;
    
    constructor(store: JournalEntryStore) {
        this.store = store; 
    }
}

