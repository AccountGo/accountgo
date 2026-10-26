# Accounting, Bookkeeping, Costing, and ERP Finance Patterns

Use this reference when implementing accounting features in ERP, POS, healthcare billing, school fees, manufacturing, distribution, or subscription systems. It synthesises bookkeeping, financial accounting, management accounting, SAP/Infor-style ERP finance configuration, and practical SME controls.

## Accounting Model

- Treat source transactions as business events and journal entries as the financial record derived from them.
- Preserve the accounting equation: `Assets = Liabilities + Equity`; every posting must keep the equation in balance.
- Use accrual accounting where invoices, bills, payroll, tax, inventory, depreciation, accruals, and prepayments matter; cash-basis reporting can be a view for micro users but should not corrupt the ledger.
- Keep a clean separation between the general ledger and subledgers. AR, AP, inventory, fixed assets, payroll, tax, and bank/cash modules should reconcile to GL control accounts.
- Never delete posted records. Correct by reversal, credit note, debit note, adjustment journal, or write-off workflow with approval.
- Period close must lock prior periods, preserve reporting cut-off, and support reopening only under controlled permission and audit trail.

## Core Records and Controls

| Area | Required records | Controls |
|---|---|---|
| General ledger | Chart of accounts, journal headers, journal lines, fiscal periods, account balances | balanced entry validation, closed-period lock, journal approval, reversal reason |
| Accounts receivable | customers, invoices, receipts, credit notes, allocations, aging | customer statements, credit limits, doubtful debt/write-off approval |
| Accounts payable | suppliers, bills, payments, debit notes, allocations, aging | three-way match, payment approval, duplicate invoice detection |
| Inventory accounting | item cost layers, stock movements, COGS postings, shrinkage, write-offs | FIFO/weighted average policy, stock count variance approval, negative-stock prevention |
| Fixed assets | asset register, acquisition, depreciation, disposal, revaluation, impairment | depreciation schedule approval, asset custody, disposal authorization |
| Payroll | earnings, deductions, employer obligations, net pay, payroll journal | payroll approval, statutory deductions, employee-level audit trail |
| Tax | VAT/sales tax, WHT, PAYE, import duty, tax payable/receivable | tax point, exemption logic, return reconciliation, document evidence |
| Bank and cash | bank accounts, cashbooks, deposits, withdrawals, bank statement lines | bank reconciliation, cash count, maker-checker for payments |

## ERP Configuration Patterns

- Use enterprise structure deliberately: company, legal entity, branch, business area, cost centre, profit centre, segment, project, and location are different dimensions.
- Chart of accounts should support reporting, statutory compliance, management analysis, and subledger integration. Avoid one flat list that cannot produce statements or management reports.
- Posting rules should be configurable by transaction type, product/service, tax code, customer/supplier group, inventory category, location, and legal entity.
- Reconciliation accounts connect subledgers to the general ledger. Users should not post directly to AR/AP/inventory control accounts except through controlled adjustment workflows.
- Open-item management is required for AR, AP, clearing accounts, accruals, advances, deposits, and suspense accounts.
- Document types and number ranges should distinguish invoices, receipts, payments, journals, credit notes, debit notes, payroll, depreciation, tax, inventory, and bank entries.
- Multi-currency systems need transaction currency, functional currency, exchange-rate source, realized gain/loss, unrealized revaluation, and audit trail.
- ERP finance screens should support both business users and accountants: simple source-entry forms plus technical ledger, trial balance, reconciliation, and close views.

## Bookkeeping Workflows

### Sales to Cash

1. Quote/order confirms commercial terms.
2. Invoice posts AR and revenue, plus tax if applicable.
3. Receipt posts bank/cash and clears AR.
4. Credit note reverses revenue/tax/AR where goods are returned or invoice value is reduced.
5. Aging report shows current, 30, 60, 90, and overdue balances.
6. Write-off uses approval and posts bad debt expense against AR.

### Procure to Pay

1. Purchase order records commitment but normally does not post GL.
2. Goods receipt updates inventory or accrued liability depending on policy.
3. Supplier invoice posts expense/inventory and AP, matched to PO/receipt where applicable.
4. Payment clears AP and reduces bank/cash.
5. Supplier debit note or credit memo corrects overbilling, returns, or allowances.

### Inventory to COGS

1. Receive stock into inventory at cost.
2. Move stock through locations without changing total inventory value unless cost centre ownership changes.
3. On sale or consumption, post COGS/direct cost and credit inventory.
4. Stock count differences post to shrinkage/gain accounts with reason codes and approvals.
5. Slow-moving, obsolete, expired, damaged, or lost stock requires write-down/write-off treatment.

### Record to Report

1. Capture all source transactions for the period.
2. Reconcile bank, AR, AP, inventory, payroll, fixed assets, tax, suspense, and intercompany accounts.
3. Post accruals, prepayments, depreciation, provisions, revaluations, and correction journals.
4. Review trial balance and exception reports.
5. Close the period and produce income statement, balance sheet, cash flow, aging schedules, and management pack.

## Management and Cost Accounting

- Separate financial accounting from management accounting. Financial accounting proves the books; management accounting helps managers decide and control.
- Model costs as direct/indirect, fixed/variable, controllable/non-controllable, product/period, and relevant/irrelevant depending on the decision.
- Cost centres collect responsibility costs; profit centres measure revenue and cost responsibility; projects/jobs collect contract or assignment economics.
- Use standard costing for manufacturing, food processing, construction, and repeatable services where variance control matters.
- Standard cost variance should split at minimum into price/rate variance, usage/efficiency variance, volume variance, mix/yield variance where relevant, and overhead expenditure/absorption variance.
- Use marginal/contribution costing for pricing, break-even, make-or-buy, capacity, and special-order decisions.
- Use activity-based costing when overhead is material and traditional allocation distorts product/customer profitability.
- Budget reports should distinguish fixed-budget variance from flexible-budget variance so managers are not blamed for volume-driven costs.

## Financial Statements and Ratios

- Trial balance is the control report: total debits must equal total credits before financial statements are trusted.
- Income statement separates revenue, COGS/direct costs, gross profit, operating expenses, finance costs, tax, and net profit.
- Balance sheet separates current/non-current assets and liabilities, equity, retained earnings, and control-account balances.
- Cash flow should separate operating, investing, and financing flows. Profit is not cash.
- Core ratios: gross margin, operating margin, net margin, current ratio, quick ratio, inventory days, receivable days, payable days, debt service cover, interest cover, return on assets, and return on equity.

## Acceptance Tests

- Every posted transaction has balanced journal lines and source-document linkage.
- AR aging total equals the AR control account; AP aging total equals the AP control account.
- Inventory valuation equals inventory control accounts by item/location/category after allowed timing differences.
- Fixed asset register net book value equals asset cost less accumulated depreciation in GL.
- Bank statement reconciliation explains every difference between book and bank balance.
- No user can post to closed periods, delete posted entries, or approve their own high-risk transaction.
- Financial reports can be regenerated from source entries, not only from stored totals.
