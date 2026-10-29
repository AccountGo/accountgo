# Sales, Clients & Invoicing Guide

This reference covers all user workflows under **Invoices & Sales** (`/org/{orgSlug}/sales`) and **Products & services** (`/org/{orgSlug}/items`).

## Module & Permission Requirements

- **Required Module**: `sales` (for Invoices & Clients); `sales` or `purchases` (for Products & services).
- **Permissions** (`Owner` and `Admin` workspace roles have all permissions automatically; `Member` users need these granted via a Security Role):
  - `sales.customers.view` — View the **Clients** tab and client detail pages.
  - `sales.customers.manage` — Create, edit, archive, restore, or delete clients and client contacts.
  - `sales.invoices.view` — View the **Invoices & Sales** area and invoice list/detail pages.
  - `sales.invoices.create` — Create and edit draft invoices.
  - `sales.invoices.post` — Issue (post) invoices and void issued invoices.
  - `sales.receipts.create` — Record customer payments against issued invoices.
  - `inventory.items.view` — View **Products & services** catalog items.
  - `inventory.items.manage` — Create, edit, archive, restore, or delete catalog items.

---

## 1. Creating a New Client (Customer)

In AccountGo's UI, customers are labeled **Clients**. You can create a new client in two ways:

### Method A: From the Clients Page
1. In the left sidebar, click **Invoices & Sales** (`/org/{orgSlug}/sales`).
2. Click the **Clients** tab under the page header (`/org/{orgSlug}/sales/clients`).
   *(Note: The **Clients** tab is only visible if you have the `sales.customers.view` permission).*
3. Click the **New client** button in the top-right header (or **Add a client** in the empty state).
   *(Requires `sales.customers.manage` permission).*
4. In the **New client** dialog, fill in the client details:
   - **Client / company name** *(Required, up to 200 characters)* — e.g., `Acme Corporation`.
   - **Email** *(Optional)* — e.g., `billing@acme.com`. Without an email address on the client or Bill To contact, invoices cannot be emailed from AccountGo.
   - **Phone** *(Optional)* — e.g., `+1 (555) 000-0000`.
   - **Street address** *(Optional)* — e.g., `123 Market St, Suite 400`.
   - **City / state** *(Optional)* — e.g., `New York, NY`.
   - **Postal code** *(Optional)* — e.g., `10001`.
   - **Country** *(Optional)* — e.g., `USA`.
   - **Default payment terms** *(Optional dropdown)* — Defaults to **Workspace default**, or choose a configured payment term (such as *Net 15*, *Net 30*, *Due on receipt*).
5. Click **Add client**. You will be taken directly to the new client's detail page (`/org/{orgSlug}/sales/clients/{partyId}`).

### Method B: On-the-Fly While Creating or Editing an Invoice
1. Go to **Invoices & Sales** → click **New Invoice** (`/org/{orgSlug}/sales/new`).
2. In the **Bill To (Client)** section (top-left of the invoice card), either:
   - Click the **New client** action link directly above the client box, **or**
   - Click **Select a client** to open the **Select a client** picker dialog, then click **New client** in the bottom footer of that dialog.
3. Complete the **New client** modal and click **Add client**. The newly created client is automatically selected on your invoice and their default payment terms are applied.

---

## 2. Managing Clients & Client Contacts

From **Invoices & Sales** → **Clients** (`/org/{orgSlug}/sales/clients`), click any client row to open their detail page (`/org/{orgSlug}/sales/clients/{partyId}`).

### Viewing Balances & Invoice History
- The detail page shows three summary tiles: **Outstanding** (unpaid on issued invoices), **Overdue** (past the due date), and **Paid to date** (payments received), followed by the **Invoices** table for that client.
- Click **New invoice** in the top-right header to start an invoice pre-filled for this client (`/org/{orgSlug}/sales/new?client={partyId}`).

### Managing Contact Persons
Under the **Contacts** card on a client's detail page:
1. Click **+ Add contact** (requires `sales.customers.manage`).
2. Enter the contact person's **First name**, **Last name**, **Email**, **Phone**, and optional address, then save.
3. Use the `...` menu on any contact row to:
   - **Set as Bill To** — Marks this contact as the recipient shown on invoices (`Attn:` line and email recipient).
   - **Set as Primary** — Marks this contact as the primary contact person.
   - **Edit** — Update contact details.
   - **Remove** — Unlink the contact (non-primary contacts only; issued invoices keep the details they were issued with).

### Editing, Archiving, or Deleting a Client
- **Edit**: Click **Edit** in the top-right of the client detail page (or **Edit client** inside the invoice editor). Changes apply to new invoices and drafts; already-issued invoices keep the snapshot they were issued with.
- **Archive / Restore**: Open the `...` menu in the top-right of the client detail page and select **Archive client** (or **Restore client**). Archived clients are hidden when picking a client on new invoices, and can be viewed by toggling the **Archived** filter pill on the **Clients** list page.
- **Delete**: Open the `...` menu and select **Delete client**. A client can **only** be deleted if nothing has been recorded for them yet (no invoices or transactions).

---

## 3. Creating, Issuing & Managing Sales Invoices

### Navigating the Invoices Page (`/org/{orgSlug}/sales`)
- **KPI Summary Tiles**: Displays **Total Outstanding**, **Overdue**, **Paid This Month**, and **Drafts**.
- **Status Filters**: Filter invoices by **All**, **Unpaid** (Open, Partially Paid, Overdue), **Overdue**, **Paid**, **Draft**, or **Void**.
- **Search**: Search by invoice number, client name, email, or reference/PO number.

### Creating or Editing an Invoice (`/org/{orgSlug}/sales/new` or `/org/{orgSlug}/sales/{invoiceId}/edit`)
1. **Bill To (Client)** *(Required)*: Select a client (and optionally choose a specific **Bill To contact** from the dropdown if the client has contact persons).
2. **Invoice Metadata**:
   - **Invoice #**: Leave blank to auto-assign the next sequence number when issued, or enter a custom number (up to 30 characters).
   - **Reference / PO #**: Optional customer purchase order or reference string.
   - **Issue Date** *(Required)*: Defaults to today.
   - **Payment Terms**: Select a payment term to auto-calculate the **Due Date**, or set a custom **Due Date** (must not be before the Issue Date).
3. **Line Items**:
   - Type a line description directly, or click the **Package icon** in the item field to open the **Products & services** picker dialog (you can also click **New item** from inside that picker if you have `inventory.items.manage`).
   - Set **Qty**, **Unit Price**, optional **Discount** (amount or percentage), and **Tax** (if taxes are configured).
   - In accounting-enabled workspaces (`financials` module active), you can also select the **Revenue Account** per line.
   - Click **+ Add Line** to add more rows.
4. **Shipping, Notes & Payment Instructions**:
   - Optionally enter **Shipping & Handling**, **Customer Notes**, and **Payment Instructions** (bank details, IBAN, etc.).
5. **Save or Issue**:
   - **Save Draft**: Saves the invoice in `Draft` status without posting to the General Ledger (`sales.invoices.create`).
   - **Issue Invoice**: Finalizes the invoice, assigns its invoice number, posts the double-entry journal to the General Ledger (if `financials` is active), and opens the **Send Invoice** dialog (`sales.invoices.post`).

### Emailing an Invoice (`SendInvoiceDialog`)
- When issuing an invoice, or by clicking **Send / Email** on an issued invoice's detail page (`/org/{orgSlug}/sales/{invoiceId}`), you can:
  - Email the invoice to the client (with a secure public view/PDF link and optional PDF attachment), or
  - Mark it as issued without sending an email if the customer already received it outside AccountGo.
- Because emailing a customer cannot be undone, AccountGo asks you to confirm by typing the invoice number before sending.

### Recording a Customer Payment
1. From the **Invoices** list (`/org/{orgSlug}/sales`) or the **Invoice Detail** page (`/org/{orgSlug}/sales/{invoiceId}`), click **Record Payment** (requires `sales.receipts.create`).
2. In the **Record Payment** modal, enter:
   - **Amount** (defaults to remaining balance due)
   - **Payment Date**
   - **Payment Method** (e.g., `Bank Transfer`, `Cash`, `Card`, `Check`)
   - **Reference** and **Notes** (optional)
3. Submit to record the receipt. The invoice status updates to **Partially Paid** or **Paid**.

### Voiding an Invoice
- From the **Invoices** list or **Invoice Detail** page, click **Void** on an issued invoice (requires `sales.invoices.post`) and confirm in the dialog. Void invoices are excluded from outstanding totals and their GL postings are reversed.

---

## 4. Products & Services Catalog (`/org/{orgSlug}/items`)

Shared between Sales and Purchases.
1. Click **Products & services** in the left sidebar (`/org/{orgSlug}/items`).
2. Click **New item** in the top-right header (requires `inventory.items.manage`).
3. In the **New item** modal (`ItemFormDialog`), configure:
   - **Name** *(Required)* — Product or service name.
   - **Code / SKU** *(Optional)* — Item code.
   - **Selling price** *(Shown when `sales` module is active)* — Default unit price on sales invoices.
   - **Sales account** *(Shown when `sales` module is active)* — Default revenue account (defaults to `40100` Sales if available).
   - **Purchase cost** *(Shown when `purchases` module is active)* — Default unit cost on vendor bills.
   - **Track stock** *(Toggle, available when `inventory` module is active)* — Enables inventory quantity and average cost tracking, with optional **Opening quantity** and **Opening unit cost**.
4. Click **Add item** (or **Save item** when editing). You can also archive, restore, or delete unused items from the Edit Item dialog.
