# Multi-Tenancy Implementation Plan

Status: **Ready for implementation** · Owner: @mvperez · Reviewer: Claude (code review gate before each merge)

This plan is written for an implementing agent. Work phase by phase. **Each phase is one PR** on its own branch (`feat/mt-phase-N-<name>`), and each PR must pass review before the next phase starts. Do not merge to `main` or deploy without review sign-off.

---

## 0. Context and definitions

**Workspace = Organization = Company = Tenant.** These are the same thing:

| Layer | Name used |
|---|---|
| UI copy, emails | "workspace", "organization" |
| URL | `/org/:orgSlug/*` |
| Database / C# | `Company` table, `Core.Domain.Company` |

Use **"Company"** in backend code and **"workspace"** in user-facing copy. Do not introduce a fourth name ("Tenant", "Organization") in the schema.

### 0.1 Current state (verified 2026-10-21 against local `src/Api/accountgo.db` and remote D1 `accountgo-db`)

- One `Company` row: `Id=1`, `Financial Solutions Inc.`, slug `ph-financials`. Three users in `AspNetUsers`.
- **No user↔company relationship exists.** `AspNetUsers`, `User`, `SecurityUserRole` have no company column; there is no owner, membership, or invitation table.
- Roles (`SecurityRole` / `SecurityUserRole`) are **global**. Signup (`AuthController.SignUp`, `src/Api/Controllers/AuthController.cs:171`) gives every new user the global `SystemAdministrators` role and creates a company with no owner link.
- `GET /api/Administration/companies` and `POST /api/Administration/company` are `[AllowAnonymous]` and return/create **all** companies. `SubscriptionController` has 4 `[AllowAnonymous]` actions.
- Only `Account`, `GeneralLedgerSetting`, `CompanySetting` have `CompanyId`. All other business tables (customers, vendors, items, invoices, receipts, GL, fiscal years, sequence numbers, taxes, …) are shared. `AccountService.cs:23`, `Initializer.cs:242,372`, `DatabaseSeeder.cs:98` hardcode `CompanyId = 1`.
- `GetDefaultCompany()` (first row in `Company`) is used for module gating (`AdministrationService.IsModuleEnabled`), subscription lookup, invoice printing (`SalesController.cs:355,1759`), and admin endpoints.
- The frontend `:orgSlug` only selects a name/plan for display (`src/Web/src/layouts/OrgLayout.tsx`); it never scopes API data. Role is hardcoded `'Admin'`. `Members.tsx` manages **global** users via `/api/Administration/users`.
- **Schema management on SQLite is not migration-based.** `Program.cs:306` calls `CreateTables()` only when the DB is empty; `Migrate()` runs only for SQL Server. Columns like `Company.Slug` were added by hand to `accountgo.db` and pushed to D1 via `scripts/sync-d1.py`. The Docker image ships `accountgo.db`; on cold start `worker.ts` restores a **fixed list of 14 tables** from D1 (`Company` is not among them).
- Writes reach D1 via `D1SqlGenerator` → `x-d1-sync-sql` response header → `worker.ts` executes on D1. This covers every EF `SaveChanges` in `ApiDbContext` and `ApplicationIdentityDbContext`.

### 0.2 Target behaviour

1. A person registers once (global identity) and signs in with email OTP.
2. A user can **create a workspace**; the creator becomes its **Owner**.
3. A user can be **invited** to an existing workspace by its Owner/Admin, and joins with a role.
4. A user can belong to many workspaces and switch between them; they see **only** workspaces they are a member of.
5. Every piece of business data belongs to exactly one workspace and is never readable or writable from another.
6. Permissions (Accountant, Sales Rep, …) are granted **per workspace**, not globally.
7. Subscription/module gating uses the **current** workspace, not the first company row.

---

## Phase 0 — Security hotfixes (ship first, independent of multi-tenancy)

These are live vulnerabilities. Small PR, deploy as soon as reviewed.

| # | Problem | Fix |
|---|---|---|
| 0.1 | Production returns the OTP to the browser (`devCode`) for any email — anyone can sign in as anyone. Cause: `EmailOtpService` checks `Email:Provider == "Console"`, and `appsettings.json:30` sets it. | Return `devCode` **only** when `_emailSender is DevConsoleEmailSender` **and** `IHostEnvironment.IsDevelopment()`. Inject `IHostEnvironment`. Apply to `SendOtpAsync` and `SendWelcomeOtpAsync`. |
| 0.2 | `BaseController.GetCurrentUserName()` falls back to a client-supplied `UserName` header. | Remove the header fallback; return `null`/throw when unauthenticated. Fix callers. |
| 0.3 | Hardcoded fallback internal key in `src/Api/worker.ts` (`restoreD1ToContainer`) and `InternalSyncController.cs`. | Remove fallbacks. If `SECRET_KEY` is missing: worker skips restore and logs an error; controller returns 503. Use constant-time comparison (`CryptographicOperations.FixedTimeEquals`). |
| 0.4 | `.env` (Resend key, `SECRET_KEY`) is copied into the Docker image — no `.dockerignore`. | Add repo-root `.dockerignore` excluding `**/.env`, `**/node_modules`, `**/bin`, `**/obj`, `**/.wrangler`, `src/Web/dist`. |
| 0.5 | `/restart-container` path and `x-restart-container` header in `worker.ts` are unauthenticated. | Require `x-internal-key` == `SECRET_KEY` (constant-time), else 404. |
| 0.6 | Container cold-start/DO errors return bodies without `detail`, so the web shows a generic "Unable to send verification code". | In `worker.ts`, return RFC 7807 bodies (`{title, detail, status}`) for 503/504/500. Return 503 + `Retry-After: 5` for startup timeout. In `AuthContext.tsx`, show "Service is starting up, please retry in a few seconds" on 503/504. |
| 0.7 | Unhandled exceptions in production return an empty 500. | Add `app.UseExceptionHandler()` + `app.UseStatusCodePages()` (ProblemDetails already registered). Never leak stack traces in production. |

**Acceptance:** On the live site, `send-otp` response has no `devCode`; `curl -X POST .../restart-container` → 404; `docker run --rm <image> ls -a /app` shows no `.env`.

> Rotate `SECRET_KEY` and `RESEND_API_KEY` after 0.3/0.4 ship (they were baked into published images). The owner does the rotation; the implementor documents it in the PR.

---

## Phase 1 — Data model: membership, invitations, tenant columns

### 1.1 New entities (`src/Core/Domain/Tenancy/`)

```csharp
public enum CompanyMemberRole { Owner = 1, Admin = 2, Member = 3 }
public enum CompanyMemberStatus { Active = 1, Suspended = 2 }

[Table("CompanyMember")]
public class CompanyMember : BaseEntity
{
    public int CompanyId { get; set; }
    public string UserId { get; set; } = "";   // AspNetUsers.Id (== JWT NameIdentifier)
    public CompanyMemberRole Role { get; set; }  // governance: who can manage the workspace
    public CompanyMemberStatus Status { get; set; } = CompanyMemberStatus.Active;
    public DateTime JoinedAt { get; set; }
    public string? InvitedByUserId { get; set; }
    public virtual Company Company { get; set; } = null!;
    public virtual ICollection<CompanyMemberSecurityRole> SecurityRoles { get; set; } = new List<CompanyMemberSecurityRole>();
}

[Table("CompanyMemberSecurityRole")]   // per-workspace functional roles (Accountant, Sales Rep, …)
public class CompanyMemberSecurityRole : BaseEntity
{
    public int CompanyMemberId { get; set; }
    public int SecurityRoleId { get; set; }
}

[Table("CompanyInvitation")]
public class CompanyInvitation : BaseEntity
{
    public int CompanyId { get; set; }
    public string Email { get; set; } = "";             // normalized lower-case
    public CompanyMemberRole Role { get; set; }          // Admin or Member only (never Owner)
    public string SecurityRoleIdsJson { get; set; } = "[]";
    public string TokenHash { get; set; } = "";          // SHA-256 base64 of raw token; raw token never stored
    public DateTime ExpiresAt { get; set; }              // now + 7 days
    public DateTime CreatedAt { get; set; }
    public string InvitedByUserId { get; set; } = "";
    public DateTime? AcceptedAt { get; set; }
    public string? AcceptedByUserId { get; set; }
    public DateTime? RevokedAt { get; set; }
}
```

Constraints / indexes:
- `CompanyMember`: unique `(CompanyId, UserId)`; index `UserId`.
- `CompanyMemberSecurityRole`: unique `(CompanyMemberId, SecurityRoleId)`.
- `CompanyInvitation`: unique `TokenHash`; index `(CompanyId, Email)`. At most one **pending** invite per `(CompanyId, Email)` — enforce in service code (re-inviting revokes the old one).
- Add to `Company`: `CreatedAt` (TEXT), `CreatedByUserId` (TEXT, nullable). Ownership lives in `CompanyMember` — **do not** add `OwnerUserId` to `Company` (a single column cannot represent ownership transfer or multiple owners cleanly).

Rules:
- Each company has **≥ 1 Owner** at all times. The last Owner cannot leave, be removed, or be demoted; they must transfer ownership first.
- `Owner` and `Admin` implicitly have all permissions in that workspace. `Member` permissions come only from `CompanyMemberSecurityRole`.
- `SecurityRole` stays a global catalog of role **templates** (`Accountant`, `Sales Representative`, `Purchasing Agent`, `Auditor / Viewer`, `GeneralUsers`). Do not assign them via `SecurityUserRole` any more (see Phase 3).
- `SystemAdministrators` becomes **platform staff only** and grants **no** implicit workspace access (Decision D2).

### 1.2 Tenant column on business tables

Introduce in `Core`:

```csharp
public interface ICompanyOwned { int CompanyId { get; set; } }
```

Add `public int CompanyId { get; set; }` + `ICompanyOwned` to **every** entity below. Child/line tables get it too (defence in depth, simple filters, and lets D1 data be exported per tenant).

**Tenant-owned (add `CompanyId`):**
`Party`, `Contact`, `Address`, `CustomerContact`, `VendorContact`, `Customer`, `Vendor`, `Donor`, `Item`, `ItemCategory`, `Measurement`, `InventoryControlJournal`, `Bank`, `Account` (exists), `MainContraAccount`, `FiscalYear`, `GeneralLedgerSetting` (exists, make non-null), `GeneralLedgerHeader`, `GeneralLedgerLine`, `JournalEntryHeader`, `JournalEntryLine`, `SalesQuoteHeader/Line`, `SalesOrderHeader/Line`, `SalesDeliveryHeader/Line`, `SalesInvoiceHeader/Line`, `SalesReceiptHeader/Line`, `CustomerAllocation`, `PurchaseOrderHeader/Line`, `PurchaseReceiptHeader/Line`, `PurchaseInvoiceHeader/Line`, `VendorPayment`, `DonationInvoiceHeader/Line`, `Tax`, `TaxGroup`, `ItemTaxGroup` (and the `TaxSystem/*` variants), `PaymentTerm`, `SequenceNumber`, `CompanySetting` (exists), `AuditLog`, `Log`.

**Global (no `CompanyId`):** `Company`, `AccountClass`, `SecurityRole`, `SecurityRolePermission`, `SecurityPermission`, `SecurityGroup`, `SystemModule`, `User`, `AuditableEntity`, `AuditableAttribute`, Identity tables.

> If an entity is not in either list, stop and ask in the PR description — do not guess.

Composite uniqueness must become per-company. At minimum:
- `Account (CompanyId, AccountCode)`
- `SequenceNumber (CompanyId, SequenceNumberType)`
- `Customer (CompanyId, No)`, `Vendor (CompanyId, No)`, `Item (CompanyId, No)` (or whichever document-number columns exist)
- Any existing single-column unique index on a tenant table → review and scope to `CompanyId`.

### 1.3 Schema change delivery (SQLite + D1 + SQL Server)

Because SQLite does not use EF migrations here, deliver schema in **three** synchronized places:

1. **EF model** (`ApiDbContext`, configurations) — source of truth for fresh databases and SQL Server. Add an EF migration for SQL Server (`dotnet ef migrations add MultiTenancy -c ApiDbContext`).
2. **SQLite upgrader** — new `Api/Data/Schema/SqliteSchemaUpgrader.cs`, run at startup in `Program.cs` **after** `CreateTables()` and **before** seeding. Idempotent: checks `pragma_table_info` and `sqlite_master`, then `ALTER TABLE … ADD COLUMN "CompanyId" INTEGER NOT NULL DEFAULT 1`, `CREATE TABLE IF NOT EXISTS`, `CREATE [UNIQUE] INDEX IF NOT EXISTS`. Keep a `__SchemaVersion` table (`Version INTEGER`) and apply numbered steps once. Drop/replace old single-column unique indexes.
3. **D1 migration** — `src/Api/d1/migrations/0001_multi_tenancy.sql` with the same DDL (D1 is plain SQLite). Add `"migrations_dir": "d1/migrations"` to the `d1_databases` entry in `src/Api/wrangler.jsonc` and apply with `npx wrangler d1 migrations apply accountgo-db --remote`. Must be idempotent-safe to re-run, or rely on wrangler's migration tracking.

`DEFAULT 1` is the **backfill**: all existing rows belong to `ph-financials` (Id 1). After backfill, new rows must always get an explicit `CompanyId` from the tenant context (Phase 2); `DEFAULT 1` must not be relied on — Phase 2 adds a `SaveChanges` guard that throws if `CompanyId == 0`, and the upgrader/EF model should not give C# a default.

Regenerate the shipped `src/Api/accountgo.db` by running the API locally once against it (upgrader applies), then verify with `sqlite3` (see acceptance).

### 1.4 Backfill memberships for existing data

In the same migration/upgrader step (idempotent, `INSERT … WHERE NOT EXISTS`):
- `Company 1` memberships per **Decision D1**: `marvinperez@gmail.com` = Owner, `hello@marvinperez.com` = Admin, `admin@accountgo.ph` = Member + `Auditor / Viewer`.
- Remove `SystemAdministrators` from `SecurityUserRole` for every user except the platform-staff list (D2).

### 1.5 D1 restore must cover everything

In `src/Api/worker.ts` `restoreD1ToContainer`:
- Replace the hardcoded 14-table list with a dynamic list from D1: `SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '_cf_%' AND name != 'd1_migrations' AND name != '__SchemaVersion'`.
- Order inserts parent-before-child, or run the restore with `PRAGMA foreign_keys=OFF` inside one transaction on the .NET side (`InternalSyncController`) and re-enable after. Prefer the latter.
- Use the D1 column list per row (already done) — the image DB must have all columns D1 has; the upgrader guarantees this because it runs before the container reports ready.
- Batch: send in chunks (e.g. 500 statements per request) so large tenants don't exceed request limits.
- **The readiness probe must not succeed before restore finishes**, otherwise the first requests after a cold start see seed data instead of D1 data. Make restore part of readiness: the worker awaits restore before forwarding the first request (not `waitUntil`), or the API exposes `/api/internal/ready` that returns 200 only after a restore-complete flag is set.

**Acceptance (Phase 1):**
- `sqlite3 src/Api/accountgo.db "select count(*) from pragma_table_info('SalesInvoiceHeader') where name='CompanyId'"` → `1` for every tenant table in §1.2.
- Same query against D1 (`wrangler d1 execute accountgo-db --remote --command …`) → `1`.
- `select * from CompanyMember` shows the D1 backfill. Fresh empty DB start → same schema as upgraded DB (compare `sqlite_master` DDL with a script; include it in the PR).
- App still builds, all existing tests pass. No behaviour change yet except the restore fix.

---

## Phase 2 — Tenant resolution and data isolation (the core)

### 2.1 Tenant context

`src/Core/Tenancy/ITenantContext.cs` (in `Core` so `Services` can depend on it):

```csharp
public interface ITenantContext
{
    int? CompanyId { get; }            // null = no tenant resolved (auth/org-list endpoints)
    string? CompanySlug { get; }
    CompanyMemberRole? Role { get; }
    IReadOnlySet<string> Permissions { get; }
    bool IsResolved { get; }
    int RequireCompanyId();            // throws TenantNotResolvedException if null
}
```

Scoped implementation `TenantContext` in `Api`, populated by middleware.

### 2.2 How the client names the tenant

- Header: **`X-AccountGo-Org: <slug>`** on every tenant-scoped API call.
- Header (not route prefix) keeps existing controller routes unchanged. Slug (not id) matches the URL the user sees.
- Slug changes (Settings) must keep working: resolve slug → `CompanyId` per request; the frontend updates the header when the slug changes.

### 2.3 `TenantResolutionMiddleware`

Placed **after** `UseAuthentication()` and **before** `UseAuthorization()`/`MapControllers()` in `Program.cs`.

1. If header absent → leave context unresolved, continue.
2. If present but request is unauthenticated → 401.
3. Look up company by slug (exact match on `Slug` only — **remove** the `ShortName`/`CompanyCode` fallback matching in `AdministrationService.GetCompanyBySlug`, it lets one slug alias another company).
4. Look up `CompanyMember` for `(CompanyId, NameIdentifier claim)` with `Status = Active`.
5. Company not found **or** not a member → **404** ProblemDetails `{ title: "Workspace not found" }`. Same response for both (no existence oracle).
6. Load role + effective permissions (Owner/Admin = all; Member = union of `SecurityRolePermission` for their `CompanyMemberSecurityRole`s). Cache per `(UserId, CompanyId)` in `IMemoryCache` for ≤ 60 s; invalidate on membership/role changes.
7. Populate `TenantContext`.

### 2.4 Endpoint classification

Add `[TenantScoped]` (requires resolved tenant; 400 `"X-AccountGo-Org header required"` if missing) and `[TenantFree]` attributes. Enforce with a global filter: **every controller action must have exactly one**, otherwise a startup self-check throws (write a reflection test too).

| Tenant-free | Tenant-scoped (everything else) |
|---|---|
| `AuthController` (all) | Sales, Purchasing, Financials, Inventory, Donations, Donors, Tax, Dashboard, Contact, Common, Account, Audit |
| `InternalSyncController` | `SubscriptionController` (current/update) |
| `OrganizationsController`: list mine, create | Administration (company settings, users→members, roles, payment terms, …) |
| `InvitationsController`: preview, accept | `OrganizationsController`: members, invitations, transfer, leave, delete |
| health | |

Remove every `[AllowAnonymous]` from `AdministrationController` and `SubscriptionController` unless explicitly listed here (plan definitions list may stay anonymous for the marketing/pricing page — confirm in PR).

### 2.5 EF Core isolation

In `ApiDbContext`:
- Inject `ITenantContext` (optional ctor param so design-time/migrations still work).
- In `OnModelCreating`, for every entity type implementing `ICompanyOwned`, apply `HasQueryFilter(e => e.CompanyId == _tenantCompanyId)` via a generic helper. The filter must reference a **context instance member** (e.g. `private int? CurrentCompanyId => _tenant?.CompanyId;`) so EF parameterizes it per context instance.
- When tenant is unresolved, the filter must return **no rows** (`CompanyId == -1` semantics), never all rows. Startup/seed/provisioning code that legitimately crosses tenants must use an explicit `TenantContext` set to the target company (`using (tenantContext.Impersonate(companyId))` scoped helper), **not** `IgnoreQueryFilters()`.
- `IgnoreQueryFilters()` is allowed **only** in: `InternalSyncController`, `SqliteSchemaUpgrader`, `CompanyProvisioningService`. Add a test that greps the solution and fails on any other usage.
- In both `SaveChanges` overrides (before `base.SaveChanges`), for each `ICompanyOwned` entry:
  - `Added` with `CompanyId == 0` → set to `RequireCompanyId()`.
  - `Added`/`Modified` with `CompanyId != current` → throw `CrossTenantWriteException`.
  - `Modified` where `CompanyId` property is modified → throw.
- Note: the D1 SQL generator runs after `SaveChanges` and will include `CompanyId` automatically — verify in a test that the emitted SQL contains it.

### 2.6 Cross-tenant reference validation

Query filters stop reads, but a request can still **reference** another tenant's id in a foreign key (e.g. create an invoice in A with `customerId` from B). For every create/update path that accepts foreign-key ids from the client (customerId, vendorId, itemId, accountId, taxId, paymentTermId, bankId, fiscalYearId, measurementId, invoice/order ids for allocations, …):
- Load the referenced entity through the filtered repository; if `null` → 400/404. Never assign a raw FK id from a DTO without this lookup.
- Add a reusable helper `ITenantGuard.EnsureOwned<T>(int id)` and use it across services.
- List every such path in the PR description with a ✅ — reviewer will check them one by one.

### 2.7 Remove "default company" assumptions

- Delete `IAdministrationService.GetDefaultCompany()`. Replace every call site (`AdministrationService.cs:375,413,442`, `AdministrationController.cs:98,659`, `SalesController.cs:355,1759`, `Initializer.cs:174`) with the current tenant's company.
- `IsModuleEnabled(moduleKey)` → uses `ITenantContext.CompanyId`. `RequireModuleAttribute` must run **after** tenant resolution; if tenant unresolved → 400.
- `GetCurrentSubscription(slug)` / `UpdateSubscription` → current tenant only; ignore slug/companyId in the body (or reject mismatch). Subscription changes require Owner.
- `AccountService.cs:23` / `Initializer.cs` / `DatabaseSeeder.cs` → no `CompanyId = 1` literals; seeding goes through provisioning (Phase 3.4) for a given company.
- `FinancialService.GetGeneralLedgerSetting(int? companyId = null)` → drop the parameter; use tenant.
- `AdministrationService.SaveCompany` updates must target the current tenant only.

### 2.8 Authorization per workspace

- `RequirePermissionAttribute` → check `ITenantContext.Permissions` (not `securityService.CheckPermission(username)` which is global).
- New `[RequireWorkspaceRole(CompanyMemberRole.Admin)]` (Owner satisfies Admin).
- `/api/auth/me` stops returning global roles/permissions for workspace UI. Add `GET /api/organizations/current` (tenant-scoped) returning `{ id, name, slug, plan, activeModules, subscriptionStatus, role, permissions }`.

**Acceptance (Phase 2) — required automated tests** (in `test/AccountGo.BackendTests`, using `WebApplicationFactory` + an in-memory/temp-file SQLite DB):
1. Seed companies A and B, users UA (member of A only) and UB (member of B only).
2. UA lists customers/invoices/accounts/items/journal entries with `X-AccountGo-Org: a` → sees only A's rows.
3. UA with `X-AccountGo-Org: b` → 404 on every tenant endpoint. Same 404 for a non-existent slug.
4. UA `GET /api/Sales/customer/{idOfB}` (and equivalent for each module) with header A → 404.
5. UA creates an invoice in A referencing B's customer id / item id / account id → 400/404, and no row is written.
6. Created rows have `CompanyId = A`; emitted `x-d1-sync-sql` contains `"CompanyId"` with A's id.
7. Tenant-scoped endpoint without header → 400. Unauthenticated → 401.
8. Reflection test: every controller action has `[TenantScoped]` or `[TenantFree]`.
9. Grep test: `IgnoreQueryFilters` only in the allowed files.
10. Module gating: A on `invoicing` plan gets 403 `MODULE_NOT_SUBSCRIBED` on financials; B on `accounting` passes — same user if member of both.

---

## Phase 3 — Workspace lifecycle, membership, invitations (API)

New `OrganizationsController` (`api/organizations`) and `InvitationsController` (`api/invitations`). Retire the company endpoints in `AdministrationController` (`companies`, `company` POST) — keep them as thin aliases returning the new behaviour for one release, or remove and update the frontend in the same PR.

### 3.1 Endpoints

| Method & path | Scope | Who | Behaviour |
|---|---|---|---|
| `GET /api/organizations` | free | authenticated | Workspaces where caller is an active member: `{id,name,slug,plan,role,activeModules,subscriptionStatus}` |
| `POST /api/organizations` | free | authenticated | Create workspace `{name, slug?, plan?, currency?}`; caller becomes Owner; runs provisioning (3.4) in **one transaction**; 409 on slug taken |
| `GET /api/organizations/current` | scoped | member | See 2.8 |
| `PATCH /api/organizations/current` | scoped | Admin+ | Name, slug, settings. Slug change validated + unique |
| `DELETE /api/organizations/current` | scoped | Owner | Requires body `{confirmSlug}`. Soft-delete (`Company.DeletedAt`); hide from lists; hard-delete is out of scope |
| `GET /api/organizations/current/members` | scoped | member | `{memberId,userId,email,firstName,lastName,role,securityRoles[],status,joinedAt}` |
| `PATCH /api/organizations/current/members/{memberId}` | scoped | Admin+ | Change `role` (Admin/Member) and `securityRoleIds`. Admin cannot change Owners; cannot demote last Owner |
| `DELETE /api/organizations/current/members/{memberId}` | scoped | Admin+ (or self = leave) | Remove member. Last Owner cannot be removed/leave. Revoke cached permissions |
| `POST /api/organizations/current/transfer-ownership` | scoped | Owner | `{memberId}` → target becomes Owner; caller becomes Admin. Target must be active member |
| `GET /api/organizations/current/invitations` | scoped | Admin+ | Pending invitations |
| `POST /api/organizations/current/invitations` | scoped | Admin+ | `{email, role: Admin|Member, securityRoleIds[]}`. If already a member → 409. Re-invite revokes previous pending. Sends email |
| `POST /api/organizations/current/invitations/{id}/resend` | scoped | Admin+ | New token, new expiry, resend email; rate-limited |
| `DELETE /api/organizations/current/invitations/{id}` | scoped | Admin+ | Revoke |
| `GET /api/invitations/{token}` | free, **anonymous** | anyone with link | Preview `{workspaceName, inviterName, role, emailMasked, expired, accepted}`. 404 if unknown/revoked. Rate-limited per IP |
| `POST /api/invitations/{token}/accept` | free | authenticated | Email of caller must equal invitation email (case-insensitive) → else 403 "This invitation was sent to a different email". Creates membership + security roles; marks accepted; single use; expired → 410 |

Per **Decision D3**: Admins invite and manage Members only; granting `Admin` (invite or promotion) requires Owner.

### 3.2 Signup and login flows

Change `AuthController.SignUp`:
- **Stop** assigning `SystemAdministrators` (or any global role).
- Accept optional `invitationToken`. If present: no workspace is created; after OTP verification the client calls accept (or `verify-otp` accepts it server-side when `invitationToken` is passed — pick one, document it).
- If `organizationName` present (and no invite): create workspace + Owner membership + provisioning **after** the identity user is created, in one transaction; on failure roll back both (today a failure leaves an orphan user).
- If neither: user is created with no workspace; after login, the frontend shows SelectOrg with "Create workspace" and any pending invites for their email.
- Add `GET /api/invitations/pending` (free, authenticated): pending invites addressed to the caller's verified email, so users who signed up without the link still see them.

`verify-otp` response: include `organizations: [{slug,name,role}]` so the client can route straight to the single workspace or to SelectOrg.

### 3.3 Invitation email

Use `IEmailSender` (Resend). Link: `{PUBLIC_WEB_URL}/invite/{rawToken}`. Add `PUBLIC_WEB_URL` to `wrangler.jsonc` vars (`https://accountgo-web.anter.workers.dev`) and `.env.example`. Subject: `"{inviter} invited you to {workspace} on AccountGo"`. HTML-encode every interpolated value. Token: 32 random bytes, base64url; store SHA-256 only. Expiry 7 days. Rate limit: 20 invites / workspace / hour; 5 resends / invite / day.

### 3.4 `CompanyProvisioningService`

Everything a new workspace needs, extracted from `Initializer.cs` / `DatabaseSeeder.cs` (which today seed company 1 only):
- Chart of accounts (from `Api/Data/coa.csv` / `ChartOfAccounts.csv`) with `CompanyId = new`.
- `GeneralLedgerSetting`, current `FiscalYear`, `SequenceNumber` rows for each `SequenceNumberTypes`, default `PaymentTerm`s, `Measurement`s, default `Tax`/`TaxGroup`s, `CompanySetting` defaults (currency from request, default PHP).
- **No** sample customers/invoices for new workspaces (sample data stays only for the demo company, if at all).
- Idempotent per company (safe to re-run). Used by `POST /api/organizations`, signup, and startup seeding of company 1.

### 3.5 Legacy global user/role management

- `AdministrationController` `users` endpoints (`GET/POST/PUT/DELETE /api/Administration/users`) manage **global** users → replace with the members/invitations endpoints above. Creating a user directly by an admin is removed — invite instead.
- `SecurityUserRole` stays only for platform staff (`SystemAdministrators`). `SecurityService.CheckPermission(username)` is no longer used for workspace authorization.

**Acceptance (Phase 3)** — automated tests:
1. Create workspace → caller is Owner; provisioning created CoA/GL settings/sequence numbers scoped to the new company; creating an invoice there works immediately.
2. Invite → email sent (assert via fake `IEmailSender`) → accept with wrong email = 403, right email = member with given security roles; second accept = 410/404; expired = 410; revoked = 404.
3. Last-owner protections: leave/remove/demote last Owner → 409. Transfer works.
4. Member without `Sales.Create` permission → 403 on invoice create; Admin → allowed.
5. Signup with org creates Owner membership and **no** global `SystemAdministrators` role. Signup with invite token creates no workspace and joins the invited one.
6. User with zero memberships: `GET /api/organizations` → `[]`; any tenant header → 404.

---

## Phase 4 — Frontend (`src/Web`)

1. **`authFetch` attaches the tenant header.** Add an `ActiveOrgContext` (set by `OrgLayout` from `:orgSlug`); `authFetch` adds `X-AccountGo-Org` when an active org exists and the URL is not under `/api/auth`, `/api/organizations` (list/create), `/api/invitations`. Do not rely on every page remembering to add it.
2. **`OrgLayout.tsx`**: load `GET /api/organizations` (mine) and `GET /api/organizations/current`. Use the real `role`, `permissions`, `plan`, `activeModules`. Remove hardcoded `role: 'Admin'`, `currency: 'PHP'`, `validUntil: '2028-12-31'`, theme derived from plan (keep theme as a workspace setting if needed). On 404 → clear `accountgo:lastOrgSlug` and redirect to `/select-org` with a toast "You don't have access to that workspace".
3. **`SelectOrg.tsx`**: list my workspaces with role badges; "Create workspace" (→ `POST /api/organizations`); "Pending invitations" section (→ `/api/invitations/pending`, accept inline). If exactly one workspace and no pending invites, auto-redirect.
4. **`Members.tsx`**: rewrite on members/invitations endpoints. Table: name, email, role (Owner/Admin/Member), security roles, joined. Actions gated by role: invite (email + role + security roles), change role, remove, resend/revoke invite, transfer ownership (Owner only), leave workspace.
5. **New `/invite/:token` page**: preview invitation. Not logged in → buttons "Sign in" / "Create account" that carry `?invite=<token>` through Login/Signup and auto-accept after OTP verify. Logged in with a different email → explain and offer sign-out.
6. **`Gating.tsx` / `ProtectedRoute`**: use per-workspace `permissions` and `activeModules` from `organizations/current`, not `/api/auth/me` roles.
7. **`OrgSwitcher.tsx`**: list only my workspaces; switching keeps the sub-path (existing behaviour).
8. **Settings → danger zone**: delete workspace (Owner only, type slug to confirm).
9. Types in `src/types/` for `Organization`, `Member`, `Invitation`, `WorkspaceRole`.

**Acceptance (Phase 4):** `pnpm build` passes (tsc strict). Manual script in the PR: two browsers, two users, two workspaces; user 2 cannot see workspace 1 in switcher, cannot open `/org/<ws1>/dashboard` (redirected), receives invite, accepts, then sees it with the invited role and only permitted nav items.

---

## Phase 5 — Rollout to production

Run in this order, with the owner present:

1. `npx wrangler d1 export accountgo-db --remote --output backups/accountgo-db-<date>.sql` (keep out of git).
2. Confirm Phase 0 is live and secrets were rotated.
3. `npx wrangler d1 migrations apply accountgo-db --remote` (Phase 1 DDL + backfill).
4. Verify with SQL: every tenant table has `CompanyId`, no row has `CompanyId` NULL/0, `CompanyMember` backfill matches D1 decision.
5. Bump `CONTAINER_NAME` in `src/Api/wrangler.jsonc` (forces a fresh container with the new image) and `cd src/Api && pnpm deploy`.
6. `cd src/Web && pnpm build && pnpm deploy`.
7. Smoke test on the live site (script in PR): login → only own workspaces; create workspace → empty books with CoA; invite second email → accept → correct role; cross-tenant URL → redirected; existing `ph-financials` data intact for its members.
8. Watch `wrangler tail accountgo-api` for 30 minutes for `CrossTenantWriteException`, `TenantNotResolvedException`, 5xx.

Rollback: redeploy previous worker versions (`wrangler rollback`) — the additive columns/tables are backward compatible with the old code. Restore D1 from the export only if data was corrupted.

---

## Decisions (resolved 2026-10-21)

| # | Question | Decision |
|---|---|---|
| D1 | Who owns `ph-financials` (Company 1)? | `marvinperez@gmail.com` = **Owner**; `hello@marvinperez.com` = **Admin**; `admin@accountgo.ph` = **Member** with the `Auditor / Viewer` security role. Reason: `admin@accountgo.ph` is a seeded, publicly advertised demo account (shown on the login page) and must not hold ownership |
| D2 | Should platform staff (`SystemAdministrators`) see/enter every workspace? | **No implicit access.** Platform staff must be an explicit member. Platform staff list = `marvinperez@gmail.com` only; remove `SystemAdministrators` from `admin@accountgo.ph` and `hello@marvinperez.com` in the backfill. A support-access feature is out of scope |
| D3 | Can Admins invite other Admins, or only Owners? | **Only Owners grant the Admin role** (by invite or promotion). Admins invite/manage Members only. Only Owners transfer ownership, change the subscription, or delete the workspace |
| D4 | One email can belong to many workspaces? | **Yes** |
| D5 | Is `ph-financials` data kept in production? | **Yes, kept as-is.** New workspaces get a clean ledger (CoA + settings only, no sample customers/invoices) |
| D6 | Phase 0 start | **Start immediately** on `feat/mt-phase-0-security-hotfixes`. Add 0.8 below |
| D7 | Invite acceptance for unauthenticated users | **Frontend calls `POST /api/invitations/{token}/accept` right after OTP verification succeeds** (no token in `AuthController`). Details: the `/invite/:token` page stores the token in `sessionStorage` (not `localStorage`, and strip it from the URL via `history.replaceState` after reading); the signup/login form prefills and locks the email from the invitation preview; after accept, route to `/org/<slug>/dashboard` and clear the stored token. If accept fails or the tab closes, `GET /api/invitations/pending` on SelectOrg is the recovery path. The email-match check stays server-side in `accept` |

**0.8 (added to Phase 0):** Remove the "Default Admin: admin@accountgo.ph" hint and the prefilled email from `src/Web/src/pages/Login.tsx` in production builds (`import.meta.env.DEV` only). It advertises a valid account to anyone.

Phase 3 consequence of D3: `PATCH …/members/{id}` to `role: Admin` and `POST …/invitations` with `role: Admin` require Owner (403 for Admin). Add a test for each.

---

## Out of scope

Billing/payment for plans, SSO/SAML, per-workspace custom domains, hard-delete/export of a workspace, cross-workspace consolidated reports, support-impersonation, moving data between workspaces.

---

## Implementor checklist per PR

- [ ] Branch `feat/mt-phase-N-*`, PR description lists files changed, decisions taken, and anything deviating from this plan (with reason).
- [ ] `dotnet build src/src.sln` and `dotnet test` pass; `cd src/Web && pnpm build` passes.
- [ ] New/changed endpoints listed with auth scope and required role.
- [ ] Tests from the phase's Acceptance section exist and pass; paste the test output.
- [ ] No secrets, `.env`, `accountgo.db-wal/shm`, or backups committed.
- [ ] No `CompanyId = 1` literals, no `GetDefaultCompany`, no new `[AllowAnonymous]` without justification.
- [ ] Request review; do not merge or deploy until review says **GO**.
