# Journal Posting Rules — Auto-Post from Business Transactions

## Core Posting Service

Every business transaction calls this service. It creates the journal entry and lines.

```php
class AccountingEngine
{
    /**
     * Post a balanced journal entry. Rolls back if debits != credits.
     *
     * @param int    $franchiseId  Tenant ID
     * @param string $referenceType Source type (sale_invoice, payment, purchase, etc.)
     * @param int    $referenceId   Source record ID
     * @param string $narration     Human-readable description
     * @param array  $lines         [{account_id, debit, credit, narration}]
     * @param string $entryDate     Date of the transaction (YYYY-MM-DD)
     * @param int    $postedBy      User ID
     * @return int   Journal entry ID
     * @throws UnbalancedEntryException
     * @throws ClosedPeriodException
     */
    public function postEntry(
        int $franchiseId,
        string $referenceType,
        int $referenceId,
        string $narration,
        array $lines,
        string $entryDate,
        int $postedBy
    ): int {
        // 1. Validate period is open
        // 2. Validate SUM(debit) = SUM(credit)
        // 3. Insert journal_entries header
        // 4. Insert journal_entry_lines
        // 5. Update account_balances (if materialized)
        // 6. Return entry ID
    }
}
```

## Posting Rules by Transaction Type

### 1. Sales Invoice

**Trigger:** Sale invoice is created/finalized.

```
DR  1200 Accounts Receivable     {invoice_total}
CR  4000 Sales Revenue            {subtotal}
CR  2200 Tax Payable - VAT        {tax_amount}      (if tax applies)
```

**With discount:**

```
DR  1200 Accounts Receivable     {net_total}
DR  4020 Sales Discounts          {discount_amount}
CR  4000 Sales Revenue            {subtotal}
CR  2200 Tax Payable - VAT        {tax_amount}
```

**With inventory (COGS):** Posted simultaneously:

```
DR  5000 Cost of Goods Sold       {cost_of_items}
CR  1300 Inventory                 {cost_of_items}
```

### 2. Customer Payment (Receipt)

**Trigger:** Payment received from customer.

```
DR  1100 Bank Account             {payment_amount}   (or 1000 Cash)
CR  1200 Accounts Receivable      {payment_amount}
```

**Overpayment:** Credit goes to customer advance account.

### 3. Purchase / Supplier Invoice

**Trigger:** Purchase order received / supplier invoice recorded.

**Inventory purchase:**

```
DR  1300 Inventory                 {purchase_amount}
DR  {tax_receivable}               {tax_amount}       (if reclaimable)
CR  2000 Accounts Payable          {total_amount}
```

**Expense purchase (non-inventory):**

```
DR  6XXX {Expense Account}         {amount}
CR  2000 Accounts Payable          {total_amount}
```

### 4. Supplier Payment

**Trigger:** Payment made to supplier.

```
DR  2000 Accounts Payable          {payment_amount}
CR  1100 Bank Account              {payment_amount}
```

### 5. Direct Expense (Cash/Bank)

**Trigger:** Expense paid directly (no AP).

```
DR  6XXX {Expense Account}         {amount}
CR  1100 Bank Account              {amount}   (or 1000 Cash)
```

### 6. Inventory Adjustment

**Trigger:** Stock count variance, damage, expiry.

**Positive adjustment (found extra stock):**

```
DR  1300 Inventory                 {adjustment_value}
CR  4300 Other Income              {adjustment_value}
```

**Negative adjustment (loss/damage):**

```
DR  6940 Inventory Adjustment Loss {adjustment_value}
CR  1300 Inventory                 {adjustment_value}
```

### 7. Stock Transfer (Between Locations)

**Trigger:** Inventory moved between warehouses. No P&L impact.

```
DR  1300 Inventory (Destination)   {transfer_value}
CR  1300 Inventory (Source)         {transfer_value}
```

Uses sub-accounts per location (e.g., 1301 Warehouse A, 1302 Warehouse B).

### 8. Salary/Payroll

**Trigger:** Payroll processed.

```
DR  6000 Salary & Wages            {gross_salary}
CR  2220 Tax Payable - Payroll     {tax_withheld}
CR  1100 Bank Account              {net_salary}
```

### 9. Loan Received

```
DR  1100 Bank Account              {loan_amount}
CR  2500 Long-Term Loans           {loan_amount}
```

### 10. Loan Repayment

```
DR  2500 Long-Term Loans           {principal}
DR  6930 Interest Expense          {interest}
CR  1100 Bank Account              {total_payment}
```

### 11. Depreciation (Monthly)

```
DR  6700 Depreciation Expense      {monthly_amount}
CR  1510 Accumulated Depreciation  {monthly_amount}
```

### 12. Year-End Closing Entry

Close all revenue and expense accounts to Retained Earnings.

```
-- Close revenue accounts (debit to zero them out)
DR  4000 Sales Revenue             {total_revenue}
DR  4100 Service Revenue           {total_service}
CR  3300 Current Year Earnings     {total_income}

-- Close expense accounts (credit to zero them out)
DR  3300 Current Year Earnings     {total_expenses}
CR  5000 COGS                      {total_cogs}
CR  6000 Salaries                  {total_salaries}
CR  6XXX Other Expenses            {total_other}

-- Transfer net income to retained earnings
DR  3300 Current Year Earnings     {net_income}
CR  3100 Retained Earnings         {net_income}
```

## Balance Validation Stored Procedure

```sql
DELIMITER //
CREATE PROCEDURE sp_post_journal_entry(
    IN p_franchise_id INT,
    IN p_entry_date DATE,
    IN p_reference_type VARCHAR(50),
    IN p_reference_id INT,
    IN p_narration VARCHAR(500),
    IN p_posted_by INT,
    IN p_lines JSON
)
BEGIN
    DECLARE v_entry_id INT;
    DECLARE v_total_debit DECIMAL(15,2) DEFAULT 0;
    DECLARE v_total_credit DECIMAL(15,2) DEFAULT 0;

    -- Validate period is open
    IF NOT EXISTS (
        SELECT 1 FROM fiscal_periods
        WHERE franchise_id = p_franchise_id
        AND p_entry_date BETWEEN start_date AND end_date
        AND status = 'open'
    ) THEN
        SIGNAL SQLSTATE '45000'
        SET MESSAGE_TEXT = 'Cannot post to a closed fiscal period';
    END IF;

    START TRANSACTION;

    -- Insert header
    INSERT INTO journal_entries
        (franchise_id, entry_date, reference_type, reference_id,
         narration, posted_by, status)
    VALUES
        (p_franchise_id, p_entry_date, p_reference_type, p_reference_id,
         p_narration, p_posted_by, 'POSTED');

    SET v_entry_id = LAST_INSERT_ID();

    -- Insert lines from JSON and calculate totals
    INSERT INTO journal_entry_lines
        (journal_entry_id, account_id, debit_amount, credit_amount,
         narration, franchise_id)
    SELECT v_entry_id, jt.account_id, jt.debit_amount, jt.credit_amount,
           jt.narration, p_franchise_id
    FROM JSON_TABLE(p_lines, '$[*]' COLUMNS (
        account_id INT PATH '$.account_id',
        debit_amount DECIMAL(15,2) PATH '$.debit',
        credit_amount DECIMAL(15,2) PATH '$.credit',
        narration VARCHAR(255) PATH '$.narration'
    )) AS jt;

    -- Validate balance
    SELECT SUM(debit_amount), SUM(credit_amount)
    INTO v_total_debit, v_total_credit
    FROM journal_entry_lines WHERE journal_entry_id = v_entry_id;

    IF v_total_debit != v_total_credit THEN
        ROLLBACK;
        SIGNAL SQLSTATE '45000'
        SET MESSAGE_TEXT = 'Journal entry is unbalanced: debits do not equal credits';
    END IF;

    COMMIT;
    SELECT v_entry_id AS entry_id;
END //
DELIMITER ;
```

## Posting Service Integration Pattern

```php
// In SalesInvoiceService::finalize()
$lines = [
    ['account_id' => $arAccountId, 'debit' => $total, 'credit' => 0,
     'narration' => "AR for Invoice #{$invoice->number}"],
    ['account_id' => $revenueAccountId, 'debit' => 0, 'credit' => $subtotal,
     'narration' => "Revenue for Invoice #{$invoice->number}"],
];
if ($taxAmount > 0) {
    $lines[] = ['account_id' => $taxAccountId, 'debit' => 0,
                'credit' => $taxAmount, 'narration' => "VAT on Invoice #{$invoice->number}"];
}
$this->accountingEngine->postEntry(
    $invoice->franchise_id, 'sale_invoice', $invoice->id,
    "Sale Invoice #{$invoice->number} to {$customer->name}",
    $lines, $invoice->invoice_date, $currentUserId
);
```
