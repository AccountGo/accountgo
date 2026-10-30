# Development Guide

This guide covers setting up your local environment, building, running, and debugging the **AccountGo** application.

---

## 1. Prerequisites

Ensure you have the following installed on your development workstation:

| Tool | Version Requirement | Purpose |
| ---- | ------------------- | ------- |
| **.NET SDK** | .NET 10.0+ | Backend API, Core domain, and tests |
| **Node.js** | v22.0.0+ | Frontend build toolchain |
| **pnpm** | v9.0.0+ | Fast node package manager |
| **Git** | v2.30+ | Source version control |
| **Docker** | (Optional) | Local SQL Server or containerized orchestration |

Verify versions in your terminal:
```bash
dotnet --version
node -v
pnpm -v
```

---

## 2. Repository Setup

Clone the repository and initialize submodules (if any):

```bash
git clone https://github.com/AccountGo/accountgo.git
cd accountgo
```

Restore dependencies:
```bash
# Restore .NET packages
dotnet restore

# Restore Web frontend packages
cd src/Web
pnpm install
cd ../..
```

---

## 3. Running Locally

You can run AccountGo either via **.NET Aspire** (orchestrates both backend and frontend together) or by running the API and Web frontend independently.

### Option A: Independent Processes (Recommended for rapid development)

#### 1. Start Backend API (.NET 10)
```bash
dotnet run --project src/Api/Api.csproj --urls "http://localhost:8001"
```
The API starts on `http://localhost:8001`. On first run, it automatically creates and seeds the local SQLite database (`accountgo.db` or `src/Api/accountgo.db`).

#### 2. Start Frontend Web (React 19 / Vite)
In a new terminal window:
```bash
cd src/Web
pnpm dev
```
The Vite development server starts on `http://localhost:3000` with hot-module reloading enabled.

### Option B: Using .NET Aspire AppHost
```bash
dotnet run --project src/Aspire/AccountGo.AppHost/AccountGo.AppHost.csproj
```
Aspire launches the dashboard at `http://localhost:15888` providing live logs, metrics, and traces across all services.

---

## 4. Development Credentials & Authentication

AccountGo uses passwordless authentication via one-time passcodes (OTP).

1. Open your browser to `http://localhost:3000`.
2. Enter the default administrator email:
   ```
   admin@accountgo.org
   ```
3. In development mode, the API outputs the generated 6-digit passcode in the terminal log (and in the API response `devCode`):
   ```
   [OTP Service] Dev Verification Code for admin@accountgo.org: 123456
   ```
4. Enter the code to sign in and select the default organization: **Financial Solutions Inc.** (`ph-financials`).

---

## 5. Database Management & Seeding

### SQLite Database
By default, the application runs against SQLite stored at `src/Api/accountgo.db`.

#### Resetting and Re-seeding:
To start fresh with clean sample data:
```bash
# Stop the API server, then remove the SQLite database files
rm -f src/Api/accountgo.db* accountgo.db*

# Start the API - it automatically runs migrations and seeds default entities
dotnet run --project src/Api/Api.csproj --urls "http://localhost:8001"
```

#### Inspecting the Database:
You can use `sqlite3` or any GUI like **DB Browser for SQLite**:
```bash
sqlite3 src/Api/accountgo.db "SELECT Id, Name, Code FROM Company;"
```

### PostgreSQL

Set the provider and a connection string (environment or `.env`):

```bash
DatabaseProvider=Postgres
ConnectionStrings__PostgresConnection="Host=localhost;Port=5432;Database=accountgo;Username=postgres;Password=..."
```

On start the API applies the Postgres migrations (`src/Api/Data/Migrations/Postgres`). A database with no migrations
applied also gets the demo books and the built-in users; set `SEED_DEMO_DATA=false` to skip the demo books.

Schema changes need an EF migration for each server provider. For Postgres:

```bash
cd src/Api
dotnet ef migrations add <Name> --context PostgresApiDbContext \
  --output-dir Data/Migrations/Postgres/ApiDb --namespace Api.Data.Migrations.Postgres.ApiDb
```

(`PostgresIdentityDbContext` with `Data/Migrations/Postgres/IdentityDb` for Identity tables.)

#### Copying a SQLite database into Postgres

`--copy-from-sqlite` copies every table of a SQLite file (for example a D1 export loaded with `sqlite3`) into an empty
Postgres database, in one transaction, and checks the row counts afterwards:

```bash
sqlite3 export.db < d1-export.sql
DatabaseProvider=Postgres ConnectionStrings__PostgresConnection="..." \
  dotnet Api.dll --copy-from-sqlite export.db
```

It applies the migrations first and refuses a target that already holds data.

---

## 6. Running Automated Tests

Run all unit and integration tests across the solution:

```bash
# Run backend test suite
dotnet test

# Run tests with detailed verbosity
dotnet test --logger "console;verbosity=normal"

# Run the multi-tenancy integration tests on Postgres (one throwaway database per test)
ACCOUNTGO_TEST_POSTGRES="Host=127.0.0.1;Port=5432;Username=postgres;Password=...;Maximum Pool Size=5" \
  dotnet test test/AccountGo.BackendTests --filter "FullyQualifiedName~MultiTenancyPhase3Tests" -- xUnit.MaxParallelThreads=4

# Typecheck and build frontend
cd src/Web
pnpm build
```

---

## 7. Code Formatting & Linting

- **C#**: Format code using `dotnet format`:
  ```bash
  dotnet format accountgo.sln
  ```
- **TypeScript / CSS**: In `src/Web`:
  ```bash
  cd src/Web
  pnpm build
  ```
