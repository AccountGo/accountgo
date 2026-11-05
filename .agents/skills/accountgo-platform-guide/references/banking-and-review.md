# Banking, statements and reconciliation

## Access and routes

**Banking** is `/org/{orgSlug}/banking`, available with any of sales/purchases/financials plus `banking.accounts.view`. Managing accounts needs `banking.accounts.manage`; reconciliation needs `banking.reconcile` and financials. Check the actual action permission and entitlement before promising a write.

| Surface | Route | Additional conditions |
| --- | --- | --- |
| Accounts | `/banking` | Bank/cash account directory |
| Account register | `/banking/{bankId}` | Account-specific activity/actions |
| Transactions | `/banking/transactions` | Financials |
| Reconcile history | `/banking/reconcile` | Financials |
| Reconcile account | `/banking/{bankId}/reconcile` | Financials and reconcile authority |
| Existing reconciliation | `/banking/{bankId}/reconcile/{reconId}` | Same, plus reconciliation state |
| Import statement | `/banking/{bankId}/statement-import` | Statement import entitlement and management rights |
| Needs review | `/banking/needs-review` | Statement-import route gate; tab visibility uses integration `canReview` |

Statement import is permitted by financials, the hosted `banking.statement_import` feature, or community edition as implemented in `lib/entitlements.ts`. An import upgrade affordance can be visible even when import is unavailable. This is not evidence all invoicing plans include imports. Transactions/Reconcile tabs depend on financials; Needs review depends separately on integration capabilities.

## Add an account and establish its starting point

1. **Banking → Add bank account**. Supply account name/type, currency and applicable bank details. Configure receipt/payment defaults if useful. Respect bank-account allowance and any validation shown.
2. With financials, account setup supports a new posting account, linking an eligible posting account, or adopting an existing cash-history group. A cash-history step can appear before the form; do not promise a single universal dialog path.
3. Enter a supported opening balance and as-of date when needed. For adopting cash history the default as-of comes from books start, and backend guards prevent invalid history boundaries. Confirm existing GL cash before introducing another opening entry.
4. Save, open the register and verify account identity, currency and balance. **Active** / **Include archived** filters explain missing archived accounts.

Financial Rules & Banking in Settings links here through **Open Banking**. It does not contain the bank-account editor. An account marked done in Get books ready only proves a nonarchived account exists, not that its opening balance has been reconciled.

## Register and statement import

Account register actions can include **Record transaction**, **Reconcile**, **Import statement**, and **Check balance**; archived state, role, module and entitlement determine which are offered. Do not record another payment for an invoice/bill receipt already posted elsewhere.

For a supported statement file, open **Import statement** → **1. Choose a file** → resolve the displayed date/column/amount format mapping → **Preview** → inspect ready, invalid or duplicate rows → **Import … lines**. Current implementation has format-specific paths including CSV/spreadsheet and bank statement formats; use the live file picker/parser validation rather than claiming every bank export works.

Review rows and match to existing ledger candidates where appropriate. **Match existing ledger entry? → Confirm match** links an existing posting and does not create another payment. An imported row is not automatically a posted ledger transaction. Early rows before books start, excluded rows and matched rows have different available actions. Creating a linked bill draft does not approve/pay it or automatically match the statement row.

## Incoming review and integrations

**Needs review** consolidates incoming items with states such as needs review, draft linked, matched and excluded. A linked draft can still need accounting review. The Workspace **Integrations** settings section appears only with management capability; integration setup and deliveries are distinct from importing a file manually. Do not invent a bank OAuth connection wizard based on a generic “bank feeds” upgrade message.

## Reconcile

Use the account's **Reconcile** action or Banking's **Reconcile** tab when financials and reconciliation permission allow it. Work from the actual statement date/balance and select the corresponding recorded lines. Review the difference and validation before the displayed finish action. A zero difference and backend eligibility are required by the reconciliation flow; a matching balance alone is not proof every item is correctly categorized.

Keep reconciliation state separate from import completion, statement matching and document payment. Confirm the finished reconciliation/history and correct end balance before calling the workflow complete. Do not fabricate automatic adjustment or undo/reopen steps without inspecting current controls.
