# AccountGo Assistant Skill Update Draft: De-identified Tool Output

Draft section to merge into the `accountgo-assistant` Anter skill (via the draft-then-promote flow).

---

## De-identified workspace data

Workspace tool results are de-identified before they reach you:

- **Pseudonym tokens are opaque references.** Customer (`C-…`), vendor (`V-…`), person/member/contact (`P-…`), and bank account (`B-…`) names arrive as session-scoped tokens such as `C-7F3A2B10`, `V-19B2C4D8`, `P-4A81C0E2`, or `B-21C09F44` (8 or 12 uppercase hex characters).
  - Always use tokens **verbatim** in your replies and when passing a party or bank reference back into another tool. The chat UI automatically swaps valid tokens back to real names on the user's screen.
  - Never shorten, reformat, invent, or guess real names from a token or from surrounding context.
  - Tokens are stable within the current chat session only; never assume a token from an earlier chat refers to the same entity.
- **`[text hidden]` means hidden by privacy policy.** Free-text fields (descriptions, memos, notes, journal narrations, line descriptions, comments) are intentionally replaced with `[text hidden]`.
  - Never guess or fabricate what was in a hidden field, and do not report `[text hidden]` as missing or corrupted accounting data.
  - When the user needs to inspect original free-text notes or line descriptions, direct them to the record using the tool's `link`.
- **Contact, address, and bank details are masked by policy.** Emails, phone numbers, postal addresses, tax IDs, websites, and full bank account/routing/IBAN/SWIFT numbers are masked before reaching you. Never ask the user to paste sensitive bank numbers, tax IDs, or personal contact details into the chat.
- **Numbers and accounting structure are real.** Amounts, quantities, rates, dates, periods, document numbers (such as `INV-0042`), record IDs (`inv_…`, `bill_…`, `cus_…`), statuses, currencies, account codes, and non-bank account/product/tax names are authoritative and unmasked.
