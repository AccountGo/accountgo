# Chart of Accounts — Templates and Setup

## Default COA Template

Seed this for every new tenant. Tenants can customize after creation.

### Assets (1000-1999)

| Code | Name | Sub-Type | Normal Balance |
|------|------|----------|----------------|
| 1000 | Cash on Hand | Current Asset | Debit |
| 1010 | Petty Cash | Current Asset | Debit |
| 1100 | Bank Account - Main | Current Asset | Debit |
| 1101 | Bank Account - Secondary | Current Asset | Debit |
| 1200 | Accounts Receivable | Current Asset | Debit |
| 1210 | Allowance for Doubtful Accounts | Current Asset (Contra) | Credit |
| 1300 | Inventory | Current Asset | Debit |
| 1310 | Raw Materials | Current Asset | Debit |
| 1320 | Work in Progress | Current Asset | Debit |
| 1330 | Finished Goods | Current Asset | Debit |
| 1400 | Prepaid Expenses | Current Asset | Debit |
| 1500 | Equipment | Fixed Asset | Debit |
| 1510 | Accumulated Depreciation - Equipment | Fixed Asset (Contra) | Credit |
| 1600 | Vehicles | Fixed Asset | Debit |
| 1610 | Accumulated Depreciation - Vehicles | Fixed Asset (Contra) | Credit |
| 1700 | Land & Buildings | Fixed Asset | Debit |
| 1710 | Accumulated Depreciation - Buildings | Fixed Asset (Contra) | Credit |

### Liabilities (2000-2999)

| Code | Name | Sub-Type | Normal Balance |
|------|------|----------|----------------|
| 2000 | Accounts Payable | Current Liability | Credit |
| 2100 | Accrued Expenses | Current Liability | Credit |
| 2200 | Tax Payable - VAT/Sales Tax | Current Liability | Credit |
| 2210 | Tax Payable - Income Tax | Current Liability | Credit |
| 2220 | Tax Payable - Payroll Tax | Current Liability | Credit |
| 2300 | Unearned Revenue | Current Liability | Credit |
| 2400 | Short-Term Loans | Current Liability | Credit |
| 2500 | Long-Term Loans | Long-Term Liability | Credit |
| 2600 | Mortgage Payable | Long-Term Liability | Credit |

### Equity (3000-3999)

| Code | Name | Sub-Type | Normal Balance |
|------|------|----------|----------------|
| 3000 | Owner's Equity / Capital | Equity | Credit |
| 3100 | Retained Earnings | Equity | Credit |
| 3200 | Owner's Drawings / Distributions | Equity (Contra) | Debit |
| 3300 | Current Year Earnings | Equity | Credit |

### Revenue (4000-4999)

| Code | Name | Sub-Type | Normal Balance |
|------|------|----------|----------------|
| 4000 | Sales Revenue | Operating Revenue | Credit |
| 4010 | Sales Returns & Allowances | Revenue (Contra) | Debit |
| 4020 | Sales Discounts | Revenue (Contra) | Debit |
| 4100 | Service Revenue | Operating Revenue | Credit |
| 4200 | Interest Income | Other Revenue | Credit |
| 4300 | Other Income | Other Revenue | Credit |

### Cost of Goods Sold (5000-5999)

| Code | Name | Sub-Type | Normal Balance |
|------|------|----------|----------------|
| 5000 | Cost of Goods Sold | COGS | Debit |
| 5100 | Direct Materials | COGS | Debit |
| 5200 | Direct Labor | COGS | Debit |
| 5300 | Manufacturing Overhead | COGS | Debit |
| 5400 | Purchase Returns & Allowances | COGS (Contra) | Credit |

### Expenses (6000-6999)

| Code | Name | Sub-Type | Normal Balance |
|------|------|----------|----------------|
| 6000 | Salary & Wages | Operating Expense | Debit |
| 6010 | Employee Benefits | Operating Expense | Debit |
| 6100 | Rent Expense | Operating Expense | Debit |
| 6200 | Utilities | Operating Expense | Debit |
| 6300 | Insurance | Operating Expense | Debit |
| 6400 | Office Supplies | Operating Expense | Debit |
| 6500 | Marketing & Advertising | Operating Expense | Debit |
| 6600 | Travel & Entertainment | Operating Expense | Debit |
| 6700 | Depreciation Expense | Operating Expense | Debit |
| 6800 | Professional Fees | Operating Expense | Debit |
| 6900 | Miscellaneous Expense | Operating Expense | Debit |
| 6910 | Bank Charges & Fees | Operating Expense | Debit |
| 6920 | Bad Debt Expense | Operating Expense | Debit |
| 6930 | Interest Expense | Financial Expense | Debit |
| 6940 | Inventory Adjustment (Loss) | Operating Expense | Debit |

## COA Database Schema

```sql
CREATE TABLE accounts (
    id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    franchise_id INT UNSIGNED NOT NULL,
    code VARCHAR(10) NOT NULL,
    name VARCHAR(100) NOT NULL,
    account_type ENUM('asset','liability','equity','revenue','cogs','expense') NOT NULL,
    sub_type VARCHAR(50) DEFAULT NULL,
    parent_id INT UNSIGNED DEFAULT NULL,
    normal_balance ENUM('debit','credit') NOT NULL,
    is_active TINYINT(1) DEFAULT 1,
    is_system TINYINT(1) DEFAULT 0, -- cannot delete system accounts
    description TEXT DEFAULT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    UNIQUE KEY uk_franchise_code (franchise_id, code),
    FOREIGN KEY (franchise_id) REFERENCES franchises(id),
    FOREIGN KEY (parent_id) REFERENCES accounts(id),
    INDEX idx_franchise_type (franchise_id, account_type)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
```

## COA Seeder Pattern

```php
// Called when a new tenant (franchise) is created
function seedChartOfAccounts(int $franchiseId, PDO $pdo): void {
    $defaultAccounts = [
        ['1000', 'Cash on Hand', 'asset', 'Current Asset', 'debit', 1],
        ['1100', 'Bank Account - Main', 'asset', 'Current Asset', 'debit', 1],
        ['1200', 'Accounts Receivable', 'asset', 'Current Asset', 'debit', 1],
        ['1300', 'Inventory', 'asset', 'Current Asset', 'debit', 1],
        ['2000', 'Accounts Payable', 'liability', 'Current Liability', 'credit', 1],
        ['2200', 'Tax Payable - VAT', 'liability', 'Current Liability', 'credit', 1],
        ['3000', 'Owner\'s Equity', 'equity', 'Equity', 'credit', 1],
        ['3100', 'Retained Earnings', 'equity', 'Equity', 'credit', 1],
        ['4000', 'Sales Revenue', 'revenue', 'Operating Revenue', 'credit', 1],
        ['5000', 'Cost of Goods Sold', 'cogs', 'COGS', 'debit', 1],
        ['6000', 'Salary & Wages', 'expense', 'Operating Expense', 'debit', 1],
        // ... add remaining accounts
    ];

    $stmt = $pdo->prepare(
        "INSERT INTO accounts (franchise_id, code, name, account_type,
         sub_type, normal_balance, is_system) VALUES (?, ?, ?, ?, ?, ?, ?)"
    );

    foreach ($defaultAccounts as $acct) {
        $stmt->execute([$franchiseId, ...$acct]);
    }
}
```

## Account Rules

1. **System accounts** (`is_system = 1`) cannot be deleted or deactivated
2. **Custom accounts** can be added by tenant within allowed code ranges
3. **Deactivated accounts** cannot receive new postings but retain history
4. **Parent-child** relationships enable sub-account grouping for reports
5. **Code uniqueness** enforced per tenant (different tenants can reuse codes)
