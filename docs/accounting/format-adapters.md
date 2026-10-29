# Chart of Accounts Format Adapters

**Related:** [COA import/export requirements](./coa-import-export-requirements.md) §7.7 · [P4 plan](./coa-import-p4-plan.md)

AccountGo imports COA structure through a **Detect → MapToCanonical → shared validate/commit** pipeline (`Api.Service.Import`).

| Format key | Display name | Installed | Notes |
|---|---|---|---|
| `accountgo-v1` | AccountGo Chart of Accounts (v1) | **Yes** | `# AccountGo-COA-v1` Structure CSV |
| `xero` | Xero Chart of Accounts | Stub | Detected; returns “adapter not installed” |
| `qbo` | QuickBooks Online Chart of Accounts | Stub | Detected; returns “adapter not installed” |
| `accountgo-snapshot-v1` | Snapshot | Rejected | Balances → conversion journal epic (§8) |

Until a foreign adapter is installed, convert exports to AccountGo Structure (download the sample template from `/financials/accounts`) or use Append/Upsert/Replace with an AccountGo-v1 file.

---

## Pipeline

1. **Registry** parses CSV and fails closed on Snapshot marker or any `*Balance*` header column.  
2. **Detect** walks adapters (`accountgo-v1` first, then stubs).  
3. If `IsInstalled == false`, return an actionable error pointing here.  
4. If installed, **MapToCanonical** emits `CoaCanonicalRow` list.  
5. **AccountService** runs Append / Upsert / Replace / Retire-missing validation and atomic commit unchanged.

Extension point: implement `ICoaFormatAdapter`, register in DI next to the stubs, set `IsInstalled = true`.

---

## AccountGo Structure (`# AccountGo-COA-v1`)

| Column | Required | Notes |
|---|---|---|
| `AccountCode` | Yes | Unique per company |
| `AccountName` | Yes | |
| `ParentAccountCode` | No | |
| `AccountClass` | Yes | `1`–`6` or Assets, Liabilities, Equity, Revenue, Expenses, Closing accounts |
| `Contra` / `Cash` | No | `TRUE`/`FALSE` |
| `Description` | No | |
| `Status` | No | `Active` / `Archived` |
| `DefaultTaxCode`, `Currency` | Reserved | Ignored with soft warning |

---

## Xero → AccountGo (documented map; stub only)

Typical Xero export headers: `*Code`, `*Name`, `*Type`, `*Tax Code`, `*Description`, …

| Xero column | AccountGo column | Notes |
|---|---|---|
| `*Code` / Code | `AccountCode` | Keep codes stable for later TB mapping |
| `*Name` / Name | `AccountName` | |
| (none) | `ParentAccountCode` | Xero has no deep hierarchy; leave blank or encode groups in naming |
| `*Type` | `AccountClass` | See class table below |
| | `Contra` | Infer from type (e.g. Depreciation, Tracking) when implementing |
| | `Cash` | Bank / Current Asset bank accounts |
| `*Description` | `Description` | |
| | `Status` | Archived Xero accounts → `Archived` |
| `*Tax Code` | `DefaultTaxCode` | Reserved; ignore in v1 |
| Balance columns | — | **Do not import**; use conversion journal |

### Xero account type → AccountGo class

| Xero type (examples) | AccountClass |
|---|---|
| Current Asset, Fixed Asset, Non-current Asset, Inventory | 1 Assets |
| Current Liability, Liability, Non-current Liability | 2 Liabilities |
| Equity | 3 Equity |
| Revenue, Sales, Other Income | 4 Revenue |
| Expense, Direct Costs, Overhead, Depreciation | 5 Expenses |
| (closing / historical special) | 6 Closing accounts |

---

## QuickBooks Online → AccountGo (documented map; stub only)

Typical QBO import columns: `Account Number`, `Account Name`, `Type`, `Detail Type`, optional `Currency`.

| QBO column | AccountGo column | Notes |
|---|---|---|
| Account Number / Number | `AccountCode` | Enable account numbers in QBO before export when possible |
| Account Name | `AccountName` | Subaccounts often encoded as `Parent:Child` in Name — split when implementing |
| Type | `AccountClass` | See class table below |
| Detail Type | Description / flags | Informational; map Bank → `Cash=TRUE` when implementing |
| Currency | `Currency` | Reserved; ignore in v1 |
| Balance | — | **Do not import** |

### QBO type → AccountGo class

| QBO Type (examples) | AccountClass |
|---|---|
| Bank, Other Current Asset, Fixed Asset, Other Asset, Accounts Receivable | 1 Assets |
| Accounts Payable, Credit Card, Other Current Liability, Long Term Liability | 2 Liabilities |
| Equity | 3 Equity |
| Income, Other Income | 4 Revenue |
| Cost of Goods Sold, Expense, Other Expense | 5 Expenses |

---

## Installing a future adapter

1. Implement `ICoaFormatAdapter` with `IsInstalled = true` and `MapToCanonical`.  
2. Register: `builder.Services.AddSingleton<ICoaFormatAdapter, YourAdapter>();`  
3. Add integration tests next to `ChartOfAccountsFormatAdapterTests`.  
4. Keep Snapshot/balance rejection in the registry; never write balances from COA import.
