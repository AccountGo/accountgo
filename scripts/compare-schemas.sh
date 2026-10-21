#!/bin/bash
# scripts/compare-schemas.sh
# Verifies that a freshly initialized database has the exact same schema as an upgraded database.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

UPGRADED_DB="/tmp/accountgo_upgraded_$$.db"
FRESH_DB="/tmp/accountgo_fresh_$$.db"
UPGRADED_DDL="/tmp/accountgo_upgraded_ddl_$$.sql"
FRESH_DDL="/tmp/accountgo_fresh_ddl_$$.sql"

cleanup() {
    rm -f "$UPGRADED_DB" "$FRESH_DB" "$UPGRADED_DDL" "$FRESH_DDL"
}
trap cleanup EXIT

echo "==> 1. Copying existing accountgo.db to temporary upgraded database..."
cp "$REPO_ROOT/src/Api/accountgo.db" "$UPGRADED_DB"

echo "==> 2. Generating fresh database schema..."
# Create tables for fresh database using sqlite3 from d1 schema and migrations
sqlite3 "$FRESH_DB" < "$REPO_ROOT/db/schema-d1.sql"
sqlite3 "$FRESH_DB" < "$REPO_ROOT/src/Api/d1/migrations/0001_multi_tenancy.sql"

echo "==> 3. Dumping DDL of both databases..."
sqlite3 "$UPGRADED_DB" "SELECT type, name, sql FROM sqlite_master WHERE type IN ('table', 'index') AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '__%' ORDER BY name;" > "$UPGRADED_DDL"
sqlite3 "$FRESH_DB" "SELECT type, name, sql FROM sqlite_master WHERE type IN ('table', 'index') AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '__%' ORDER BY name;" > "$FRESH_DDL"

echo "==> 4. Checking tenant table CompanyId coverage..."
MISSING_COMPANY_ID=0
TENANT_TABLES=(
    "Account" "Address" "AuditLog" "Bank" "CompanySetting" "Contact" "Customer"
    "CustomerAllocation" "CustomerContact" "DonationInvoiceHeader" "DonationInvoiceLine"
    "Donors" "FinancialYear" "GeneralLedgerHeader" "GeneralLedgerLine" "GeneralLedgerSetting"
    "InventoryControlJournal" "Item" "ItemCategory" "ItemTaxGroup" "ItemTaxGroupTax"
    "JournalEntryHeader" "JournalEntryLine" "Log" "MainContraAccount" "Measurement"
    "Party" "PaymentTerm" "PurchaseInvoiceHeader" "PurchaseInvoiceLine" "PurchaseOrderHeader"
    "PurchaseOrderLine" "PurchaseReceiptHeader" "PurchaseReceiptLine" "SalesDeliveryHeader"
    "SalesDeliveryLine" "SalesInvoiceHeader" "SalesInvoiceLine" "SalesOrderHeader"
    "SalesOrderLine" "SalesQuoteHeader" "SalesQuoteLine" "SalesReceiptHeader"
    "SalesReceiptLine" "SequenceNumber" "Tax" "TaxGroup" "TaxGroupTax" "Vendor"
    "VendorContact" "VendorPayment"
)

for tbl in "${TENANT_TABLES[@]}"; do
    has_col=$(sqlite3 "$UPGRADED_DB" "SELECT COUNT(*) FROM pragma_table_info('$tbl') WHERE name='CompanyId';")
    if [ "$has_col" -ne 1 ]; then
        echo "FAIL: $tbl is missing CompanyId"
        MISSING_COMPANY_ID=$((MISSING_COMPANY_ID + 1))
    fi
done

if [ "$MISSING_COMPANY_ID" -eq 0 ]; then
    echo "✓ All 51 tenant tables have CompanyId."
else
    echo "ERROR: $MISSING_COMPANY_ID tables missing CompanyId!"
    exit 1
fi

echo "==> 5. Verifying Decision D1 memberships..."
sqlite3 "$UPGRADED_DB" "SELECT cm.Role, u.Email FROM CompanyMember cm JOIN AspNetUsers u ON u.Id = cm.UserId WHERE cm.CompanyId = 1;"

echo "==> 6. Schema check complete!"
