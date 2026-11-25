# Changelog

All notable changes to **AccountGo** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Changes collect under `[Unreleased]` as they ship to production and get a version number only when a release is announced. See [RELEASING.md](RELEASING.md).

---

## [Unreleased]

### Added

- Eligible sole workspace owners can set up missing financial access directly from the dashboard.

- Self-hosted installations can opt in to reading uploaded documents locally and review the extracted details before saving.

- Workspace owners can opt in to AI-assisted reading of scanned documents from Settings, where their host offers it, and turn it off again at any time.

### Changed

- Attach a document to the assistant from the message box; the file is read on our servers and only masked fields reach the AI.
- Scanned bills and invoices now suggest existing vendors and customers even when the name is written differently, and remember your choices.
- Rejected document scans and scans left unused for 24 hours are now deleted, including the extracted details.
- Pages you can't open now explain why and how to get access, instead of showing an error code.
- Actions you can't take now say which role you need and how to get it, instead of a generic error.
- Error messages no longer show internal permission names.
- When your session ends, you're asked to sign in again and returned to the page you were on.
- When you lose access to a workspace, you're taken to your workspace list with an explanation instead of an error.
- The dashboard's access setup prompt for new workspace owners can be dismissed and offers a way into access setup.

### Fixed

- Two-step sign-in with a passkey, authenticator code or recovery code keeps its button disabled and shows "Verifying…" then "Signing you in…" until you are signed in, so on a slow connection it no longer looks like nothing happened and invites a second attempt. Errors that come up while finishing sign-in, such as an expired invitation, now show on screen.

- Scanned bills and invoices now read `From:` / `To:` party labels (and strip merged `Order Number` text), and the first scan after a container start no longer drops dates and totals under a cold-start parse timeout.

- Assistant document attachments now extract invoice-style fields even when the PDF uses space-separated labels instead of `Label: Value`, and fall back to masked extracted text when structured fields are still empty.

- Scanned bills now include the document's tax, as a recoverable tax rate for VAT/GST workspaces or in the line costs otherwise, and can't be approved with the tax missing.

- Scanned sales invoices now apply the document's tax as output tax, and the tax treatment of every scanned bill and invoice is recorded.

- Switching workspaces no longer counts canceled requests as service outages or failed saves.

- Notices and banners now use readable colours in the dark theme when the device itself is in light mode.

### Security

- Assistant workspace queries now replace customer, vendor, bank account, and person names with per-session pseudonyms and mask contacts, addresses, tax identifiers, and free-text notes before sending data to the AI model, restoring authorized names only in the browser.

### Self-hosting

- Optional AI field mapping for document capture (off by default; requires an Anter capture-mapper agent and a mapping policy).

## [0.3.0] - 2026-11-21

Release notes: https://github.com/AccountGo/accountgo/releases/tag/v0.3.0

### Added

- Customer credit notes, settlement applications, and dated application/credit reversals with historical aging, tax-period, and close-readiness integration (behind the per-workspace feature gate).
- Supplier credit notes (including standalone supplier credits) and multi-bill batch applications with dated reversals (behind the per-workspace feature gate).
- Customer and supplier cash refunds (including multi-source supplier inflows) and supplier advances with non-self-approval team review and bank reconciliation protection (behind the per-workspace feature gate).
- Receivable write-offs with loss-allowance attribution, impairment shortfall recognition, bounded recoveries, and erroneous write-off reversals (behind the per-workspace feature gate).
- Customer physical return receipts (at supported original issue cost and condition), supplier return dispatches with control-transfer clearing, and stock rebate allocations (behind the per-workspace feature gate).

- Starter charts include accounts for payment processor clearing, payouts in transit, deferred revenue, related-party revenue, payment fees and foreign exchange gains and losses.
- New workspaces' starter chart now includes a general Insurance Expenses account and a Loans Payable account.
- The year-end close review and Settings now name the equity account the year's result is posted to, and the books start date hints explain that the first fiscal year is the whole year containing it.

- The assistant can review your books: it checks the reports, bank reconciliations, invoices, bills and manual journals for likely errors, lists what it found with links to each record, and says what it could not check. It can also help answer an accountant's or auditor's requests. This is a review of your own records, not an audit.
- The assistant can run a set of checks over a period when you ask it to review your books: whether what clients and vendors owe agrees with the ledger, invoices and bills missing from the ledger, duplicate bills, manual journals worth a second look, and bank accounts not reconciled to the period end. Each result links to the records behind it.
- The assistant can pick out ledger entries by amount or by text in the description or account, and for a manual journal can tell members who may view record history who prepared it, who posted it and when.
- The assistant can now tell you what clients owed you and what you owed vendors at the end of any past day, such as a month end or year end, and compare it with the balance sheet at that date.
- The assistant can read fiscal years, tax reports and filings, changes in equity, sales receipts, bank outstanding aging, workspace members and roles, record history, and stock movements, with the same access rules as the app.
- Imported journals: journals created by an importer show where they came from, can't be edited, can be discarded before posting, and can be posted together from the journal list. The same imported item is never recorded twice.
- Optional fiscal-year sales invoice numbering in Settings → Invoices: configure a format pattern using `{FY}` and `{####}` (for example `INV-{FY}-{####}`) with a forward-only starting number and live preview across fiscal years, or leave the format blank for continuous numbering.
- Tax page: see output tax, input tax and the net payable for each monthly or quarterly tax period, by tax code, with the source invoices and bills and a CSV export. Tax periods are set in Settings → Taxes and don't have to match your fiscal year. Cash-basis workspaces see tax when invoices and bills are paid.
- Record a tax filing for a period: AccountGo keeps the figures you filed, warns when they change afterwards, and can lock the books through the period end. Corrections are recorded as amended filings.
- Due recurring invoices can be run directly from the recurring schedules list.
- Reports now include a statement of changes in equity: opening balance, profit, contributions and drawings (or shares issued and dividends), prior-period adjustments and closing balance, checked against the balance sheet, with a comparison period.
- You can undo an owner drawing or contribution from the matched statement line.
- Bank statement lines that move money between you and your business can be recorded as an owner drawing or owner contribution wherever statement import is available, so they no longer count as expenses or income. Partnerships use partner wording.
- Partnerships can set up each partner's capital and drawings accounts with profit-sharing ratios for each year. Bank statement lines can be recorded as a drawing or contribution for a chosen partner, with remembered suggestions and undo, and the statement of changes in equity shows a column for each partner. Closing the year splits the profit or loss between partners and closes each partner's drawings into their capital.
- Year-end close now reviews the year before it closes. The button is **Review & close**. Warnings need a written reason. Authorized users can download the retained close record as JSON for years closed with this feature.
- Reports includes **Receivables & Payables Aging** for a chosen date. It shows outstanding balances and whether they agree with the ledger. The comparison uses the accrual ledger.
- People who can review or close a year can open **Year-end close** without being an organization administrator.

### Changed

- Records are now identified everywhere by a prefixed ID that says what it is, such as `inv_…` for an invoice, `bill_…` for a bill, `cus_…` for a client and `acct_…` for an account, instead of a number. This applies to page addresses, the API and the assistant. Existing records are given an ID automatically when you upgrade. Old links and bookmarks that use numbers no longer open, and API calls must send the new IDs.
- The sign-in page now has Sign in and Create account tabs under the email field, with separate headings and wording for each. The verification code step has clearer wording, a centered layout, and a "Not your email?" link to go back and change it. The terms checkbox is now aligned with its text.
- Moved Customer Credits & Settlements, Supplier Credits & Advances, and Year-end close out of the main sidebar into the Sales tabs, Purchases tabs, and General Ledger views navigation respectively.
- Year-end assistant guidance uses the implemented close controls and explains current aging, close-record and correction limits.
- Fiscal years in Settings now show Open, Ended or Closed. A year that is not the current one is no longer labelled Closed, and an unsaved current year says it still needs to be prepared.
- The assistant can see whether each fiscal year is open, ended or closed, who closed it and when. That status is not a books-lock date and does not mean the books are correct.
- Assistant review guidance now uses tax filings, equity statements, receipt details, stock movements and record history, with explicit limits on historical evidence and sampling.
- Unnumbered invoice drafts show the next number for their invoice date. The issue action checks the previewed number and asks for confirmation again if another invoice has taken it.
- Resuming payment on a workspace waiting for checkout now sends you directly back to checkout for the plan you selected, or asks you to choose a plan when none was recorded.

### Fixed
- The final review step of workspace setup now shows the business type you chose when solo setup is off.
- Creating a workspace without solo setup now asks what kind of business it is, so a company no longer defaults to sole-proprietor equity at year-end close.
- Overflow action menus (`...`) inside cards such as client and vendor contacts now render cleanly above surrounding sections instead of being clipped, and form dialogs keep their headers and action buttons pinned while long forms scroll.
- Saving roles on the Members page while the service is starting now shows a clear message that nothing was saved, and the message scrolls into view.
- Notices above the sign-in form, such as the message shown after you are signed out, are now readable in the light theme.
- Wise webhook integrations now accept initial transfer state notifications and additional balance update schema versions, show the failing event type and reason, and let you replay failed malformed deliveries.
- Creating accounts now uses the correct debit or credit normal balance for their class and contra status. Editing unused accounts also corrects legacy defaults without changing accounts that have journal history.
- Imported journals with source workflow checks cannot be posted while their source is stale, conflicted or awaiting recovery.
- Issuing a sales invoice dated in a locked period or before the books start date is now refused before an invoice number is assigned, so failed attempts no longer consume a number.
- Workspace automatic sign-out settings now apply per active workspace and no longer fall back to 15 minutes when unconfigured or when a member belongs to another workspace with a shorter timeout.
- Recurring invoice statuses and cadence labels display correctly, restoring Run now, Skip and Pause actions for active schedules.
- Bank statement Personal actions appear only when Personal books are linked, and each workspace shows one set of owner money-movement actions.

### Removed
- The unused audit settings API (`/api/Audit/Entity` and `/api/Audit/Attribute`) is gone. Its settings applied to every workspace on a server and never changed what the audit trail records.

### Security
- Sign-in and welcome verification emails no longer put the live OTP in the subject line, and production email/dispatch logs no longer record recipient addresses, subjects, or raw provider response bodies. Dev console email dumps are gated to Development and redact invite tokens in the body.
- Operational text logs no longer include client IP or user-agent on OTP and refresh-token events (those stay in the security audit table), leftover sales/donation debug Information logs that could leak line descriptions were removed, API error responses no longer echo raw database exception text for sales invoice save/load failures, and OpenTelemetry ASP.NET spans strip `url.query` so signed PDF and OAuth query parameters are not exported.
- CLI credentials can no longer create unrestricted web sessions. Signing out or revoking a session now prevents concurrent refresh requests and older refresh tokens from restoring it, and alternate spellings of sign-in URLs no longer bypass bot verification.

### Self-hosting
- API startup logs are quieter by default: a short structured summary replaces the long Console dump. Raise `Microsoft.EntityFrameworkCore.Database.Command` to see SQL again; set `EF_SENSITIVE_DATA_LOGGING=true` for EF sensitive-data logging.
- A database migration adds partner and profit-sharing tables and partner columns to bank statement description rules and company settings. It runs automatically when the API starts.
- PostgreSQL deployments can run behind a transaction-mode connection pooler (such as Supabase's on port 6543), which lets more API instances share one database. The deployment guide lists the connection settings.
- `SEED_DEMO_DATA=false` now also applies to a new SQLite database, so it starts without the demo books. The default is unchanged: new databases get the demo books.

## [0.2.0] - 2026-11-13

Release notes: https://github.com/AccountGo/accountgo/releases/tag/v0.2.0

### Added
- AccountGo now tells you when a new version is available, so you can reload when it suits you.
- A clear status screen now appears while AccountGo is being updated, starting up or under maintenance. It continues on its own when AccountGo is back and keeps what you were working on.
- Before the Financials module turns on, AccountGo checks that every issued invoice and bill has its ledger entry. It lists any that don't and can post them for you on their own dates, so your reports agree with what customers and vendors owe.
- A plan picked before signing up is highlighted on the subscription settings once your workspace is set up.
- A plan change that would charge your saved card now shows the exact amount (today's prorated charge, then the regular price and date) and waits for you to confirm before anything is charged.

### Changed
- Plan screens take plan names, groups and highlights from your server's plan list. A locked module now says which plan includes it, and the sidebar suggestion to get a general ledger links to your plans instead of naming one.
- The plan limit message no longer says paid plans are coming soon. It offers a contact link when no upgrade covers what you need.
- The statement import upgrade screen now names the plans that include statement import from your server's plan list, instead of fixed plan names. The sign-in screen and loading message now say AccountGo.

### Fixed
- On invoices and bills, the income and expense account choice on each line and the Account column now appear whenever the General Ledger module is on. Before, most workspaces never saw them and every line went to the default account.
- Statement import no longer offers an available or interim balance, or a balance for another account, as the statement closing balance, and no longer assumes today's date when the file gives none.
- The automatic sign-out setting now tells you when another of your workspaces signs you out sooner, and names it. One sign-in covers all your workspaces, so the shortest setting applies.
- After a plan change, the plan shown in the workspace header, switcher and subscription settings now updates right away instead of after a reload, and the payment-confirming message no longer stays on screen indefinitely.
- The profile page no longer tells workspace Owners and Admins they have every permission; it now lists the permissions their security roles actually grant.

### Security
- Recurring invoices set to issue automatically are now posted only if the person who turned on auto-issue still holds the invoice posting permission; a workspace Owner or Admin role alone is no longer enough.

### Self-hosting
- A new installation no longer creates a built-in platform administrator account for the project maintainer. To have start-up create a platform owner, set `PLATFORM_OWNER_EMAIL`; that user gets the SystemAdministrators role and owns the demo workspace. Existing databases are unchanged.

---

## [0.1.0] - 2026-11-08

First release of the rebuilt AccountGo. Release notes: https://github.com/AccountGo/accountgo/releases/tag/v0.1.0

### Platform
- .NET 10 API and a React 19 / TypeScript / Vite / Tailwind CSS v4 web app, installable as a mobile home-screen app (PWA) with safe-area layouts.
- Multi-tenant workspaces with slug routing (`/org/:orgSlug/*`), one login across several businesses, and a base currency per workspace (16 supported).
- SQLite or PostgreSQL storage; the hosted service runs on Cloudflare Workers and Containers.

### Sales and purchases
- Clients, quotes, sales invoices, payments received, and a products & services catalog.
- Invoice numbers assigned at issue, stored due dates, business-day and custom payment terms, duplicate and void with frozen history.
- Branded PDF invoices, invoice email with PDF attachment or secure link, and reminders.
- Vendors, bills with per-line expense accounts and tax, and bill payments.

### Accounting
- Chart of accounts (with CSV import/export), double-entry journal entries, and the general ledger.
- Trial balance, profit and loss, balance sheet, and cash flow on an accrual or cash basis, with CSV downloads.
- Company onboarding wizard (reporting framework, fiscal year, books start date, tax regime) and an accounting policy lock after the first journal.
- Bank accounts, bank statement import, matching, and reconciliation.

### Security and teams
- Passwordless email one-time-code sign-in, Google sign-in, refresh-token sessions, rate limiting, and per-workspace idle timeout.
- Team invitations with role-based permissions, and an audit trail of changes.

### Project
- AGPL-3.0-only license, trademark policy, Contributor License Agreement, contributing guide, code of conduct, security policy, and issue/PR templates.

---

## Legacy (2015–2020)

The original ASP.NET AccountGo (last tagged `0.0.1-alpha`). Kept for history; v0.1.0 is a ground-up rebuild.

### Added
- Core double-entry accounting ledger and journal entry recording.
- Sales quotes, orders, customer accounts, and invoicing workflows.
- Purchasing orders, receipts, vendor accounts, and bill tracking.
- Financial reports: Balance Sheet, Income Statement (P&L), Trial Balance.
