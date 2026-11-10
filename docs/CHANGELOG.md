# Changelog

All notable changes to **AccountGo** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Changes collect under `[Unreleased]` as they ship to production and get a version number only when a release is announced. See [RELEASING.md](RELEASING.md).

---

## [Unreleased]

### Added
- AccountGo now tells you when a new version is available, so you can reload when it suits you.
- A clear status screen now appears while AccountGo is being updated, starting up or under maintenance. It continues on its own when AccountGo is back and keeps what you were working on.
- Before the Financials module turns on, AccountGo checks that every issued invoice and bill has its ledger entry. It lists any that don't and can post them for you on their own dates, so your reports agree with what customers and vendors owe.
- A plan picked before signing up is highlighted on the subscription settings once your workspace is set up.
- A plan change that would charge your saved card now shows the exact amount (today's prorated charge, then the regular price and date) and waits for you to confirm before anything is charged.

### Changed
- The plan limit message no longer says paid plans are coming soon. It offers a contact link when no upgrade covers what you need.

### Fixed
- The automatic sign-out setting now tells you when another of your workspaces signs you out sooner, and names it. One sign-in covers all your workspaces, so the shortest setting applies.
- After a plan change, the plan shown in the workspace header, switcher and subscription settings now updates right away instead of after a reload, and the payment-confirming message no longer stays on screen indefinitely.

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
