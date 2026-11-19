# Run Locally: API, Web, and Database

These steps start the community edition on your own machine. Commands use a POSIX shell (macOS or Linux); on Windows use WSL. Starting the API initializes the selected database and seeds a new one. Use a disposable local database: the examples are not production settings. Package restore downloads dependencies; local authentication uses a console email sender when no Resend key is configured. External email, payment checkout, Google sign-in, and the assistant need their own service configuration.

> **Coding agents:** A quick-start skill is also available at [`.agents/skills/accountgo-local-dev/SKILL.md`](../../.agents/skills/accountgo-local-dev/SKILL.md).

## 1. Install prerequisites and clone

Install Git, the .NET 10 SDK, Node.js 22.22.2+, 24.15.0+, or 26+, pnpm 12, and OpenSSL. SQLite is built into the API; the `sqlite3` command-line tool is optional. For PostgreSQL, install PostgreSQL and its client tools, or run a local PostgreSQL server with Docker. Verify:

```bash
git --version
dotnet --version
node --version
pnpm --version
openssl version
```

```bash
git clone https://github.com/AccountGo/accountgo.git
cd accountgo
dotnet restore
cd src/Web
pnpm install --frozen-lockfile
cd ../..
```

## 2. Configure the API

Create `src/Api/.env` from the repository root. This file is local and must not be committed. Generate separate keys and keep them across API restarts; changing `SECRET_KEY` invalidates existing sessions. The API's `.env` loader overrides values with the same names from the shell, so edit this file when changing providers. Set `ASPNETCORE_ENVIRONMENT` in the shell command below: the hosting environment is selected before `.env` is loaded.

```bash
umask 077
printf 'SECRET_KEY=%s\nINTERNAL_SYNC_KEY=%s\nPUBLIC_WEB_URL=http://localhost:3000\nPUBLIC_API_URL=http://localhost:8001\n' \
  "$(openssl rand -hex 32)" "$(openssl rand -hex 32)" > src/Api/.env
```

The local settings in this file are:

| Setting | Purpose |
| --- | --- |
| `SECRET_KEY` | Signs authentication tokens and codes; required and at least 32 bytes. |
| `INTERNAL_SYNC_KEY` | Separate key for internal sync endpoints. |
| `PUBLIC_WEB_URL`, `PUBLIC_API_URL` | Local origins used in invitation and signed invoice links. |
| `DBPROVIDER` | Selects `Sqlite` or `Postgres`. |
| `ConnectionStrings:SqliteConnection`, `ConnectionStrings:PostgresConnection` | Connection string for the chosen provider. |
| `SEED_DEMO_DATA` | Optional first-start seed control for a new SQLite or PostgreSQL database; defaults to enabled. Set `false` to start without the demo books. |
| `ASPNETCORE_ENVIRONMENT` | Set to `Development` in the API process environment, before startup. |

Do not copy the placeholder Resend key from `.env.example`: an absent `RESEND_API_KEY` selects the development console sender. The sample file documents optional email and assistant settings. `.env` is parsed as simple `KEY=value` lines, not executed as shell code; do not put command substitutions in it. Use colon-separated configuration keys in this file (for example `ConnectionStrings:PostgresConnection`). When exporting configuration directly in the shell, use double underscores instead (for example `ConnectionStrings__PostgresConnection`).

Use **SQLite** by default for local development, or switch to **PostgreSQL** if you want to test against Postgres. Both the application data and identity data use the selected database.

### SQLite (Default)

```bash
printf '\nDBPROVIDER=Sqlite\nConnectionStrings:SqliteConnection=Data Source=accountgo.db;Foreign Keys=True\n' >> src/Api/.env
```

The relative database path is resolved from the API content root, `src/Api`. On its first start the API creates and seeds `src/Api/accountgo.db`; startup upgrades existing SQLite schema. For an isolated file, supply an absolute path in the connection string. Back up local data before deleting or replacing it.

### PostgreSQL (Optional)

Start a PostgreSQL server and create an empty database and a login with permission to create tables, indexes, and run migrations. For example, on a workstation with Docker:

```bash
docker run --name accountgo-postgres -e POSTGRES_USER=accountgo \
  -e POSTGRES_PASSWORD=localdevpassword -e POSTGRES_DB=accountgo_local \
  -p 127.0.0.1:5432:5432 -v accountgo-postgres-data:/var/lib/postgresql/data \
  -d postgres:17
```

Wait for PostgreSQL to accept connections (`docker logs accountgo-postgres`). If port 5432 is in use, choose another host port and update the connection string. The credentials below are local examples; replace them if you use an existing server.

```bash
printf '\nDBPROVIDER=Postgres\nConnectionStrings:PostgresConnection=Host=localhost;Port=5432;Database=accountgo_local;Username=accountgo;Password=localdevpassword\n' >> src/Api/.env
```

If switching from SQLite, **replace** its `DBPROVIDER` line rather than leaving duplicate provider settings. The API applies PostgreSQL migrations on startup and seeds demo data into a new database. Set `SEED_DEMO_DATA=false` before first startup to omit demo books. Never point this local example at a production database.

## 3. Start both processes

From the repository root, start the API in terminal 1 and leave it running:

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --no-launch-profile --project src/Api/Api.csproj --urls http://localhost:8001
```

Wait for `Now listening on: http://localhost:8001`. In terminal 2, from the repository root:

```bash
cd src/Web
pnpm dev --host 127.0.0.1
```

Open **http://localhost:3000**. Vite proxies `/api` to `http://localhost:8001`; changing the API port requires updating the proxy in `src/Web/vite.config.ts`. Use the same `localhost` hostname for browser and API. Stop either process with Ctrl+C.

## 4. Sign in and enable your workspace screens

1. On the sign-in page choose **Sign up**, enter a local test email, accept the terms, and choose **Create Account & Send Code**.
2. With `ASPNETCORE_ENVIRONMENT=Development` set in the API command and no Resend key, the development code appears on the verification screen and is auto-filled. Choose **Complete Sign-Up & Enter**. For a later login, request a new code and choose **Verify & Enter AccountGo**. This account exists only in your selected local database.
3. Choose **New Organization**, select **Community**, and complete the workspace name, currency, books-start date, fiscal-year and reporting screens. Choose **Create workspace**. For test transactions dated in the past, choose a books-start date no later than those transactions.
4. Open **Team & Members → Set up my access**. For a disposable development workspace, select **Full workspace access** and choose **Save and activate roles**. An Owner workspace role grants administration; functional roles grant access to banking, sales, purchasing, reports, and other operational screens. Assign only the roles appropriate to the user in a real workspace. Journal posting can still require independent review.
5. Open **Banking → Add bank account**, provide a name and currency, and save. Open the account and choose **Import statement** to test file preview and import. The sidebar exposes the modules granted by the functional role and the workspace's enabled modules.

The local console sender does not send real email. If the code does not appear, check that the API is in Development and no `RESEND_API_KEY` is configured, then inspect its terminal output. Paid hosted plans, hosted plan gates and external integrations cannot all be reproduced by the community configuration alone.

## Database and startup troubleshooting

- **Signing-key error:** ensure `SECRET_KEY` in `src/Api/.env` is at least 32 bytes, then restart the API.
- **Connection refused or migrations fail:** check the selected provider, PostgreSQL server, credentials, database privileges, and API log. A second API process cannot bind the same port.
- **Screens missing or 403:** check the active workspace, **Team & Members** functional roles, and enabled modules. Owner alone does not grant all operational permissions.
- **Vite shows a proxy error:** confirm the API is listening on port 8001 and Vite on port 3000.
- **Switch databases:** stop the API, update `.env` to one provider and its connection string, and restart. Each database has separate users and workspaces.
- **Reset a disposable SQLite database:** stop the API, back up anything needed, and remove the selected SQLite file and its matching `-wal` and `-shm` files. Restart to create a new database. For PostgreSQL, create a separate empty database; do not drop a database containing data you need.

## Tests

From the repository root:

```bash
dotnet test test/AccountGo.BackendTests/AccountGo.BackendTests.csproj
cd src/Web
pnpm test
pnpm build
```

The focused statement-import suite can be run with `dotnet test test/AccountGo.BackendTests/AccountGo.BackendTests.csproj --filter 'FullyQualifiedName~BankStatementParserTests|FullyQualifiedName~BankStatementImportServiceTests|FullyQualifiedName~StatementIncomeRecorderTests|FullyQualifiedName~MultiTenancyPhase3BankingAcceptanceTests'`. Automated tests do not replace checking the full browser flow and external service integrations.
