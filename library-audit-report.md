# Library Audit Report - _Layout.cshtml

## Current State Analysis

### CSS Libraries (in <head>)
| Library | Version | Purpose | Status |
|---------|---------|---------|--------|
| `dark.css` | CoreUI v2.1.16 | Main theme | ⚠️ OLD - CoreUI v2 is for Bootstrap 4 |
| `font-awesome` | 4.7.0 | Icons | ⚠️ OLD - FA 4.7 is outdated (current is 6.x) |
| `simple-line-icons` | 2.5.5 | Icons | ✅ Used in navigation |
| `pace-js` | latest | Loading indicator | ✅ Used |
| `agw-colors.css` | Custom | Color variables | ✅ Just created |
| `GoodBooks.styles.css` | Scoped CSS | Blazor scoped | ✅ Auto-generated |
| `coreui/icons` | Local | CoreUI icons | ❓ Need to verify usage |

### JavaScript Libraries (in footer)
| Library | Version | Purpose | Status |
|---------|---------|---------|--------|
| `jquery` | 3.2.1 slim | DOM manipulation | ⚠️ OLD (current 3.7.1) |
| `popper.js` | 1.12.9 | Tooltips/dropdowns | ⚠️ OLD (for Bootstrap 4) |
| `bootstrap` | 4.0.0 | Framework JS | ❌ OUTDATED - Need Bootstrap 5.3.x |
| `pace-js` | latest | Loading | ✅ Used |
| `perfect-scrollbar` | 1.5.5 | Custom scrollbars | ❓ Need to verify usage |
| `coreui` | 4.3.2 | CoreUI framework | ⚠️ Mismatch - v4 is for Bootstrap 5 but we're using BS4 |
| `ag-grid` | 31.0.1 | Data grids | ✅ Used in several views |
| `blazor.server.js` | Framework | Blazor runtime | ✅ Required for Blazor |

## Issues Identified

### 1. Bootstrap Version Conflict ❌
- **Problem:** Loading Bootstrap 4.0.0 JS but CoreUI 4.3.2 requires Bootstrap 5
- **Impact:** Incompatible data attributes (data-toggle vs data-bs-toggle)
- **Evidence:** Mixed usage of `data-toggle` and `data-bs-toggle` in views

### 2. Outdated Libraries ⚠️
- jQuery 3.2.1 (2017) - Current is 3.7.1
- Font Awesome 4.7.0 (2016) - Current is 6.5.x
- Bootstrap 4.0.0 (2018) - Current is 5.3.x
- Popper.js 1.12.9 (for BS4) - Need Popper 2.x for BS5

### 3. Theme Mismatch ⚠️
- `dark.css` is CoreUI v2.1.16 (Bootstrap 4)
- CoreUI JS is v4.3.2 (Bootstrap 5)
- These versions are incompatible

### 4. Potentially Unused Libraries ❓
- `perfect-scrollbar` - Need to verify usage
- `coreui/icons/css/all.min.css` - May overlap with Font Awesome

## Recommendations

### Priority 1: Upgrade to Bootstrap 5.3.x
**Required Changes:**
1. Update Bootstrap CSS (via dark.css or separate link)
2. Update Bootstrap JS to 5.3.x
3. Update Popper.js to 2.11.x (Bootstrap 5 dependency)
4. Update jQuery to 3.7.1 (if keeping jQuery)
5. Update Font Awesome to 6.5.x
6. Update CoreUI CSS to v5.x or use Bootstrap 5 directly

**Code Changes Needed:**
- Replace `data-toggle` with `data-bs-toggle` globally
- Replace `data-target` with `data-bs-target` globally
- Replace `data-dismiss` with `data-bs-dismiss` globally
- Update dropdown/modal/collapse markup
- Update utility classes (ml-* → ms-*, mr-* → me-, etc.)

### Priority 2: Standardize Icon Libraries
**Options:**
1. Keep Font Awesome only (most comprehensive)
2. Use Bootstrap Icons + Simple Line Icons
3. Use CoreUI Icons only

**Recommendation:** Upgrade to Font Awesome 6.x and remove redundant icon libraries

### Priority 3: Optimize Library Loading
- Consider using Bootstrap 5 bundle (includes Popper)
- Remove unused libraries after verification
- Use CDN with integrity hashes for security
- Consider local hosting for better performance

## Migration Path

### Phase 1: Library Updates (Safe)
- Update jQuery to 3.7.1
- Update pace-js (already on latest)
- Update ag-grid to 32.x if needed

### Phase 2: Bootstrap 5 Migration (Breaking)
- Download/link Bootstrap 5.3.x CSS
- Update Bootstrap JS to 5.3.x bundle
- Update Popper to 2.11.x
- Update CoreUI CSS to v5.x or remove

### Phase 3: Code Updates (Required)
- Replace data-toggle → data-bs-toggle
- Replace data-target → data-bs-target
- Replace data-dismiss → data-bs-dismiss
- Update utility classes
- Test all modals, dropdowns, tooltips

### Phase 4: Cleanup
- Remove old Bootstrap 4 references
- Remove redundant icon libraries
- Remove unused JS libraries
- Consolidate CSS files

## Testing Checklist
- [ ] All modals open/close correctly
- [ ] All dropdowns work (nav, forms, etc.)
- [ ] Tooltips display properly
- [ ] Sidebar navigation expands/collapses
- [ ] Forms validate and submit
- [ ] Tables render correctly
- [ ] Icons display properly
- [ ] Responsive design works on mobile
- [ ] Dark theme renders correctly
- [ ] Blazor components function properly
