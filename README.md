# AccountGo

<p align="center">
  <img src="src/Web/public/accountgo-icon.png" alt="AccountGo Logo" width="100" height="100" />
</p>

<p align="center">
  <strong>Modern, Cloud-Native, Multi-Tenant Accounting & ERP Platform</strong>
</p>

<p align="center">
  <a href="https://github.com/AccountGo/accountgo/actions/workflows/api.yml"><img src="https://github.com/AccountGo/accountgo/actions/workflows/api.yml/badge.svg" alt="Deploy API" /></a>
  <a href="https://github.com/AccountGo/accountgo/actions/workflows/web.yml"><img src="https://github.com/AccountGo/accountgo/actions/workflows/web.yml/badge.svg" alt="Deploy Web" /></a>
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet" alt=".NET 10" />
  <img src="https://img.shields.io/badge/React-19-61DAFB?logo=react" alt="React 19" />
  <img src="https://img.shields.io/badge/TypeScript-5.9-3178C6?logo=typescript" alt="TypeScript" />
  <img src="https://img.shields.io/badge/Vite-8.3-646CFF?logo=vite" alt="Vite" />
  <img src="https://img.shields.io/badge/TailwindCSS-v4-06B6D4?logo=tailwindcss" alt="Tailwind CSS v4" />
  <img src="https://img.shields.io/badge/Cloudflare-Workers_%26_Containers-F38020?logo=cloudflare" alt="Cloudflare" />
  <img src="https://img.shields.io/badge/License-Apache_2.0-blue.svg" alt="License" />
</p>

---

## Live Deployments

- **Web Application (SPA)**: [https://accountgo-web.anter.workers.dev](https://accountgo-web.anter.workers.dev)
- **Backend API & Containers**: [https://accountgo-api.anter.workers.dev](https://accountgo-api.anter.workers.dev)

---

## Overview

**AccountGo** is an open-source, enterprise-grade accounting and ERP suite designed for small to medium enterprises, multi-company groups, and non-profit organizations. 

Re-architected for modern cloud infrastructure, AccountGo combines a high-performance **.NET 10** backend containerized on Cloudflare edge compute with a responsive **React 19** single-page application powered by **Vite** and **Tailwind CSS v4**.

---

## User Interface Showcase

### 1. Passwordless Authentication & Magic Codes
Fast, secure sign-in with 6-digit OTP verification codes, sliding-window rate limiters, and HTTP-only session cookies.

![AccountGo Login](docs/screenshots/01-login.png)

---

### 2. Multi-Tenant Workspace Selector
Seamlessly switch between multiple companies, organizations, and branches with isolated data, custom currencies, and independent permission sets.

![Select Organization](docs/screenshots/02-select-org.png)

---

### 3. Executive Financial Dashboard
Real-time financial metrics, revenue vs. expense breakdowns, cash position, and quick navigation across accounting modules.

![AccountGo Dashboard](docs/screenshots/03-dashboard.png)

---

### 4. Sales & Customer Management
Manage customer directories, track accounts receivable, issue quotations, and process sales invoices with automatic tax and GL postings.

![Customers](docs/screenshots/04-customers.png)

---

### 5. Financials & Chart of Accounts
Complete double-entry accounting ledger with hierarchical chart of accounts, customizable account classes, and real-time balance calculations.

![Chart of Accounts](docs/screenshots/05-chart-of-accounts.png)

---

### 6. Team & Organization Members (RBAC)
Role-based member management with support for `SystemAdministrators` and `GeneralUsers`, granular permissions, and tenant security.

![Team & Members](docs/screenshots/06-team-members.png)

---

### 7. Profile, Sessions & Security Hub
User profile management, credential administration, active device/session monitoring with single-click remote session revocation.

![Profile & Security](docs/screenshots/07-profile-security.png)

---

## Core Capabilities & Features

- **Multi-Tenancy & Workspace Switching**: Complete tenant isolation, custom branding, localized currencies (PHP, USD, CAD, EUR, etc.), and independent fiscal years.
- **Enterprise Security & Authentication**:
  - ASP.NET Core Identity with custom SQLite / SQL Server storage.
  - Passwordless 6-digit Email OTP / Magic Code authentication.
  - JWT access tokens + secure, HTTP-only refresh tokens.
  - Sliding-window and fixed-window rate limiters on sensitive auth endpoints.
  - Concurrent session tracking with remote session revocation.
- **Financial Accounting**:
  - Hierarchical Chart of Accounts (Assets, Liabilities, Equity, Revenue, Expenses).
  - Double-entry Journal Entries with balanced debit/credit validation.
  - Bank Accounts and Cash Management.
  - Fiscal year closing and period controls.
- **Sales & Accounts Receivable (AR)**:
  - Customers, Sales Quotations, Sales Invoices, and Payment Receipts.
  - Customer Aging reports.
- **Purchasing & Accounts Payable (AP)**:
  - Vendors, Purchase Orders, Bills, and Vendor Aging.
- **Inventory & Warehousing**:
  - Item Master, Categories, Units of Measure, and Inventory Adjustments.
- **Donations & Non-Profit Features**:
  - Donor tracking, Payment Methods (Cash, Cheque, Credit Card, E-Transfer).
  - Official Tax Receipt generation and dedicated GL account attribution.
- **Audit Trails**:
  - Comprehensive entity auditing and security event logging.

---

## Architecture & Technology Stack

```
                          ┌──────────────────────────────────────┐
                          │         Cloudflare Workers           │
                          │   (React 19 SPA Static Assets)       │
                          │   accountgo-web.anter.workers.dev    │
                          └──────────────────┬───────────────────┘
                                             │  /api/* Proxy
                                             ▼
                          ┌──────────────────────────────────────┐
                          │        Cloudflare Containers         │
                          │   (.NET 10 ASP.NET Core Web API)     │
                          │   accountgo-api.anter.workers.dev    │
                          └──────────────────┬───────────────────┘
                                             │
                      ┌──────────────────────┴──────────────────────┐
                      ▼                                             ▼
          ┌───────────────────────┐                     ┌───────────────────────┐
          │     Entity Framework  │                     │    ASP.NET Identity   │
          │   Core (Domain Data)  │                     │   (Auth, RBAC, Tokens)│
          │     accountgo.db      │                     │     accountgo.db      │
          └───────────────────────┘                     └───────────────────────┘
```

| Layer | Technologies |
| :--- | :--- |
| **Frontend** | React 19, TypeScript, Vite 8, Tailwind CSS v4, Lucide React, React Router 7 |
| **Backend API** | .NET 10, ASP.NET Core Web API, C# 14 |
| **Data Access** | Entity Framework Core 10, SQLite (edge-optimized), SQL Server support |
| **Security & Auth**| ASP.NET Core Identity, JWT Bearer, HTTP-only Refresh Cookies, Rate Limiting |
| **Orchestration** | .NET Aspire (`src.AppHost`, `src.ServiceDefaults`) |
| **Edge Compute** | Cloudflare Workers, Cloudflare Containers, Durable Objects |
| **CI / CD** | GitHub Actions with automated Cloudflare deployments |

---

## Repository Structure

```
accountgo/
├── .github/workflows/          # GitHub Actions CI/CD workflows
│   ├── api.yml                 # Cloudflare Container deployment for API
│   └── web.yml                 # Cloudflare Workers deployment for Web SPA
├── docs/                       # Project documentation & screenshots
│   └── screenshots/            # High-resolution application screenshots
├── src/
│   ├── Api/                    # ASP.NET Core Web API (.NET 10) & Worker adapter
│   ├── Core/                   # Domain entities, value objects, and interfaces
│   ├── Dto/                    # Data Transfer Objects for API contracts
│   ├── Infrastructure/         # Assembly loading, module extensions, shared utilities
│   ├── MigrationService/       # EF Core automated database migration runner
│   ├── Services/               # Business logic, repositories, and domain services
│   ├── src.AppHost/            # .NET Aspire local orchestration host
│   ├── src.ServiceDefaults/    # Aspire telemetry, health checks, and service discovery
│   └── Web/                    # React 19 + TypeScript + Vite + Tailwind CSS v4 SPA
└── test/
    ├── AccountGo.BackendTests/ # Domain and accounting logic tests
    ├── AccountGo.ServicesTests/# Security, Administration, and Donations service tests
    └── Module.Tests/           # Dynamic module loader and plugin tests
```

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (`net10.0`)
- [Node.js 22+](https://nodejs.org/)
- [pnpm 10+](https://pnpm.io/) (`npm install -g pnpm`)

---

### Running with .NET Aspire (Recommended)

1. Clone the repository:
   ```bash
   git clone https://github.com/AccountGo/accountgo.git
   cd accountgo
   ```
2. Launch the Aspire AppHost:
   ```bash
   dotnet run --project src/src.AppHost/src.AppHost.csproj
   ```
3. Open the Aspire Dashboard URL displayed in your terminal to inspect logs, traces, and service endpoints.

---

### Running API and Web Independently

#### 1. Start Backend API
```bash
cd src/Api
dotnet run
```
The API starts locally on `http://localhost:8001`.

#### 2. Start Frontend Web
```bash
cd src/Web
pnpm install
pnpm run dev
```
The application opens at `http://localhost:5173`.

#### Default Credentials
- **Default Email**: `admin@accountgo.ph`
- **Authentication**: Enter your email to receive an instant 6-digit verification code. In local development and staging environments, the code is auto-filled or printed directly to the API console.

---

## Database Setup (SQLite & Multi-Engine)

### Zero-Configuration SQLite (Default)

AccountGo comes pre-configured with **SQLite** as the primary, zero-configuration database engine. You do not need to install SQL Server, MySQL, or run heavy Docker database containers to get started.

- **Configuration**: Set in [`src/Api/appsettings.json`](src/Api/appsettings.json):
  ```json
  "DatabaseProvider": "Sqlite",
  "ConnectionStrings": {
    "SqliteConnection": "Data Source=accountgo.db"
  }
  ```
- **Automated Lifecycle on API Startup**:
  1. **Automatic Database Creation**: If `accountgo.db` does not exist, EF Core automatically creates the file on startup.
  2. **Schema Generation**: Automatically executes table creation for both `ApiDbContext` (business domains) and `ApplicationIdentityDbContext` (ASP.NET Identity auth, users, and refresh tokens).
  3. **Initial Data Seeding**: Populates initial chart of accounts, company setup ("Financial Solutions Inc."), tax configurations, default vendors, customers, items, banks, and RBAC roles (`SystemAdministrators`, `GeneralUsers`).
  4. **Admin User Provisioning**: Guarantees `admin@accountgo.ph` is seeded and initialized with master administrative permissions.

### Resetting or Re-seeding the Database
To reset the database back to clean seeded defaults at any time:
```bash
# Remove local SQLite database and write-ahead logs
rm -f src/Api/accountgo.db*

# Start the API - it will automatically recreate and reseed
cd src/Api && dotnet run
```

### Inspecting the SQLite Database
You can query and explore the database directly using the SQLite CLI or visual tools:
```bash
sqlite3 src/Api/accountgo.db ".tables"
sqlite3 src/Api/accountgo.db "SELECT Name, ShortName, Slug FROM Company;"
sqlite3 src/Api/accountgo.db "SELECT UserName, Email FROM AspNetUsers;"
```
Visual tools supported:
- **VS Code**: SQLite Viewer or SQLite Explorer extension
- **GUI**: [DB Browser for SQLite](https://sqlitebrowser.org/)

### Switching to Microsoft SQL Server (Optional)
If you prefer running against Microsoft SQL Server for enterprise deployments:
1. Set the environment variable `DBPROVIDER=SqlServer` (or edit `DatabaseProvider` in [`src/Api/appsettings.json`](src/Api/appsettings.json)).
2. Configure your connection string in `ConnectionStrings:DefaultConnection`.
3. Start the API — EF Core will automatically target your SQL Server instance.

---

## Running Automated Tests

AccountGo includes a comprehensive automated test suite covering domain entities, accounting algorithms, RBAC security, and service boundaries:

```bash
# Run all 99 automated tests across the solution
dotnet test
```

```bash
# Test frontend compilation and type-safety
cd src/Web
pnpm run build
```

---

## Deployment to Cloudflare

Deployments are fully automated via GitHub Actions on every push to `main`:

- **API**: Packaged into a container and deployed to Cloudflare Containers via Wrangler.
- **Web**: Built with Vite and deployed to Cloudflare Workers Static Assets via Wrangler.

To deploy manually with Wrangler:
```bash
# Deploy Backend API
cd src/Api
pnpm run deploy

# Deploy Frontend Web SPA
cd src/Web
pnpm run deploy
```

---

## Contributing

Contributions, issues, and feature requests are welcome!

1. Fork the project.
2. Create your feature branch (`git checkout -b feature/amazing-feature`).
3. Commit your changes (`git commit -m 'feat: add amazing feature'`).
4. Ensure all tests pass (`dotnet test`).
5. Push to your branch (`git push origin feature/amazing-feature`).
6. Open a Pull Request.

---

## License

This project is licensed under the Apache License 2.0. See the [LICENSE](LICENSE) file for details.
