# Database Schema Design — Complete Accounting Tables

## Schema Overview

```
accounts ─────────┐
                   ├──► journal_entry_lines ◄── journal_entries
account_balances ──┘         │                       │
                             │                       │
fiscal_periods ──────────────┘                       │
                                                     │
sale_invoices ──► (reference_type = 'sale_invoice') ─┘
payments ──────► (reference_type = 'payment') ───────┘
purchases ─────► (reference_type = 'purchase') ──────┘
expenses ──────► (reference_type = 'expense') ───────┘
```

## Core Accounting Tables

### accounts (Chart of Accounts)

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
    is_system TINYINT(1) DEFAULT 0,
    description TEXT DEFAULT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    UNIQUE KEY uk_franchise_code (franchise_id, code),
    INDEX idx_franchise_type (franchise_id, account_type),
    FOREIGN KEY (franchise_id) REFERENCES franchises(id),
    FOREIGN KEY (parent_id) REFERENCES accounts(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
```

### journal_entries (Header)

```sql
CREATE TABLE journal_entries (
    id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    franchise_id INT UNSIGNED NOT NULL,
    entry_number VARCHAR(20) NOT NULL,
    entry_date DATE NOT NULL,
    reference_type VARCHAR(50) NOT NULL
        COMMENT 'sale_invoice, payment, purchase, expense, adjustment, closing',
    reference_id INT UNSIGNED DEFAULT NULL,
    narration VARCHAR(500) NOT NULL,
    is_reversal TINYINT(1) DEFAULT 0,
    reversed_entry_id INT UNSIGNED DEFAULT NULL,
    status ENUM('DRAFT','POSTED','VOIDED') DEFAULT 'POSTED',
    void_reason VARCHAR(255) DEFAULT NULL,
    posted_by INT UNSIGNED NOT NULL,
    voided_by INT UNSIGNED DEFAULT NULL,
    voided_at TIMESTAMP NULL DEFAULT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY uk_franchise_entry_num (franchise_id, entry_number),
    INDEX idx_franchise_date (franchise_id, entry_date),
    INDEX idx_franchise_ref (franchise_id, reference_type, reference_id),
    INDEX idx_franchise_status (franchise_id, status),
    FOREIGN KEY (franchise_id) REFERENCES franchises(id),
    FOREIGN KEY (reversed_entry_id) REFERENCES journal_entries(id),
    FOREIGN KEY (posted_by) REFERENCES users(id),
    FOREIGN KEY (voided_by) REFERENCES users(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
```

### journal_entry_lines (Detail)

```sql
CREATE TABLE journal_entry_lines (
    id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    journal_entry_id INT UNSIGNED NOT NULL,
    account_id INT UNSIGNED NOT NULL,
    debit_amount DECIMAL(15,2) NOT NULL DEFAULT 0.00,
    credit_amount DECIMAL(15,2) NOT NULL DEFAULT 0.00,
    narration VARCHAR(255) DEFAULT NULL,
    franchise_id INT UNSIGNED NOT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_entry (journal_entry_id),
    INDEX idx_account (account_id),
    INDEX idx_franchise_account (franchise_id, account_id),
    FOREIGN KEY (journal_entry_id) REFERENCES journal_entries(id),
    FOREIGN KEY (account_id) REFERENCES accounts(id),
    FOREIGN KEY (franchise_id) REFERENCES franchises(id),
    CONSTRAINT chk_debit_or_credit CHECK (
        (debit_amount > 0 AND credit_amount = 0) OR
        (debit_amount = 0 AND credit_amount > 0)
    )
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
```

### fiscal_periods

```sql
CREATE TABLE fiscal_periods (
    id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    franchise_id INT UNSIGNED NOT NULL,
    period_name VARCHAR(50) NOT NULL COMMENT 'e.g., Jan 2026, Q1 2026',
    start_date DATE NOT NULL,
    end_date DATE NOT NULL,
    status ENUM('open','closed','locked') DEFAULT 'open',
    closed_by INT UNSIGNED DEFAULT NULL,
    closed_at TIMESTAMP NULL DEFAULT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY uk_franchise_period (franchise_id, start_date, end_date),
    INDEX idx_franchise_status (franchise_id, status),
    FOREIGN KEY (franchise_id) REFERENCES franchises(id),
    FOREIGN KEY (closed_by) REFERENCES users(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
```

### account_balances (Materialized for Performance)

```sql
CREATE TABLE account_balances (
    id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    franchise_id INT UNSIGNED NOT NULL,
    account_id INT UNSIGNED NOT NULL,
    period_id INT UNSIGNED NOT NULL,
    opening_balance DECIMAL(15,2) DEFAULT 0.00,
    debit_total DECIMAL(15,2) DEFAULT 0.00,
    credit_total DECIMAL(15,2) DEFAULT 0.00,
    closing_balance DECIMAL(15,2) DEFAULT 0.00,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    UNIQUE KEY uk_franchise_account_period (franchise_id, account_id, period_id),
    FOREIGN KEY (franchise_id) REFERENCES franchises(id),
    FOREIGN KEY (account_id) REFERENCES accounts(id),
    FOREIGN KEY (period_id) REFERENCES fiscal_periods(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
```

## Trigger: Auto-Generate Entry Number

```sql
DELIMITER //
CREATE TRIGGER trg_journal_entry_number
BEFORE INSERT ON journal_entries
FOR EACH ROW
BEGIN
    DECLARE v_next_num INT;
    SELECT COALESCE(MAX(
        CAST(SUBSTRING(entry_number, 4) AS UNSIGNED)
    ), 0) + 1
    INTO v_next_num
    FROM journal_entries
    WHERE franchise_id = NEW.franchise_id;

    SET NEW.entry_number = CONCAT('JE-', LPAD(v_next_num, 7, '0'));
END //
DELIMITER ;
```

## Trigger: Validate Balance on Insert

```sql
DELIMITER //
CREATE TRIGGER trg_validate_journal_balance
AFTER INSERT ON journal_entry_lines
FOR EACH ROW
BEGIN
    DECLARE v_debit_sum DECIMAL(15,2);
    DECLARE v_credit_sum DECIMAL(15,2);
    DECLARE v_line_count INT;

    SELECT SUM(debit_amount), SUM(credit_amount), COUNT(*)
    INTO v_debit_sum, v_credit_sum, v_line_count
    FROM journal_entry_lines
    WHERE journal_entry_id = NEW.journal_entry_id;

    -- Only validate when we have at least 2 lines (complete entry)
    -- Final validation happens in stored procedure
END //
DELIMITER ;
```

## Design Principles

### Money Columns

- **Always** use `DECIMAL(15,2)` — never FLOAT or DOUBLE
- Max value: 9,999,999,999,999.99 (13 trillion) — sufficient for any business
- Minimum precision: 2 decimal places (cents/pennies)
- For currencies with 3 decimals (e.g., KWD), use `DECIMAL(15,3)`

### Multi-Tenant Isolation

- Every table has `franchise_id` column
- Every query filters by `franchise_id`
- Composite indexes start with `franchise_id`
- Foreign keys don't cross tenant boundaries

### Audit Trail

- `created_at` on every table
- `posted_by` on journal entries
- `voided_by` and `voided_at` for voids
- Never delete rows — only status changes
- Optional `audit_log` table for all changes

### Performance Considerations

- `account_balances` table is a materialized cache
- Updated on each journal posting for fast report queries
- Full recalculation possible from `journal_entry_lines` (source of truth)
- Indexes on `(franchise_id, entry_date)` for date-range queries
- Indexes on `(franchise_id, account_id)` for ledger queries

### Entry Number Sequence

- Format: `JE-0000001`, `JE-0000002`, ...
- Per-tenant sequence (each tenant starts at 1)
- Gaps are acceptable (voided entries keep their number)
- Never reuse entry numbers

### Referential Integrity

- Journal entries link to source via `reference_type` + `reference_id`
- This is a polymorphic reference (not a FK) — source can be any table
- `reference_type` values: `sale_invoice`, `payment`, `purchase`, `expense`,
  `inventory_adjustment`, `salary`, `loan`, `depreciation`, `closing`, `manual`
- Reversals link via `reversed_entry_id` (proper FK)

### Period Management

- Periods are monthly by default (configurable)
- Open period: entries can be posted
- Closed period: no new entries (requires reopen to modify)
- Locked period: permanently closed (year-end)
- Year-end closing creates a closing entry moving income/expenses to equity
