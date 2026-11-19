# Architecture & Technology Stack

```
                          ┌──────────────────────────────────────┐
                          │         Cloudflare Workers           │
                          │   (React 19 SPA Static Assets)       │
                          │          app.accountgo.dev           │
                          └──────────────────┬───────────────────┘
                                             │  /api/* Proxy
                                             ▼
                          ┌──────────────────────────────────────┐
                          │        Cloudflare Containers         │
                          │   (.NET 10 ASP.NET Core Web API)     │
                          │          api.accountgo.dev           │
                          └──────────────────┬───────────────────┘
                                             │
                      ┌──────────────────────┴──────────────────────┐
                      ▼                                             ▼
          ┌───────────────────────┐                     ┌───────────────────────┐
          │     Entity Framework  │                     │    ASP.NET Identity   │
          │   Core (Domain Data)  │                     │   (Auth, RBAC, Tokens)│
          │  SQLite / PostgreSQL  │                     │  SQLite / PostgreSQL  │
          └───────────────────────┘                     └───────────────────────┘
```

| Layer | Technologies |
| :--- | :--- |
| **Frontend** | React 19, TypeScript, Vite 8, Tailwind CSS v4, Lucide React, React Router 7 |
| **Backend API** | .NET 10, ASP.NET Core Web API, C# 14 |
| **Data Access** | Entity Framework Core 10, SQLite, PostgreSQL, SQL Server support |
| **Security & Auth** | ASP.NET Core Identity, JWT Bearer, HTTP-only Refresh Cookies, Rate Limiting |
| **Orchestration** | .NET Aspire (`src.AppHost`, `src.ServiceDefaults`) |
| **Edge Compute** | Cloudflare Workers, Cloudflare Containers, Durable Objects |
| **CI / CD** | GitHub Actions CI (public); hosted Cloudflare deploy via private ops |

---

## Repository Structure

```
accountgo/
├── .github/workflows/          # Public CI + notify hosted ops (no prod resource IDs)
│   ├── ci.yml                  # Build API + Web
│   └── notify-ops.yml          # Manual notify → private AccountGo Cloud deploy
├── docs/                       # Project documentation & screenshots
│   ├── readme/                 # Modular README sections
│   └── screenshots/            # High-resolution application screenshots
├── src/
│   ├── Api/                    # ASP.NET Core Web API (.NET 10) & Worker adapter
│   ├── Core/                   # Domain entities, value objects, and interfaces
│   ├── Dto/                    # Data Transfer Objects for API contracts
│   ├── MigrationService/       # EF Core automated database migration runner
│   ├── Services/               # Business logic, repositories, and domain services
│   ├── src.AppHost/            # .NET Aspire local orchestration host
│   ├── src.ServiceDefaults/    # Aspire telemetry, health checks, and service discovery
│   └── Web/                    # React 19 + TypeScript + Vite + Tailwind CSS v4 SPA
└── test/
    ├── AccountGo.BackendTests/ # Domain and accounting logic tests
    └── AccountGo.ServicesTests/# Security, Administration, and Donations service tests
```
