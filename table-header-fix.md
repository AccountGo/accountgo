# Table Header Visibility Fix

## Issue
In light mode, table column titles in Inventory Control >> Items (and other pages) were unreadable because the text and background were both black due to hardcoded `thead-dark` and `table-dark` classes.

## Root Cause
Multiple Blazor components use Bootstrap's `thead-dark` and `table-dark` classes which force dark backgrounds regardless of the current theme.

**Affected Components:**
- Components/Pages/Inventory/Items.razor
- Components/Pages/Contact/Contacts.razor
- Components/Pages/Donations/DonationInvoices.razor
- Components/Pages/Sales/Customers.razor (2 instances)
- Components/Pages/Purchasing/Vendors.razor
- Components/Pages/Donations/AddDonationInvoice.razor
- Components/Pages/Sales/SalesInvoice.razor
- Components/Pages/Sales/NewSalesOrder.razor
- Components/Pages/Quotations/AddSalesQuotation.razor
- Components/Pages/Quotations/Quotation.razor

## Solution
Added theme-aware CSS overrides in `utilities.css` to make `thead-dark` and `table-dark` classes adapt to the current theme.

### Changes Made

**File: `/src/AccountGoWeb/wwwroot/css/utilities.css`**

Added overrides:
```css
/* Table thead-dark - make theme-aware */
.dark-theme table thead.thead-dark th {
    background-color: #3a3f44 !important;
    color: #ffc107 !important;
    border-color: #5c6873 !important;
}

.light-theme table thead.thead-dark th {
    background-color: #f8f9fa !important;
    color: #23282c !important;
    border-color: #dee2e6 !important;
}

/* Table table-dark - make theme-aware */
.dark-theme table.table-dark,
.dark-theme table.table-dark th,
.dark-theme table.table-dark td {
    background-color: #2f353a !important;
    color: #e4e7ea !important;
    border-color: #5c6873 !important;
}

.light-theme table.table-dark,
.light-theme table.table-dark th,
.light-theme table.table-dark td {
    background-color: #fff !important;
    color: #23282c !important;
    border-color: #dee2e6 !important;
}
```

## Result

### Dark Mode
- Table headers: Dark background (#3a3f44), yellow text (#ffc107)
- Table body: Dark background (#2f353a), light gray text (#e4e7ea)

### Light Mode
- Table headers: Light gray background (#f8f9fa), dark text (#23282c)
- Table body: White background (#fff), dark text (#23282c)

## Verification
✅ All table headers now readable in both themes
✅ No code changes needed in individual components
✅ Global fix applies to all tables with `thead-dark` or `table-dark` classes
✅ Build successful: 0 errors, 0 warnings

## Files Modified
- `/src/AccountGoWeb/wwwroot/css/utilities.css` - Added theme-aware overrides

---

**Date:** June 25, 2026
**Status:** ✅ Complete
