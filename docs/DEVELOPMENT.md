# Development Guide

This guide covers setting up your local environment, building, running, and debugging the **AccountGo** application.

---

## Local setup

Follow the [step-by-step local setup in the root README](../README.md#run-locally-api-web-and-database) for prerequisites, `.env`, SQLite or PostgreSQL, sign-in, workspace creation, functional roles, and troubleshooting. The README is the source of truth for the local startup commands.

For schema work, PostgreSQL migrations are in `src/Api/Data/Migrations/Postgres`. The `--copy-from-sqlite` tool copies a SQLite database into an empty PostgreSQL database; see the implementation and its safeguards before using it with valuable data.

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
