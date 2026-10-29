# Changelog

All notable changes to **AccountGo** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### Added
- Company onboarding: a multi-step `/create-org` wizard that captures reporting framework, fiscal year, books start date, tax regime, industry and inventory costing; the API derives the fiscal year and chart-of-accounts template from those choices. Signup no longer creates a workspace.
- Accounting policy lock: framework, basis, fiscal year and books start freeze after the first journal entry; postings dated before the books start date are rejected.
- Dashboard "Get books ready" checklist and a Settings accounting policy card.
- Standard Open Source community health files: `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md` (Contributor Covenant v2.1), `SECURITY.md`.
- GitHub issue templates for bug reports and feature requests, plus pull request template with comprehensive verification checklist.
- Technical documentation in `docs/`: `ARCHITECTURE.md`, `DEVELOPMENT.md`, `DEPLOYMENT.md`.
- Live database persistence and bi-directional editing for Organization Settings (`/org/:orgSlug/settings`).
- Complete Role-Based Access Control (RBAC) permission matrices and subscription tier management UI.

---

## [1.0.0] - 2026-10-20

### Modernization & Architecture Overhaul
- **Upgraded Runtime**: Upgraded backend architecture to **.NET 10** with .NET Aspire orchestration and OpenTelemetry observability.
- **Frontend Rewrite**: Modernized web client to **React 19**, **TypeScript 5.9**, **Vite 8**, and **Tailwind CSS v4** with custom glassmorphism design tokens.
- **Passwordless Authentication**: Implemented 6-digit OTP verification code flow with rate-limiting and HTTP-only session cookies.
- **Multi-Tenancy Engine**: Dynamic organization slug routing (`/org/:orgSlug/*`), tenant header resolution, and isolated company databases.
- **Cloudflare Edge Deployment**: Added Cloudflare Workers deployment for frontend SPA and Cloudflare Containers runtime for backend API.
- **Database Support**: Zero-config SQLite database matching Cloudflare D1 schema with multi-engine support for Microsoft SQL Server.
- **Automated CI/CD**: Added GitHub Actions workflows for continuous compilation, container packaging, and automated deployment.

---

## [0.9.0] - Legacy Release

### Added
- Core double-entry accounting ledger and journal entry recording.
- Sales quotes, orders, customer accounts, and invoicing workflows.
- Purchasing orders, receipts, vendor accounts, and bill tracking.
- Financial reports: Balance Sheet, Income Statement (P&L), Trial Balance.
