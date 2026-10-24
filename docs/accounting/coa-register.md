# Chart of Accounts & Auto-Posting Rules Register

**Project:** AccountGo ERP & Accounting Platform  
**Architecture Standard:** Double-Entry General Ledger Engine (`saas-accounting-system`)  
**Scope:** Multi-Tenant Enterprise General Ledger, Subledgers, and Control Accounts  

---

## 1. System Overview & Golden Rule

AccountGo implements a hidden double-entry accounting engine under the hood of everyday business transactions. Users record sales invoices, customer receipts, vendor bills, payments, and inventory transfers using intuitive business workflows. The system automatically converts each transaction into balanced general ledger lines.

> [!IMPORTANT]
> **The Golden Rule of Accounting:**
> Every general ledger transaction MUST produce balanced entries where:
> $$\sum \text{Debits} = \sum \text{Credits}$$
> This is strictly enforced in `FinancialHelper.DrCrEqualityValidated()` and `FinancialService.ValidateGeneralLedgerEntry()`. If an entry does not balance, the entire database transaction rolls back.

---

## 2. Chart of Accounts (COA) Structure

Accounts are structured across five standard GAAP/IFRS account classes with tenant isolation via `CompanyId`.

| Class ID | Account Class | Normal Balance | Code Range | AccountGo Purpose |
| :--- | :--- | :--- | :--- | :--- |
| **1** | **Assets** | Debit (`Dr`) | `1000 - 1999` | Cash, Bank, Accounts Receivable, Inventory, Fixed Assets |
| **2** | **Liabilities** | Credit (`Cr`) | `2000 - 2999` | Accounts Payable, Taxes Payable (VAT/GST), Loans, Accruals |
| **3** | **Equity** | Credit (`Cr`) | `3000 - 3999` | Retained Earnings, Owner's Capital, Common Stock |
| **4** | **Revenue** | Credit (`Cr`) | `4000 - 4999` | Operating Sales Revenue, Service Income, Interest Income |
| **5** | **Expense** | Debit (`Dr`) | `5000 - 6999` | Cost of Goods Sold (COGS), Operating Expenses, Payroll, Rent |

### Sub-Account Hierarchy & Posting Constraints
1. **Parent / Summary Accounts**: Accounts with child accounts (e.g., `1000 Current Assets`) serve as aggregation rollups. Direct posting to parent accounts is prohibited (`account.ChildAccounts.Count > 0` validation).
2. **Postable Detail Accounts**: Leaf accounts (e.g., `1010 Operating Cash`, `1200 Accounts Receivable`) receive all postings.
3. **Contra Accounts**: Flagged with `IsContraAccount = 1` (e.g., `1510 Accumulated Depreciation`, `4020 Sales Discounts`) and carry the opposite balance to their parent class.
4. **Tenant Isolation**: Every `Account` row has a mandatory `CompanyId` foreign key.

---

## 3. General Ledger Settings Mapping

AccountGo avoids hard-coded account IDs through configurable tenant defaults stored in `GeneralLedgerSetting`:

| Setting Key | Default Account Code | Description | Control Account |
| :--- | :--- | :--- | :--- |
| `AccountsReceivableId` | `1200` | Accounts Receivable control account | :white_check_mark: Yes |
| `AccountsPayableId` | `2000` | Accounts Payable control account | :white_check_mark: Yes |
| `SalesRevenueId` | `4000` | Primary goods and services revenue | No |
| `SalesTaxPayableId` | `2200` | Sales tax (VAT/GST) liability | :white_check_mark: Yes |
| `CostOfGoodsSoldId` | `5000` | Direct product costing on delivery | No |
| `InventoryAssetId` | `1300` | Inventory balance on hand | :white_check_mark: Yes |
| `RetainedEarningsId` | `3100` | Year-end closing accumulator | :white_check_mark: Yes |

---

## 4. Transaction-to-Journal Auto-Posting Rules

### 1. Sales Invoice (`DocumentTypes.SalesInvoice`)
**Trigger:** Sales order is billed and approved (`SalesService.SaveSalesInvoice`).

| Account | Side | Amount | Memo / Narration |
| :--- | :--- | :--- | :--- |
| `Accounts Receivable (1200)` | **Debit** | Invoice Total | Invoice amount owed by customer |
| `Sales Revenue (4000)` | **Credit** | Subtotal (Net of Tax) | Recognized operating sales |
| `Tax Payable (2200)` | **Credit** | Tax Amount | VAT/GST collected on behalf of tax authority |

$$\text{Debit (Total)} = \text{Credit (Subtotal)} + \text{Credit (Tax)}$$

---

### 2. Customer Receipt / Payment (`DocumentTypes.SalesReceipt`)
**Trigger:** Cash, check, or bank payment received from a customer (`SalesService.SaveSalesReceipt`).

| Account | Side | Amount | Memo / Narration |
| :--- | :--- | :--- | :--- |
| `Cash / Bank (1010 / 1100)` | **Debit** | Payment Amount | Inflow of liquid operating funds |
| `Accounts Receivable (1200)` | **Credit** | Payment Amount | Reduction in customer outstanding receivable |

---

### 3. Sales Delivery & COGS (`DocumentTypes.SalesDelivery`)
**Trigger:** Goods dispatched to customer from inventory (`SalesService.SaveSalesDelivery`).

| Account | Side | Amount | Memo / Narration |
| :--- | :--- | :--- | :--- |
| `Cost of Goods Sold (5000)` | **Debit** | Total Item Cost | Expense recognized at point of delivery |
| `Inventory (1300)` | **Credit** | Total Item Cost | Asset reduction from stock on hand |

### Stock-tracked catalog items

Issued invoices post COGS (50300) against Inventory (10800) at moving-average cost.
Stock and COGS are planned together and persisted only after the complete invoice entry passes validation.
Zero-quantity lines have no stock movement; payment retries cannot issue stock again.
Voids reverse every original line, including repeated lines for the same item.

`Item.StockValue` retains the exact cent value of the inventory ledger. Average cost is
derived from value and quantity at four decimal places; the final issue takes the remaining
value rather than leaving rounded cents behind. The Inventory page reconciles this exact value,
not quantity multiplied by a rounded display average. Back-dated documents do not re-cost
later movements: costing follows posting order.

Stock adjustments use `inventory.adjustments.post`, shared by the API and role editor.
D1 migration `0016_stock_value.sql` backfills exact valued movements (or the legacy
quantity/cost cache for unvalued history). `0017_stock_permission_cleanup.sql` preserves
existing custom-role adjustment grants while removing the duplicate permission.
Local SQLite upgrades and default-role seeding perform the equivalent conversions.

---

### 4. Vendor Bill / Purchase Invoice (`DocumentTypes.PurchaseInvoice`)
**Trigger:** Supplier invoice entered and matched to purchase order (`PurchasingService.SavePurchaseInvoice`).

| Account | Side | Amount | Memo / Narration |
| :--- | :--- | :--- | :--- |
| `Inventory / Expense (1300 / 6000)` | **Debit** | Bill Subtotal | Acquired asset or operating expense |
| `Input Tax / Tax Receivable` | **Debit** | Reclaimable Tax | Reclaimable VAT/GST paid to vendor |
| `Accounts Payable (2000)` | **Credit** | Total Bill Amount | Liability owed to supplier |

---

### 5. Supplier Payment (`DocumentTypes.PurchaseInvoicePayment`)
**Trigger:** Vendor payment executed from cash or bank account (`PurchasingService.SavePurchaseInvoicePayment`).

| Account | Side | Amount | Memo / Narration |
| :--- | :--- | :--- | :--- |
| `Accounts Payable (2000)` | **Debit** | Payment Amount | Reduction in supplier liability |
| `Cash / Bank (1010 / 1100)` | **Credit** | Payment Amount | Outflow of liquid operating funds |

---

## 5. Void & Reversal Mechanics

In compliance with professional accounting standards:
1. **Append-Only Immutability**: Posted `GeneralLedgerLine` entries are never modified, deleted, or purged.
2. **Reversing Journals**: To void or correct a transaction, a distinct reversal entry is created containing the identical accounts with inverted Dr/Cr sides.
3. **Net-Zero Audit Trail**: The original entry plus the reversal entry mathematically net to $0.00$, preserving an unbroken historical record for financial auditors.
