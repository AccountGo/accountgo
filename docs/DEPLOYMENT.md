# Deployment Guide

This guide covers **self-hosting** AccountGo (Cloudflare Workers / Containers / D1, or Docker).  
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
        D1["Cloudflare D1 SQL"]
    end

    User --> DNS
    DNS --> WebWorker
    DNS --> APIContainer
    APIContainer --> D1
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

---

## 3. Self-host: Backend API (.NET 10)

### 3.1 Wrangler / Cloudflare Containers

```bash
cd src/Api
cp wrangler.example.jsonc wrangler.jsonc
# Set d1_databases[0].database_id to YOUR D1 database id
# Set vars (PUBLIC_* URLs, CORS, email from-addresses) for your domain
# Put secrets via: npx wrangler secret put SECRET_KEY
#                  npx wrangler secret put INTERNAL_SYNC_KEY
#                  npx wrangler secret put RESEND_API_KEY  (if using Resend)

pnpm install
npx wrangler d1 migrations apply <your-db-name> --remote
npx wrangler deploy
```

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
| `RESEND_API_KEY` | Resend API key when `EMAIL_PROVIDER=Resend` |
| `ANTER_API_KEY` / `ANTER_PROJECT_ID` | Optional AccountGo Assistant; set via secrets / env — do not commit |
| `JwtSettings__validIssuer` / `validAudience` | Match your public API origin |

> [!CAUTION]
> Never commit real secret keys, D1 database IDs for production SaaS, or Cloudflare API tokens to a public repository.

See `src/Api/.env.example` for local development placeholders.

---

## 5. CI on the public repository

Public GitHub Actions (`.github/workflows/`):

| Workflow | Purpose |
|---|---|
| `ci.yml` | Build API + Web on PRs and `main` (no production deploy) |
| `notify-ops.yml` | **Manual only** — optionally notifies private ops to deploy AccountGo Cloud (`workflow_dispatch`) |
| `dependabot.yml` | Dependency update PRs |

Self-hosters: wire your own deploy pipeline; do not expect `notify-ops` to deploy your account.
