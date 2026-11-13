# Deployment Guide

This guide covers **self-hosting** AccountGo (Cloudflare Workers / Containers with PostgreSQL, or Docker).  
**AccountGo Cloud** (the hosted product at `*.accountgo.dev`) is deployed from a private ops control plane and is not documented with production resource IDs here.

---

## 1. Deployment Architecture

```mermaid
flowchart LR
    subgraph Internet["Public Traffic"]
        User["Client Browser"]
    end

    subgraph CF["Cloudflare Platform"]
        DNS["Cloudflare DNS & SSL"]
        WebWorker["Cloudflare Worker (Static Assets SPA)"]
        APIContainer["Cloudflare Container / Worker (Backend API)"]
        PG["PostgreSQL (any host)"]
    end

    User --> DNS
    DNS --> WebWorker
    DNS --> APIContainer
    APIContainer --> PG
```

---

## 2. Self-host: Frontend (Web)

```bash
cd src/Web
cp wrangler.example.jsonc wrangler.jsonc   # if you need a fresh template
# Edit wrangler.jsonc: Worker name, API service binding, API_HOST

pnpm install
pnpm build
pnpm wrangler deploy
```

Use your own Cloudflare account. Do not reuse AccountGo Cloud resource IDs.

If serving the built SPA from a custom static web server or reverse proxy, serve `/version.json` with `Cache-Control: no-store` so open tabs learn about new versions.

---

## 3. Self-host: Backend API (.NET 10)

### 3.1 Wrangler / Cloudflare Containers

```bash
cd src/Api
cp wrangler.example.jsonc wrangler.jsonc
# Set vars (PUBLIC_* URLs, CORS, email from-addresses) for your domain; keep DatabaseProvider = "Postgres"
# Put secrets via: npx wrangler secret put SECRET_KEY
#                  npx wrangler secret put INTERNAL_SYNC_KEY
#                  npx wrangler secret put ConnectionStrings__PostgresConnection
#                  npx wrangler secret put RESEND_API_KEY  (if using Resend)

pnpm install
npx wrangler deploy
```

The container's disk is not persistent, so the Worker only starts it with `DatabaseProvider = "Postgres"`. The
connection string is a `postgresql://user:password@host:5432/database` URI or an Npgsql `Host=...;Database=...` string;
it must be reachable over IPv4. The API applies its migrations on start.

**Connection pooling.** Each API container keeps its own pool of database connections, and every request can use
two of them. A session-mode pooler (Supabase's on port 5432) holds a server connection for every open client
connection, so a few busy containers can use up its limit and requests then fail. Use a transaction-mode pooler
(Supabase's on port 6543) and cap the pool in each container:

```
Host=<pooler host>;Port=6543;Database=postgres;Username=<user>;Password=<password>;SSL Mode=Require;No Reset On Close=true;Maximum Pool Size=15;Timeout=30
```

- `No Reset On Close=true`: a transaction pooler hands each transaction to any server connection, so resetting
  session state on close does nothing useful.
- `Maximum Pool Size`: connections one container may open. Keep the total over all containers within the pooler's
  client limit.
- AccountGo keeps no session state between transactions (no session advisory locks, no server-prepared
  statements), and migrations take their lock inside a transaction, so both pooler modes work.

### 3.2 Docker (any host)

```bash
docker build -t accountgo-api:latest -f src/Api/Dockerfile .
docker run -d \
  -p 8001:8001 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e ASPNETCORE_URLS=http://+:8001 \
  -e SECRET_KEY="$(openssl rand -hex 32)" \
  --name accountgo-api \
  accountgo-api:latest
```

---

## 4. Environment Variables & Secrets

| Variable | Description |
| -------- | ----------- |
| `SECRET_KEY` | ≥32-byte secret for JWT / OTP (Cloudflare Worker secret) |
| `INTERNAL_SYNC_KEY` | Dedicated key for `/api/internal/*` (must differ from `SECRET_KEY`) |
| `DatabaseProvider` | `Sqlite` (default), `Postgres` or `SqlServer`. Cloudflare Containers require `Postgres` |
| `ConnectionStrings__PostgresConnection` | Postgres connection string (secret) when `DatabaseProvider=Postgres` |
| `RESEND_API_KEY` | Resend API key when `EMAIL_PROVIDER=Resend` |
| `ANTER_API_KEY` / `ANTER_PROJECT_ID` | Optional AccountGo Assistant; set via secrets / env — do not commit |
| `JwtSettings__validIssuer` / `validAudience` | Match your public API origin |
| `SUPPORT_EMAIL` | Shown when a quota is exceeded |
| `SERVER_TIMING_ENABLED` | Optional. Default `"true"`. When `"false"`, no `Server-Timing` header from the .NET API, API worker, or web worker |
| `AuditRetention__Enabled` | Optional. Default `true`. Daily background purge of expired security / data-access events |
| `AuditRetention__SecurityEventDays` | Optional. Default `730`. Days to keep security events (raised to 730 if lower) |
| `AuditRetention__DataAccessDays` | Optional. Default `365`. Days to keep `data.*` / `invoice.pdf.public` events (raised to 365 if lower) |
| `Google__Enabled` | Optional. Default `false`. Enable Google Sign-In |
| `Google__ClientId` | Google OAuth 2.0 Web Client ID (required if `Google__Enabled` is true) |
| `Google__HostedDomain` | Optional. Restrict Google Sign-In to a specific Google Workspace domain (e.g. `company.com`) |

> [!CAUTION]
> Never commit real secret keys, database connection strings, or Cloudflare API tokens to a public repository.

See `src/Api/.env.example` for local development placeholders.

### Google Sign-In Setup & Headers

When enabling Google Sign-In (`Google__Enabled=true`), ensure your web server and edge proxy configure appropriate CSP and COOP headers:

#### 1. Content Security Policy (CSP)
Allow Google Identity Services (GIS) assets:
- `script-src`: `https://accounts.google.com/gsi/client`
- `frame-src`: `https://accounts.google.com/gsi/`
- `connect-src`: `https://accounts.google.com/gsi/`
- `style-src`: `https://accounts.google.com/gsi/style`

#### 2. Cross-Origin-Opener-Policy (COOP)
- **Requirement**: `Cross-Origin-Opener-Policy` must either be omitted or set to `same-origin-allow-popups`.
- **Caution**: Never set COOP to `same-origin`. A value of `same-origin` severs the `window.opener` reference required by the Google Sign-In One Tap and popup authentication flow, breaking sign-in.

### Audit retention

The API keeps **minimum retention periods** for security and data-access events written to `SecurityAuditEvents`:

| Category | Minimum | Notes |
|---|---|---|
| Security events (sign-in, access denied, membership, …) | 730 days | Everything that is not a data-access event |
| Data-access events | 365 days | `data.*` and `invoice.pdf.public` |

Configuration may **lengthen** these periods; values below the floors are raised to the floors (and a warning is logged). Database triggers enforce the same floors so a delete cannot shorten retention without a new migration.

**Change and evidence records** (`RecordChanges`, and `AuditLog` rows whose `TableName` starts with `__`) are kept for as long as the workspace's books. The retention job never touches them.

Operators are responsible for meeting local legal requirements; these are operational minimums, not legal advice.

A single daily background run deletes expired rows in batches. On Postgres, parallel API instances use an advisory lock so only one run proceeds. Each purge that deletes anything writes one `audit.retention.purge` security event with counts and cutoffs (no event content).

### Reading Server-Timing (latency)

When `SERVER_TIMING_ENABLED` is on (default), each API response carries a `Server-Timing` header that **appends** a segment per hop (never overwrites earlier hops):

| Segment | Meaning |
|---|---|
| `app` | .NET pipeline time to response **headers** |
| `db` | Summed EF command time; `desc` is the query count |
| `container` | API worker → container fetch (includes DO routing) |
| `cold` | Present with `desc="1"` when this request started/woke the container |
| `worker` | API worker total to header time |
| `proxy` | Web worker `env.API` / `API_HOST` fetch |

In Chrome/Edge DevTools: **Network** → select the request → **Timing** (or Headers → `Server-Timing`). Durations are milliseconds. Slow requests also emit one structured log line (route **template** only — no ids or query strings).

Unauthenticated bench (never hits `/api/auth/*`):

```bash
node scripts/latency-bench.mjs 20
# or: node scripts/latency-bench.mjs 10 https://api.accountgo.dev/health
```

Streaming responses (SSE): workers wrap `new Response(res.body, { headers })` without reading the body; `101` upgrades pass through untouched. Manual check: open an SSE endpoint and confirm events still arrive incrementally while `Server-Timing` is present on the response headers.

---

## 5. CI on the public repository

Public GitHub Actions (`.github/workflows/`):

| Workflow | Purpose |
|---|---|
| `ci.yml` | Build API + Web on PRs and `main` (no production deploy) |
| `notify-ops.yml` | **Manual only** — optionally notifies private ops to deploy AccountGo Cloud (`workflow_dispatch`) |
| `dependabot.yml` | Dependency update PRs |

Self-hosters: wire your own deploy pipeline; do not expect `notify-ops` to deploy your account.
