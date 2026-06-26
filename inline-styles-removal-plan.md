# Inline Styles Removal Plan

## Overview
Remove all inline styles from AccountGoWeb project and replace with theme-aware CSS classes to maintain dark/light mode compatibility.

## Files with Inline Styles

### Razor Views (20 files)
1. Home/Index.cshtml
2. Financials/TrialBalance.cshtml
3. Financials/AccountsPrev.cshtml  
4. Financials/JournalEntries.cshtml
5. Financials/GeneralLedger.cshtml
6. Financials/BalanceSheet.cshtml
7. Financials/IncomeStatement.cshtml
8. Sales/DonationInvoices.cshtml
9. Sales/SalesInvoicePdf.cshtml
10. Tax/Taxes.cshtml
11. Shared/_Layout.cshtml
12. Shared/_LayoutPrev.cshtml
13. Dashboard/MonthlySales.cshtml
14. Audit/GetAuditableEntities.cshtml
15. Audit/GetAuditableAttributes.cshtml
16. Administration/AuditLogs.cshtml
17. Administration/Groups.cshtml
18. Administration/Users.cshtml
19. Administration/Roles.cshtml
20. Quotations/Quotations.cshtml

### Blazor Components (40+ files)
- All components in Components/Pages have hardcoded `bg-dark text-white` classes

## Common Inline Style Patterns

### 1. Text Alignment
```html
<!-- Before -->
<div style="text-align: center">
<td style="text-align: right">

<!-- After -->
<div class="text-center">
<td class="text-right report-amount">
```

### 2. Font Weight
```html
<!-- Before -->
<tr style="font-weight: bold;">

<!-- After -->
<tr class="report-header-row">
```

### 3. AG Grid Height
```html
<!-- Before -->
<div id="accounts" class="ag-blue" style="height:600px;">

<!-- After -->
<div id="accounts" class="ag-grid-container ag-theme-blue h-600">
```

### 4. Logo Width
```html
<!-- Before -->
<img src="..." style="width: 50%;" />

<!-- After -->
<img src="..." class="logo-main" />
```

### 5. Clear Float
```html
<!-- Before -->
<div style="clear: both">

<!-- After -->
<div class="clear-both">
```

### 6. Hardcoded Dark Mode Classes (Blazor)
```razor
<!-- Before -->
<InputSelect class="form-control bg-dark text-white border-secondary">

<!-- After -->
<InputSelect class="form-control form-control-themed border-secondary-themed">
```

## Created Utility Classes

### File: `/src/AccountGoWeb/wwwroot/css/utilities.css`

**Layout Utilities:**
- `.text-center`, `.text-right`, `.text-left`
- `.fw-bold`, `.fw-normal`
- `.clear-both`
- `.w-50`, `.w-100`
- `.h-400`, `.h-500`, `.h-600`

**Form Controls (theme-aware):**
- `.form-control-themed` - Replaces `bg-dark text-white`
- `.form-select-themed` - Replaces `bg-dark text-white` on selects
- `.border-secondary-themed` - Theme-aware border

**Financial Reports:**
- `.report-header-row` - Bold header rows (yellow in dark mode)
- `.report-total-row` - Bold total rows (yellow in dark mode)  
- `.report-amount` - Right-aligned amounts

**AG Grid:**
- `.ag-grid-container` with `.h-400`, `.h-500`, `.h-600`
- Auto-applies theme-aware AG Grid variables

**Other:**
- `.logo-main` - Logo sizing
- `.modal-content-themed` - Theme-aware modals
- `.card-footer-themed` - Theme-aware card footers
- `.table-themed` - Theme-aware tables
- `.text-themed` - Theme-aware text

## Implementation Priority

### Phase 1: Critical Financial Reports ✅
- ✅ utilities.css created
- ✅ Added to _Layout.cshtml
- [ ] BalanceSheet.cshtml
- [ ] IncomeStatement.cshtml
- [ ] TrialBalance.cshtml

### Phase 2: AG Grid Views
- [ ] Financials/AccountsPrev.cshtml
- [ ] Financials/JournalEntries.cshtml
- [ ] Financials/GeneralLedger.cshtml
- [ ] Administration/AuditLogs.cshtml
- [ ] Audit/GetAuditableEntities.cshtml
- [ ] Audit/GetAuditableAttributes.cshtml

### Phase 3: Dashboard & Home
- [ ] Dashboard/MonthlySales.cshtml
- [ ] Home/Index.cshtml

### Phase 4: Blazor Components (40+ files)
Replace all `bg-dark text-white` with theme-aware classes:
- [ ] Quotations/AddSalesQuotation.razor
- [ ] Sales/NewCustomerPayment.razor
- [ ] Donations/AddDonationInvoice.razor
- [ ] Donations/DonationInvoices.razor
- [ ] (30+ more files)

### Phase 5: Admin & Other Views
- [ ] Administration/Groups.cshtml
- [ ] Administration/Users.cshtml
- [ ] Administration/Roles.cshtml
- [ ] Tax/Taxes.cshtml
- [ ] Sales/DonationInvoices.cshtml
- [ ] Quotations/Quotations.cshtml

## Benefits

✅ **Theme Consistency:** All UI adapts to light/dark mode automatically  
✅ **Maintainability:** Centralized styling in CSS files  
✅ **Performance:** CSS classes are more efficient than inline styles  
✅ **Accessibility:** Proper contrast ratios maintained  
✅ **Code Quality:** Separation of concerns (HTML/CSS)  

## Build Status
- ✅ utilities.css created
- ✅ Added to layout
- ⏳ Files need updating (systematic replacement)

---

**Next Steps:**
1. Update critical financial reports first
2. Test each file after changes
3. Proceed systematically through all files
4. Document any edge cases

**Date:** December 2024  
**Status:** ⏳ In Progress - Utilities Created, Files Need Updating
