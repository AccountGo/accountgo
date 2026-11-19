---
name: accountgo-local-dev
description: This skill should be used when a developer asks to "set up AccountGo locally", "clone and run AccountGo", "start AccountGo local development", "build and run AccountGo API and Web", or bootstrap a local AccountGo dev environment.
---

# AccountGo Local Development Setup

Bootstrap, build, and run the AccountGo Community Edition (.NET 10 API + React 19 / Vite Web SPA + SQLite) on a local workstation.

## 1. Verify Prerequisites (Check `.NET 10 SDK` First)

Check required tools before cloning or building:

```bash
dotnet --version   # .NET 10 SDK required (e.g. 10.0.x)
git --version
node --version     # Node.js 22.22.2+, 24.15.0+, or 26+
pnpm --version     # pnpm 12
openssl version
```

If `dotnet` is missing or older than `.NET 10`:
- On macOS (Homebrew): `brew install --cask dotnet-sdk` (or use Microsoft's `dotnet-install.sh --channel 10.0`).
- On Linux/WSL: install the `.NET 10 SDK` via your distro package manager or Microsoft's `dotnet-install.sh --channel 10.0`.
- Stop and report any prerequisite that could not be installed before proceeding.

## 2. Clone Repository (If Not Already Cloned)

Check if the current directory is already an AccountGo checkout (`src/Api/Api.csproj` and `src/Web/package.json` exist). If not, clone the public repository:

```bash
git clone https://github.com/AccountGo/accountgo.git
cd accountgo
```

## 3. Install Dependencies & Build

Restore and build both the .NET backend and the Vite/React frontend from the repository root:

```bash
dotnet restore
dotnet build src/Api/Api.csproj
cd src/Web && pnpm install --frozen-lockfile && pnpm build && cd ../..
```

## 4. Configure `src/Api/.env` (SQLite by Default)

If `src/Api/.env` does not exist yet, create a disposable local configuration using **SQLite** as the default local database and the development console email sender (do **not** set `RESEND_API_KEY` so OTP codes are returned/auto-filled in Development; do **not** overwrite an existing `src/Api/.env`):

```bash
if [ ! -f src/Api/.env ]; then
  umask 077
  printf 'SECRET_KEY=%s\nINTERNAL_SYNC_KEY=%s\nPUBLIC_WEB_URL=http://localhost:3000\nPUBLIC_API_URL=http://localhost:8001\nDBPROVIDER=Sqlite\nConnectionStrings:SqliteConnection=Data Source=accountgo.db;Foreign Keys=True\n' \
    "$(openssl rand -hex 32)" "$(openssl rand -hex 32)" > src/Api/.env
fi
```

### Optional: Use PostgreSQL Instead of SQLite

If the developer asks to run against PostgreSQL instead of SQLite:

1. Start a local PostgreSQL instance (for example with Docker):
   ```bash
   docker run --name accountgo-postgres -e POSTGRES_USER=accountgo \
     -e POSTGRES_PASSWORD=localdevpassword -e POSTGRES_DB=accountgo_local \
     -p 127.0.0.1:5432:5432 -v accountgo-postgres-data:/var/lib/postgresql/data \
     -d postgres:17
   ```
2. In `src/Api/.env`, **replace** the `DBPROVIDER=Sqlite` and `ConnectionStrings:SqliteConnection` lines (do not leave duplicate `DBPROVIDER` entries) with:
   ```env
   DBPROVIDER=Postgres
   ConnectionStrings:PostgresConnection=Host=localhost;Port=5432;Database=accountgo_local;Username=accountgo;Password=localdevpassword
   ```
3. Start the API as normal; it will apply PostgreSQL migrations and seed demo data on first startup.

## 5. Run API and Web Locally

Check if ports `8001` or `3000` are already in use. Start both processes in the background (or separate terminals):

1. **API (`http://localhost:8001`)**:
   ```bash
   ASPNETCORE_ENVIRONMENT=Development dotnet run --no-launch-profile --project src/Api/Api.csproj --urls http://localhost:8001
   ```
   Wait until `http://localhost:8001/health` returns `200 OK` (`"status":"healthy"`). On first run, this creates and seeds `src/Api/accountgo.db`.

2. **Web SPA (`http://127.0.0.1:3000`)**:
   ```bash
   cd src/Web && pnpm dev --host 127.0.0.1
   ```
   Vite proxies `/api` requests to `http://localhost:8001` (configured in `src/Web/vite.config.ts`). Wait until `http://127.0.0.1:3000/` responds with `200 OK`.

## 6. Report Ready State & First-Time Login Notes

Once both servers are healthy, report the local URLs (`http://127.0.0.1:3000/` and `http://localhost:8001/health`) and remind the developer:
- Use **Sign up** on `/login` with any local test email; in `Development` mode without `RESEND_API_KEY`, the 6-digit OTP code is auto-filled.
- Create a **Community** workspace, then open **Team & Members → Set up my access** (or **Confirm solo setup** if enabled) to grant functional roles (`Owner` alone does not grant operational accounting permissions).
