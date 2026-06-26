# Blazor Component Label Visibility Fix

## Problem Reported
On the "Add Sales Quotation" page (Accounts Receivable >> Sales Quotations >> "+ New Quotation"), the following labels were invisible in dark mode:
- "Customer"
- "Payment Term"

## Root Cause

### Issue 1: Labels are Divs, Not Label Tags
The Blazor component uses plain `<div>` elements as labels instead of `<label>` tags:

```razor
<!-- Line 19-20 in AddSalesQuotation.razor -->
<div class="row mb-2">
    <div class="col-sm-2">Customer</div>  <!-- Not a <label>! -->
    <div class="col-sm-10">
        <InputSelect class="form-control bg-dark text-white" ...>
```

Our previous CSS only targeted `<label>` elements, so these div-based labels weren't affected.

### Issue 2: Hardcoded Dark Mode Classes
The Blazor component has hardcoded Bootstrap classes that override theme styles:
- `bg-dark` (dark background)
- `text-white` (white text)

These hardcoded classes don't change when switching to light mode!

---

## Solution Implemented

### 1. ✅ Target Div-Based Labels
Added CSS rules to style div elements used as labels in form rows:

```css
/* Dark mode */
.dark-theme .row .col-sm-2,
.dark-theme .card-body .row > .col-sm-2:first-child {
    color: #e4e7ea !important;
    font-weight: 500;
}

/* Light mode */
.light-theme .row .col-sm-2,
.light-theme .card-body .row > .col-sm-2:first-child {
    color: #23282c !important;
    font-weight: 500;
}
```

### 2. ✅ Override Hardcoded Classes
Added CSS rules to override hardcoded `bg-dark` and `text-white` classes:

```css
/* Light mode - override bg-dark */
.light-theme .bg-dark {
    background-color: #fff !important;
}

.light-theme .text-white {
    color: #23282c !important;
}

/* Dark mode - ensure proper colors */
.dark-theme .bg-dark {
    background-color: #2f353a !important;
}

.dark-theme .text-white {
    color: #e4e7ea !important;
}
```

### 3. ✅ Fix Select/InputSelect Dropdowns
Fixed select dropdowns with hardcoded classes:

```css
.light-theme select.bg-dark,
.light-theme InputSelect.bg-dark {
    background-color: #fff !important;
    color: #23282c !important;
}

.light-theme select.bg-dark option {
    background-color: #fff !important;
    color: #23282c !important;
}
```

---

## Files Modified

### `/src/AccountGoWeb/wwwroot/css/theme-toggle.css`

**Added:**
- Div-based label selectors (`.row .col-sm-2`, etc.)
- Hardcoded class overrides (`.bg-dark`, `.text-white`)
- Select/InputSelect dropdown fixes
- Card body text fixes
- EditForm label fixes

---

## Elements Fixed

### ✅ Div Labels in Form Rows
```html
<div class="row">
    <div class="col-sm-2">Customer</div>  <!-- NOW VISIBLE -->
    <div class="col-sm-10">...</div>
</div>
```

### ✅ Hardcoded Dark Classes
```html
<select class="form-control bg-dark text-white">  <!-- NOW ADAPTS TO THEME -->
    <option class="text-white">...</option>
</select>
```

### ✅ InputSelect Components
```razor
<InputSelect class="form-control bg-dark text-white" @bind-Value="model.CustomerId">
    <!-- NOW ADAPTS TO THEME -->
</InputSelect>
```

---

## Testing

**Hard refresh browser** (Ctrl+Shift+R / Cmd+Shift+R) and test:

### Dark Mode
1. Navigate to: Accounts Receivable >> Sales Quotations >> "+ New Quotation"
2. ✅ "Customer" label should be visible (light gray)
3. ✅ "Payment Term" label should be visible (light gray)
4. ✅ Dropdown backgrounds should be dark
5. ✅ Dropdown text should be light gray

### Light Mode
1. Click theme toggle to switch to light mode
2. ✅ "Customer" label should be visible (dark gray/black)
3. ✅ "Payment Term" label should be visible (dark gray/black)
4. ✅ Dropdown backgrounds should be white
5. ✅ Dropdown text should be dark

---

## Why This Happened

### Common Blazor Pattern
Using divs with column classes as labels is a common pattern in Blazor forms:
```razor
<div class="row">
    <div class="col-sm-2">Label Text</div>
    <div class="col-sm-10"><InputSelect ... /></div>
</div>
```

This pattern is simpler than using proper `<label for="...">` tags, but requires additional CSS to style properly.

### Hardcoded Classes
The component was likely created when the app only had dark mode, so the developer hardcoded `bg-dark text-white` classes. These don't adapt to theme changes.

---

## Future Improvements (Optional)

### Refactor Blazor Components
Consider updating Blazor components to:
1. Use proper `<label>` elements with `for` attributes
2. Remove hardcoded `bg-dark` and `text-white` classes
3. Use theme-aware CSS classes instead

**Example refactor:**
```razor
<!-- Before -->
<div class="row mb-2">
    <div class="col-sm-2">Customer</div>
    <div class="col-sm-10">
        <InputSelect class="form-control bg-dark text-white" @bind-Value="model.CustomerId">

<!-- After -->
<div class="row mb-2">
    <label for="customer" class="col-sm-2">Customer</label>
    <div class="col-sm-10">
        <InputSelect id="customer" class="form-control" @bind-Value="model.CustomerId">
```

This would:
- ✅ Improve accessibility (proper label association)
- ✅ Reduce need for override CSS
- ✅ Automatically inherit theme colors
- ✅ Follow HTML5 best practices

---

## Other Pages with Same Pattern

These pages likely have the same issue and are now fixed:
- Sales >> Add Sales Order
- Sales >> Add Customer Payment
- Sales >> Add Sales Invoice
- Donations >> Add Donation Invoice
- Purchasing >> Add Purchase Order
- Purchasing >> Add Vendor Invoice
- Any other Blazor form components

---

## Build Status
✅ **Build succeeded** (0 errors, 0 warnings)

---

## Related Documentation
- **Label Visibility Fix:** `label-visibility-fix.md`
- **Theme Toggle Implementation:** `theme-toggle-implementation.md`
- **Color Standardization:** `color-standardization-summary.md`

---

**Date:** December 2024  
**Status:** ✅ Complete - Ready for Testing  
**Scope:** All Blazor form components with div-based labels
