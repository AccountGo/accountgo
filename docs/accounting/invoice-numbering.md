# Sales Invoice Numbering

By default, sales invoices use continuous numbering (`1`, `2`, `3`, …). You can optionally configure a fiscal-year format in **Settings → Invoices → Numbering**.

## Format pattern

Leave **Format pattern** blank for continuous numbering, or enter a pattern that includes a sequence token:

- `{FY}` — The workspace's fiscal year code without a leading `FY` prefix (for example `2026`, `2026-27`, or `2027T`).
- `{####}` — Zero-padded sequence number (1 to 10 `#` characters, such as `{#}` for `1` or `{####}` for `0001`).

Formatted numbers may use letters, digits, `-`, `_`, `/`, and `.` (starting with a letter or digit) up to 30 characters total.

When a format pattern is active, each fiscal year maintains its own sequence starting at `1`, assigned according to the invoice's **Invoice date**.

## Next number (forward-only)

You can advance **Next number** to start at a higher value (for example when migrating mid-year). To protect issued invoice history, **Next number** cannot be lowered below the current sequence value or any number already issued under the current format in that fiscal year.

Changing the format pattern only affects newly issued invoices; existing issued invoices keep their numbers.
