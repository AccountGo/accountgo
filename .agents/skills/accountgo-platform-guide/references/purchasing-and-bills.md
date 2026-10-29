# Purchasing, Vendors, Bills & Inventory Guide

This reference covers all user workflows under **Purchases & Bills** (`/org/{orgSlug}/purchases`) and **Products & services** → **Inventory** (`/org/{orgSlug}/items/inventory`).

## Module & Permission Requirements

- **Required Modules**: `purchases` (for Bills & Vendors); `inventory` (for Inventory stock tracking and adjustments).
- **Permissions** (`Owner` and `Admin` workspace roles have all permissions automatically; `Member` users need these granted via a Security Role):
  - `purchases.vendors.view` — View the **Vendors** tab and vendor detail pages.
  - `purchases.vendors.manage` — Create, edit, archive, restore, or delete vendors and vendor contacts.
  - `purchases.bills.view` — View the **Purchases & Bills** area and bill list/detail pages.
  - `purchases.bills.create` — Create and edit draft bills.
  - `purchases.bills.post` — Approve (post) bills and void approved bills.
  - `purchases.payments.create` — Record payments on approved bills.
  - `inventory.items.view` — View catalog items and inventory stock levels.
  - `inventory.adjustments.post` — Post stock quantity adjustments.

---

## 1. Creating & Managing Vendors (Suppliers)

You can create a new vendor in two ways:

### Method A: From the Vendors Page
1. In the left sidebar, click **Purchases & Bills** (`/org/{orgSlug}/purchases`).
2. Click the **Vendors** tab under the page header (`/org/{orgSlug}/purchases/vendors`).
   *(Requires `purchases.vendors.view` permission).*
3. Click the **New vendor** button in the top-right header (or **Add a vendor** in the empty state).
   *(Requires `purchases.vendors.manage` permission).*
4. In the **New vendor** dialog (`PartyFormDialog`), fill in the vendor details:
   - **Vendor / company name** *(Required, up to 200 characters)*.
   - **Email** *(Optional)*.
   - **Phone** *(Optional)*.
   - **Street address**, **City / state**, **Postal code**, **Country** *(Optional)*.
   - **Default payment terms** *(Optional dropdown)* — Defaults to **Workspace default**.
5. Click **Add vendor**. You will be navigated to the new vendor's detail page (`/org/{orgSlug}/purchases/vendors/{partyId}`).

### Method B: On-the-Fly While Entering a Bill
1. Go to **Purchases & Bills** → click **New bill** (`/org/{orgSlug}/purchases/new`).
2. In the **Vendor** section, click **New vendor** directly above the vendor field, **or** click **Select a vendor** to open the **Select a vendor** picker dialog and click **New vendor** in the footer.
3. Complete the **New vendor** modal and click **Add vendor**. The new vendor is immediately selected on your draft bill.

### Vendor Detail Page (`/org/{orgSlug}/purchases/vendors/{partyId}`)
- Shows summary tiles for **Outstanding** (unpaid on approved bills), **Overdue**, and **Paid to date**, along with all **Bills** for that vendor.
- Click **New bill** in the top-right header to start a bill pre-selected for this vendor (`/org/{orgSlug}/purchases/new?vendor={partyId}`).
- Manage **Contacts** (**+ Add contact**, **Set as Bill To**, **Set as Primary**, **Edit**, **Remove**), **Edit** vendor details, **Archive / Restore vendor**, or **Delete vendor** (deletion is only allowed if no bills have been recorded for the vendor).

---

## 2. Creating, Approving, Paying & Voiding Bills

### Navigating the Bills Page (`/org/{orgSlug}/purchases`)
- **KPI Summary Tiles**: **You owe** (unpaid on approved bills), **Overdue**, **Paid this month**, and **Drafts** (waiting to be approved).
- **Status Filters**: Filter by **All**, **Unpaid**, **Overdue**, **Paid**, **Draft**, or **Void**.
- **Search**: Search by vendor name, vendor invoice number, bill number, or reference number.

### Entering or Editing a Bill (`/org/{orgSlug}/purchases/new` or `/org/{orgSlug}/purchases/{billId}/edit`)
1. **Vendor** *(Required)*: Pick a vendor from the **Select a vendor** dialog or create one with **New vendor**.
2. **Bill Metadata**:
   - **Vendor Invoice #**: The invoice number assigned by the supplier.
   - **Reference #**: Optional internal reference or purchase order number.
   - **Bill Date** *(Required)*: Defaults to today.
   - **Payment Terms** & **Due Date**: Select payment terms or pick a due date.
3. **Line Items**:
   - Enter a description or pick an item from the catalog using the **Package icon** (or create a **New item** on the fly).
   - Select the **Expense / Asset Account** for each line (defaults to `50200` Purchase A/C; stock-tracked items automatically book to their inventory asset account).
   - Enter **Quantity**, **Unit Cost / Amount**, optional **Discount %**, and **Tax**.
4. **Save Draft vs. Save & Approve**:
   - **Save draft**: Saves the bill in `Draft` status so it can be edited later (`purchases.bills.create`).
   - **Save & approve**: Approves and posts the bill to the General Ledger (`purchases.bills.post`). Once approved, a bill cannot be edited — only paid, duplicated, or voided.

### Paying a Bill (`PayBillDialog`)
1. From the **Bills** list (`/org/{orgSlug}/purchases`) or the **Bill Detail** page (`/org/{orgSlug}/purchases/{billId}`), click **Pay bill** (or **Record payment**; requires `purchases.payments.create`).
2. Enter the **Payment amount**, **Payment date**, **Payment method / Bank account**, and optional **Reference**, then confirm.

### Voiding a Bill
- On an approved bill with no active payments blocking voiding, click **Void** (requires `purchases.bills.post`) and confirm.

---

## 3. Inventory & Stock Adjustments (`/org/{orgSlug}/items/inventory`)

Available when the workspace has the `inventory` module enabled.
1. In the left sidebar, click **Products & services** (`/org/{orgSlug}/items`).
2. Click the **Inventory** tab under the page header (`/org/{orgSlug}/items/inventory`).
3. View stock-tracked items, their **Quantity on Hand**, **Average Cost**, and **Total Inventory Value**, along with the stock movement history (receipts, sales deliveries, and manual adjustments).
4. To adjust stock quantities (requires `inventory.adjustments.post` permission):
   - Select the stock item and open the **Adjust stock** action.
   - Specify the new quantity or adjustment delta, unit cost, date, adjustment account, and reason/notes, then post the adjustment.
