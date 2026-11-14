# Changelog

All notable changes to **AccountGo** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Changes collect under `[Unreleased]` as they ship to production and get a version number only when a release is announced. See [RELEASING.md](RELEASING.md).

---

## [Unreleased]

### Added

- Reports now include a statement of changes in equity: opening balance, profit, contributions and drawings (or shares issued and dividends), prior-period adjustments and closing balance, checked against the balance sheet, with a comparison period.
- You can undo an owner drawing or contribution from the matched statement line.
- Bank statement lines that move money between you and your business can be recorded as an owner drawing or owner contribution wherever statement import is available, so they no longer count as expenses or income. Partnerships use partner wording.
- Partnerships can set up each partner's capital and drawings accounts with profit-sharing ratios for each year. Bank statement lines can be recorded as a drawing or contribution for a chosen partner, with remembered suggestions and undo, and the statement of changes in equity shows a column for each partner. Closing the year splits the profit or loss between partners and closes each partner's drawings into their capital.

### Changed
- Resuming payment on a workspace waiting for checkout now sends you directly back to checkout for the plan you selected, or asks you to choose a plan when none was recorded.

### Fixed
- Workspace automatic sign-out settings now apply per active workspace and no longer fall back to 15 minutes when unconfigured or when a member belongs to another workspace with a shorter timeout.
- Bank statement Personal actions appear only when Personal books are linked, and each workspace shows one set of owner money-movement actions.

### Removed
- The unused audit settings API (`/api/Audit/Entity` and `/api/Audit/Attribute`) is gone. Its settings applied to every workspace on a server and never changed what the audit trail records.

### Self-hosting
- A database migration adds partner and profit-sharing tables and partner columns to bank statement description rules and company settings. It runs automatically when the API starts.
- PostgreSQL deployments can run behind a transaction-mode connection pooler (such as Supabase's on port 6543), which lets more API instances share one database. The deployment guide lists the connection settings.

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
