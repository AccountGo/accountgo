# Void & Reversal Patterns — Complete Mechanics

## Reversal Service

```php
class AccountingEngine
{
    /**
     * Void a posted journal entry by creating a reversing entry.
     * Never deletes the original — creates equal and opposite entry.
     */
    public function voidEntry(
        int $journalEntryId,
        int $franchiseId,
        int $voidedBy,
        string $reason
    ): int {
        // 1. Load original entry and lines
        // 2. Verify entry is POSTED (not already VOIDED)
        // 3. Create new entry with reversed debits/credits
        // 4. Mark original as VOIDED
        // 5. Link reversal to original (reversed_entry_id)
        // 6. Log void reason in audit trail
        // 7. Return reversal entry ID
    }
}
```

## Void Rules by Transaction Type

### Sale Invoice Void

**Prerequisites:** Check for dependent transactions first.

```
Has payments?
  YES → Cannot void invoice. Must void payments first.
  NO  → Proceed with void.

Has delivery orders?
  YES → Must reverse inventory movement (COGS reversal) alongside.
  NO  → Proceed with void.
```

**Steps:**

1. Check no unvoided payments reference this invoice
2. Void the AR/Revenue journal entry (reversing entry)
3. If COGS was posted, void the COGS/Inventory entry too
4. Mark sale invoice status = VOIDED
5. Update AR sub-ledger balance

**Reversing entries:**

```
Original:
  DR  AR              500
  CR  Revenue         450
  CR  Tax Payable      50

Reversal:
  DR  Revenue         450
  DR  Tax Payable      50
  CR  AR              500

COGS Original:
  DR  COGS            300
  CR  Inventory       300

COGS Reversal:
  DR  Inventory       300
  CR  COGS            300
```

### Payment Void

**Prerequisites:**

```
Payment already reconciled with bank statement?
  YES → Warn user. Require supervisor override.
  NO  → Proceed with void.
```

**Steps:**

1. Void the Cash/AR journal entry
2. Restore the invoice balance (mark invoice as unpaid/partially paid)
3. Mark payment record status = VOIDED

**Reversing entry:**

```
Original:
  DR  Bank            500
  CR  AR              500

Reversal:
  DR  AR              500
  CR  Bank            500
```

### Purchase Void

Same logic as sale invoice but for AP/Inventory.

```
Has supplier payments?
  YES → Must void payments first.
  NO  → Proceed.

Inventory already used/sold?
  YES → Warn. Stock will go negative. Require override.
  NO  → Proceed.
```

### Inventory Adjustment Void

```
Stock already sold (balance would go negative)?
  YES → Block void. Cannot reverse what was already consumed.
  NO  → Proceed with void.
```

### Expense Void

Simple reversal — no dependency checks usually needed.

```
Original:
  DR  Expense         200
  CR  Bank            200

Reversal:
  DR  Bank            200
  CR  Expense         200
```

## Partial Void (Credit Notes)

When a full void isn't appropriate (e.g., partial return), use a credit note pattern.

**Sale Credit Note (partial refund):**

```
DR  4010 Sales Returns           {return_amount}
DR  2200 Tax Payable              {return_tax}
CR  1200 Accounts Receivable      {return_total}
```

This reduces the customer balance without voiding the original invoice.

**Purchase Credit Note (supplier return):**

```
DR  2000 Accounts Payable         {return_total}
CR  1300 Inventory                 {return_amount}
CR  {Tax Receivable}               {return_tax}
```

## Cascade Dependency Checker

Before voiding any transaction, run this check:

```php
function checkVoidDependencies(string $type, int $id, int $franchiseId): array
{
    $blockers = [];

    switch ($type) {
        case 'sale_invoice':
            // Check for unvoided payments
            $payments = getPaymentsForInvoice($id, $franchiseId);
            if (!empty($payments)) {
                $blockers[] = [
                    'type' => 'payment',
                    'ids' => array_column($payments, 'id'),
                    'message' => 'Void these payments first'
                ];
            }
            // Check for delivery orders
            $deliveries = getDeliveriesForInvoice($id, $franchiseId);
            if (!empty($deliveries)) {
                $blockers[] = [
                    'type' => 'delivery',
                    'ids' => array_column($deliveries, 'id'),
                    'message' => 'Inventory will be reversed'
                ];
            }
            break;

        case 'purchase':
            // Check for supplier payments
            $payments = getSupplierPaymentsForPurchase($id, $franchiseId);
            if (!empty($payments)) {
                $blockers[] = [
                    'type' => 'supplier_payment',
                    'ids' => array_column($payments, 'id'),
                    'message' => 'Void supplier payments first'
                ];
            }
            break;
    }

    return $blockers; // Empty = safe to void
}
```

## Audit Trail for Voids

Every void creates an audit log entry:

```sql
INSERT INTO audit_log
    (franchise_id, action, entity_type, entity_id,
     performed_by, reason, details, created_at)
VALUES
    (?, 'VOID', 'sale_invoice', ?,
     ?, ?, JSON_OBJECT(
         'original_entry_id', ?,
         'reversal_entry_id', ?,
         'original_amount', ?
     ), NOW());
```

## Void Status Flow

```
Transaction created → DRAFT (optional)
                    → POSTED (journal entry created)
                    → VOIDED (reversal entry created)

Journal entry:      POSTED → VOIDED (never deleted)
Reversal entry:     POSTED (always stays POSTED, is_reversal = 1)
```

## Testing Void Scenarios

| Test Case | Expected Result |
|-----------|----------------|
| Void a simple sale invoice | AR and Revenue net to zero |
| Void invoice with COGS | AR, Revenue, COGS, Inventory all net to zero |
| Void invoice with payment | Blocked — must void payment first |
| Void payment then invoice | Both void successfully, balances restored |
| Void expense | Expense and bank net to zero |
| Void already-voided entry | Rejected — already voided |
| Void in closed period | Rejected — period is closed |
| Trial Balance after void | Still balances (DR = CR) |
| Partial credit note | Reduces balance without voiding original |
