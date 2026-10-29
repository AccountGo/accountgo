import React from 'react';
import { SalesTabs } from '../components/SalesTabs';
import { PurchasesTabs } from '../components/PurchasesTabs';
import type { Bill, Client, Invoice, InvoiceStatus, PartyBase, Vendor } from '../types';

/**
 * Clients and vendors are both parties, so one set of pages and one form serve both. A role carries what differs:
 * the endpoints, permissions, paths and wording of Sales (clients and invoices) or Purchases (vendors and bills).
 */
export type PartyRole = 'client' | 'vendor';

/** A client's invoice or a vendor's bill, as the party's page lists it. */
export interface PartyDocumentRow {
  id: number;
  /** The number to show; null for a draft that has none yet. */
  number: string | null;
  status: InvoiceStatus;
  date: string;
  dueDate?: string | null;
  total: number;
  balanceDue: number;
}

export interface PartyRoleConfig {
  noun: string;
  Noun: string;
  plural: string;
  Plural: string;
  /** The document the party is on: invoice or bill. */
  document: string;
  documents: string;
  Document: string;
  api: {
    list: string;
    get: (id: string | number) => string;
    save: string;
    archive: (id: number, archived: boolean) => string;
    remove: (id: number) => string;
    documents: (id: string | number) => string;
    saveContact: (partyId: number) => string;
    setBillToContact: (partyId: number, contactId: number) => string;
    setPrimaryContact: (partyId: number, contactId: number) => string;
    removeContact: (partyId: number, contactId: number) => string;
  };
  viewPermission: string;
  managePermission: string;
  createDocumentPermission: string;
  listPath: (orgSlug: string) => string;
  detailPath: (orgSlug: string, id: number) => string;
  /** Where to start a new document for the party; null where documents can't be written yet. */
  newDocumentPath: ((orgSlug: string, id: number) => string) | null;
  /** A document's own page; null where there is none yet. */
  documentPath: ((orgSlug: string, documentId: number) => string) | null;
  lastDocumentDate: (party: PartyBase) => string | null | undefined;
  toDocumentRow: (document: any) => PartyDocumentRow;
  Tabs: React.FC<{ orgSlug: string }>;
  copy: {
    listSubtitle: string;
    emptyHint: string;
    archivedEmptyHint: string;
    archivedNotice: string;
    editNotice: string;
    namePlaceholder: string;
    emailPlaceholder: string;
    noEmail: string;
    /** The header subtitle for what is owed. */
    owes: (amount: string) => string;
    outstandingNote: string;
    paidNote: string;
  };
}

export const partyRoles: Record<PartyRole, PartyRoleConfig> = {
  client: {
    noun: 'client',
    Noun: 'Client',
    plural: 'clients',
    Plural: 'Clients',
    document: 'invoice',
    documents: 'invoices',
    Document: 'Invoice',
    api: {
      list: '/api/Sales/Clients',
      get: (id) => `/api/Sales/Client?id=${encodeURIComponent(id)}`,
      save: '/api/Sales/SaveClient',
      archive: (id, archived) => `/api/Sales/ArchiveClient?id=${id}&archived=${archived}`,
      remove: (id) => `/api/Sales/DeleteClient?id=${id}`,
      documents: (id) => `/api/Sales/SalesInvoices?customerId=${encodeURIComponent(id)}`,
      saveContact: (partyId) => `/api/Sales/ClientContact?clientId=${partyId}`,
      setBillToContact: (partyId, contactId) =>
        `/api/Sales/SetClientBillToContact?clientId=${partyId}&contactId=${contactId}`,
      setPrimaryContact: (partyId, contactId) =>
        `/api/Sales/SetClientPrimaryContact?clientId=${partyId}&contactId=${contactId}`,
      removeContact: (partyId, contactId) =>
        `/api/Sales/DeleteClientContact?clientId=${partyId}&contactId=${contactId}`,
    },
    viewPermission: 'sales.customers.view',
    managePermission: 'sales.customers.manage',
    createDocumentPermission: 'sales.invoices.create',
    listPath: (orgSlug) => `/org/${orgSlug}/sales/clients`,
    detailPath: (orgSlug, id) => `/org/${orgSlug}/sales/clients/${id}`,
    newDocumentPath: (orgSlug, id) => `/org/${orgSlug}/sales/new?client=${id}`,
    documentPath: (orgSlug, documentId) => `/org/${orgSlug}/sales/${documentId}`,
    lastDocumentDate: (party) => (party as Client).lastInvoiceDate,
    toDocumentRow: (invoice: Invoice) => ({
      id: invoice.id,
      number: invoice.no ? `#${invoice.no}` : null,
      status: invoice.status,
      date: invoice.invoiceDate,
      dueDate: invoice.dueDate,
      total: invoice.totalAmountAfterTax ?? invoice.amount,
      balanceDue: invoice.balanceDue,
    }),
    Tabs: SalesTabs,
    copy: {
      listSubtitle: 'The people and businesses you invoice, and what each one owes you.',
      emptyHint: 'Add the people and businesses you invoice. You can also add one while writing an invoice.',
      archivedEmptyHint: 'Clients you archive are kept here with their invoices, out of the way when you pick a client.',
      archivedNotice: "This client is archived: it isn't offered when you pick a client for an invoice. Its invoices are unchanged.",
      editNotice: 'Changes apply to new invoices and drafts. Invoices already issued keep the details they were issued with.',
      namePlaceholder: 'e.g. Acme Corporation',
      emailPlaceholder: 'billing@acme.com',
      noEmail: "None, so invoices can't be emailed",
      owes: (amount) => `Owes ${amount}`,
      outstandingNote: 'Unpaid on issued invoices',
      paidNote: 'Payments received',
    },
  },
  vendor: {
    noun: 'vendor',
    Noun: 'Vendor',
    plural: 'vendors',
    Plural: 'Vendors',
    document: 'bill',
    documents: 'bills',
    Document: 'Bill',
    api: {
      list: '/api/Purchasing/Vendors',
      get: (id) => `/api/Purchasing/Vendor?id=${encodeURIComponent(id)}`,
      save: '/api/Purchasing/SaveVendor',
      archive: (id, archived) => `/api/Purchasing/ArchiveVendor?id=${id}&archived=${archived}`,
      remove: (id) => `/api/Purchasing/DeleteVendor?id=${id}`,
      documents: (id) => `/api/Purchasing/Bills?vendorId=${encodeURIComponent(id)}`,
      saveContact: (partyId) => `/api/Purchasing/VendorContact?vendorId=${partyId}`,
      setBillToContact: (partyId, contactId) =>
        `/api/Purchasing/SetVendorBillToContact?vendorId=${partyId}&contactId=${contactId}`,
      setPrimaryContact: (partyId, contactId) =>
        `/api/Purchasing/SetVendorPrimaryContact?vendorId=${partyId}&contactId=${contactId}`,
      removeContact: (partyId, contactId) =>
        `/api/Purchasing/DeleteVendorContact?vendorId=${partyId}&contactId=${contactId}`,
    },
    viewPermission: 'purchases.vendors.view',
    managePermission: 'purchases.vendors.manage',
    createDocumentPermission: 'purchases.bills.create',
    listPath: (orgSlug) => `/org/${orgSlug}/purchases/vendors`,
    detailPath: (orgSlug, id) => `/org/${orgSlug}/purchases/vendors/${id}`,
    newDocumentPath: (orgSlug, id) => `/org/${orgSlug}/purchases/new?vendor=${id}`,
    documentPath: (orgSlug, documentId) => `/org/${orgSlug}/purchases/${documentId}`,
    lastDocumentDate: (party) => (party as Vendor).lastBillDate,
    toDocumentRow: (bill: Bill) => ({
      id: bill.id,
      // The vendor's own number is the one on the paper bill; the workspace's number is the fallback.
      number: bill.vendorInvoiceNo || (bill.no ? `#${bill.no}` : null),
      status: bill.status,
      date: bill.date,
      dueDate: bill.dueDate,
      total: bill.total,
      balanceDue: bill.balanceDue,
    }),
    Tabs: PurchasesTabs,
    copy: {
      listSubtitle: 'The businesses you buy from, and what you owe each one.',
      emptyHint: 'Add the suppliers and service providers whose bills you pay.',
      archivedEmptyHint: 'Vendors you archive are kept here with their bills, out of the way when you pick a vendor.',
      archivedNotice: "This vendor is archived: it isn't offered when you pick a vendor for a bill. Its bills are unchanged.",
      editNotice: 'Changes apply to new bills and drafts. Bills already approved keep the details they were approved with.',
      namePlaceholder: 'e.g. Northwind Supplies',
      emailPlaceholder: 'accounts@northwind.com',
      noEmail: 'None',
      owes: (amount) => `You owe ${amount}`,
      outstandingNote: 'Unpaid on approved bills',
      paidNote: 'Payments made',
    },
  },
};
