# Financial Statements — Report SQL and Formats

## Trial Balance

Shows all account balances. Total debits MUST equal total credits.

```sql
SELECT
    a.code,
    a.name,
    a.account_type,
    SUM(jl.debit_amount) AS total_debit,
    SUM(jl.credit_amount) AS total_credit,
    CASE
        WHEN a.normal_balance = 'debit'
            THEN SUM(jl.debit_amount) - SUM(jl.credit_amount)
        ELSE SUM(jl.credit_amount) - SUM(jl.debit_amount)
    END AS balance
FROM accounts a
LEFT JOIN journal_entry_lines jl ON jl.account_id = a.id
LEFT JOIN journal_entries je ON je.id = jl.journal_entry_id
    AND je.status = 'POSTED'
    AND je.entry_date BETWEEN :start_date AND :end_date
WHERE a.franchise_id = :franchise_id
    AND a.is_active = 1
GROUP BY a.id, a.code, a.name, a.account_type, a.normal_balance
HAVING total_debit > 0 OR total_credit > 0
ORDER BY a.code;
```

**Validation:** Last row must show `SUM(total_debit) = SUM(total_credit)`.

## Balance Sheet (Statement of Financial Position)

**Formula:** Assets = Liabilities + Equity

```sql
-- Assets
SELECT a.code, a.name,
    CASE WHEN a.normal_balance = 'debit'
        THEN SUM(jl.debit_amount) - SUM(jl.credit_amount)
        ELSE SUM(jl.credit_amount) - SUM(jl.debit_amount)
    END AS balance
FROM accounts a
JOIN journal_entry_lines jl ON jl.account_id = a.id
JOIN journal_entries je ON je.id = jl.journal_entry_id
    AND je.status = 'POSTED' AND je.entry_date <= :as_of_date
WHERE a.franchise_id = :franchise_id AND a.account_type = 'asset'
GROUP BY a.id ORDER BY a.code;

-- Liabilities (same query, account_type = 'liability')
-- Equity (same query, account_type = 'equity')
-- Add Current Year Earnings from Income Statement to Equity section
```

**Display format (user-friendly):**

```
BALANCE SHEET as of {date}
──────────────────────────────────────
ASSETS
  Cash & Bank                  25,000
  Accounts Receivable          15,000
  Inventory                    30,000
  Equipment (net)              20,000
                              ────────
  TOTAL ASSETS                 90,000

LIABILITIES
  Accounts Payable             10,000
  Tax Payable                   5,000
  Loans                        25,000
                              ────────
  TOTAL LIABILITIES            40,000

EQUITY
  Owner's Capital              30,000
  Retained Earnings            12,000
  Current Year Earnings         8,000
                              ────────
  TOTAL EQUITY                 50,000

TOTAL LIABILITIES + EQUITY     90,000
══════════════════════════════════════
```

## Income Statement (Profit & Loss)

**Formula:** Revenue - COGS - Expenses = Net Income

```sql
-- Revenue
SELECT a.code, a.name,
    SUM(jl.credit_amount) - SUM(jl.debit_amount) AS balance
FROM accounts a
JOIN journal_entry_lines jl ON jl.account_id = a.id
JOIN journal_entries je ON je.id = jl.journal_entry_id
    AND je.status = 'POSTED'
    AND je.entry_date BETWEEN :start_date AND :end_date
WHERE a.franchise_id = :franchise_id AND a.account_type = 'revenue'
GROUP BY a.id ORDER BY a.code;

-- COGS (same, account_type = 'cogs', balance = debit - credit)
-- Expenses (same, account_type = 'expense', balance = debit - credit)
```

**Display format:**

```
INCOME STATEMENT for period {start} to {end}
──────────────────────────────────────
REVENUE
  Sales Revenue               100,000
  Service Revenue              20,000
  Less: Sales Returns          (2,000)
                              ────────
  NET REVENUE                 118,000

COST OF GOODS SOLD
  Cost of Goods Sold           60,000
                              ────────
  GROSS PROFIT                 58,000

OPERATING EXPENSES
  Salaries & Wages             20,000
  Rent                          5,000
  Utilities                     2,000
  Marketing                     3,000
  Depreciation                  1,500
  Other Expenses                1,500
                              ────────
  TOTAL EXPENSES               33,000

NET INCOME                     25,000
══════════════════════════════════════
```

## User-Friendly Profit & Loss (Simplified)

For non-accountants — no account codes, color-coded, plain language.

```
HOW YOUR BUSINESS IS DOING ({month})
──────────────────────────────────────
Money Coming In               118,000
Cost of Products Sold          60,000
                              ────────
What You Keep (Gross)          58,000

Running Costs
  Staff                        20,000
  Rent & Utilities              7,000
  Marketing                     3,000
  Other                         3,000
                              ────────
Total Running Costs            33,000

YOUR PROFIT THIS MONTH         25,000  ✅
══════════════════════════════════════
```

## General Ledger Detail

Shows every transaction for a specific account.

```sql
SELECT
    je.entry_date,
    je.reference_type,
    je.reference_id,
    je.narration,
    jl.debit_amount,
    jl.credit_amount,
    SUM(jl.debit_amount - jl.credit_amount) OVER (
        ORDER BY je.entry_date, je.id
    ) AS running_balance
FROM journal_entry_lines jl
JOIN journal_entries je ON je.id = jl.journal_entry_id
WHERE jl.account_id = :account_id
    AND jl.franchise_id = :franchise_id
    AND je.status = 'POSTED'
    AND je.entry_date BETWEEN :start_date AND :end_date
ORDER BY je.entry_date, je.id;
```

## Aged Receivables

```sql
SELECT
    c.name AS customer,
    SUM(CASE WHEN DATEDIFF(CURDATE(), si.due_date) <= 0
        THEN si.balance_due ELSE 0 END) AS current_amount,
    SUM(CASE WHEN DATEDIFF(CURDATE(), si.due_date) BETWEEN 1 AND 30
        THEN si.balance_due ELSE 0 END) AS days_1_30,
    SUM(CASE WHEN DATEDIFF(CURDATE(), si.due_date) BETWEEN 31 AND 60
        THEN si.balance_due ELSE 0 END) AS days_31_60,
    SUM(CASE WHEN DATEDIFF(CURDATE(), si.due_date) BETWEEN 61 AND 90
        THEN si.balance_due ELSE 0 END) AS days_61_90,
    SUM(CASE WHEN DATEDIFF(CURDATE(), si.due_date) > 90
        THEN si.balance_due ELSE 0 END) AS over_90,
    SUM(si.balance_due) AS total
FROM sale_invoices si
JOIN customers c ON c.id = si.customer_id
WHERE si.franchise_id = :franchise_id
    AND si.status IN ('POSTED', 'PARTIALLY_PAID')
    AND si.balance_due > 0
GROUP BY c.id, c.name
ORDER BY total DESC;
```

## Cash Flow Statement (Simplified)

```
CASH FLOW STATEMENT for period {start} to {end}
──────────────────────────────────────
OPERATING ACTIVITIES
  Cash from customers          95,000
  Cash to suppliers           (55,000)
  Cash to employees           (20,000)
  Other operating              (5,000)
                              ────────
  Net Operating Cash           15,000

INVESTING ACTIVITIES
  Equipment purchased         (10,000)
                              ────────
  Net Investing Cash          (10,000)

FINANCING ACTIVITIES
  Loan received                20,000
  Loan repayment               (5,000)
  Owner's drawings             (3,000)
                              ────────
  Net Financing Cash           12,000

NET CHANGE IN CASH             17,000
OPENING CASH BALANCE            8,000
CLOSING CASH BALANCE           25,000
══════════════════════════════════════
```

## Report Access Control

| Report | Non-Accountant | Accountant | Admin |
|--------|:-------------:|:----------:|:-----:|
| Simple P&L | Yes | Yes | Yes |
| Sales Summary | Yes | Yes | Yes |
| Outstanding Invoices | Yes | Yes | Yes |
| Cash Position | Yes | Yes | Yes |
| Trial Balance | No | Yes | Yes |
| Full Balance Sheet | No | Yes | Yes |
| Full Income Statement | No | Yes | Yes |
| General Ledger | No | Yes | Yes |
| Journal Register | No | Yes | Yes |
| Audit Trail | No | No | Yes |

Control via RBAC permissions from `vibe-security-skill` skill.
