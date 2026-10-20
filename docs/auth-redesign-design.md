# AccountGo — Authentication Redesign: Passwordless Email OTP + Google SSO + JWT

**Status:** Design for implementation
**Audience:** Implementing engineer(s)
**Reviewer:** Architecture (sign-off required at the milestone gates in §12)
**Scope:** `src/Api` (backend), `src/AccountGoWeb` (current MVC frontend) and/or a new SPA frontend.

---

## 1. Purpose & Goals

Redesign AccountGo authentication to:

1. **Close the critical hole:** every business/admin API endpoint is currently anonymous. Enforce authentication globally.
2. **Eliminate passwords for end users:** login via **email OTP** (6-digit code).
3. **Add Google SSO** as an alternative sign-in.
4. **Keep the existing data model:** reuse ASP.NET Identity tables (`AspNetUsers`, `AspNetUserTokens`, `AspNetUserLogins`, roles) and the domain `Core.Domain.Security.User`. No destruction of existing tables. New tables may be *added* (refresh-token sessions).
5. **One authorization model:** regardless of login method (OTP or Google), the backend issues **its own JWT**. Business APIs only ever trust the AccountGo JWT — never a Google token directly.

### Non-goals
- Rewriting business controllers' logic.
- Migrating existing users' data.
- Removing the Identity password column from the schema (passwords simply stop being used for login; keep the column for compatibility).

---

## 2. Current State (verified against code)

| Area | Current state | File |
|---|---|---|
| Global auth | **No `[Authorize]` anywhere.** Only a commented-out `//[Authorize]` | `src/Api/Controllers/FinancialsController.cs:13` |
| Pipeline | `UseAuthentication()`/`UseAuthorization()` present but nothing to enforce | `src/Api/Program.cs` (~L82–89) |
| Controllers registration | `AddControllers()` with Newtonsoft only — **no fallback policy / filter** | `src/Api/Program.cs` (~L30–38) |
| JWT config | Full validation on (issuer/audience/lifetime/key); HS256 symmetric | `src/Api/Extensions/ServiceExtensions.cs` `ConfigureJWT` |
| Signing key | **Hardcoded**, read from `SECRET:key` | `src/Api/appsettings.json` → `"SECRET": { "key": "GoodDeedBooks1234GoodDeedBooks1234" }` |
| Prod override | `appsettings.Production.json` does **not** override the key | `src/Api/appsettings.Production.json` |
| CORS | `AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()` | `ServiceExtensions.cs` `ConfigureCors` |
| Login body | `[FromBody] dynamic` — no model validation; lockout commented out; `Console.WriteLine(ex.StackTrace)` on failure | `src/Api/Controllers/AccountController.cs` |
| Token issuance | `CreateToken` issues JWT + 32-byte refresh token; stores `RefreshToken` + `RefreshTokenExpiryTime` (7 days) on the user row | `src/Api/Service/AuthenticationService.cs` |
| JWT claims | **Only `Name` + `Email`.** No roles/permissions | `AuthenticationService.GetClaims()` |
| Refresh | `POST api/token/refresh` exists, public; **does not rotate** the refresh token | `src/Api/Controllers/TokenController.cs`, `AuthenticationService.RefreshToken` |
| Identity | `ApplicationUser : IdentityUser` already has `RefreshToken` + `RefreshTokenExpiryTime`; default Identity schema incl. `AspNetUserTokens`; `AddDefaultTokenProviders()` registered | `src/Api/Data/ApplicationUser.cs`, `ApplicationIdentityDbContext.cs`, `ServiceExtensions.ConfigureIdentity` |
| Email/SMTP | **None.** Only a no-op sender in the unrelated `BlazorGDB` sample | — |
| Frontend token | Web app **discards** the API JWT and runs its own cookie auth (`AuthCookie`); API calls send a plaintext `UserName` header, never a Bearer token | `src/AccountGoWeb/Controllers/BaseController.cs`, `AccountController.cs` |

> ⚠️ The current frontend auth is cookie-based and decoupled from the API JWT. That is why the open endpoints went unnoticed: the JWT path was never exercised end-to-end.

---

## 3. Target Architecture

```
┌───────────────┐   OTP or Google        ┌────────────────────┐
│   Frontend    │ ─────────────────────► │  AccountGo Auth     │
│ (SPA or MVC)  │                         │  (AuthController)   │
│               │ ◄───── AccountGo JWT ── │  issues JWT +       │
│  - access tok │        + refresh        │  refresh token      │
│    in memory  │                         └─────────┬──────────┘
│  - refresh in │                                   │ mint JWT w/ roles+perms
│    httpOnly   │                                   ▼
│    cookie     │   Bearer <JWT>          ┌────────────────────┐
│               │ ─────────────────────► │  Business APIs      │
└───────────────┘                         │  [Authorize] global │
                                          │  trust ONLY our JWT │
                                          └────────────────────┘
```

**Key principle:** OTP and Google are *identity establishment* mechanisms. Both converge on the same step: "resolve to an `ApplicationUser` → mint AccountGo JWT (with roles/permissions) + refresh token." Business APIs are identical regardless of login method.

---

## 4. Backend — Security Hardening (do first, before OTP)

### 4.1 Move the signing key out of config
- Remove `SECRET:key` from `appsettings.json` (the value is compromised — it is in git history; treat it as burned and generate a new one).
- Source the key from:
  - **Dev:** .NET user-secrets (`dotnet user-secrets set "SECRET:key" "<new-256-bit-key>"`).
  - **Prod:** environment variable / Key Vault / parameter store, bound via configuration.
- Minimum key length for HS256: 32 bytes (256-bit). Generate cryptographically (e.g. `openssl rand -base64 48`).
- Add a startup guard: if `SECRET:key` is null/short, **throw on startup** (fail closed) rather than running with a default.

### 4.2 Global fallback authorization policy
Replace the "open by default" behavior. In `Program.cs`, after `ConfigureJWT`:

```csharp
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
```

- **Why `FallbackPolicy` over `AuthorizeFilter`:** the fallback applies only when no other policy is present, composes with `[Authorize(Roles=...)]`, and still honors `[AllowAnonymous]`. `AuthorizeFilter` is the legacy approach.
- Endpoints that **must** stay anonymous (decorate explicitly with `[AllowAnonymous]`):
  - `AuthController.SendOtp`
  - `AuthController.VerifyOtp`
  - `AuthController.GoogleSignIn`
  - `TokenController.RefreshToken`
  - Swagger UI (dev only) and health/ping endpoints if any.
- Everything else (all 14 business/admin controllers) becomes protected automatically — **no per-controller edits required**.

### 4.3 CORS lockdown
Replace `AllowAnyOrigin()` with explicit origins:

```csharp
options.AddPolicy("Frontend", policy =>
    policy.WithOrigins(configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()!)
          .AllowAnyHeader()
          .AllowAnyMethod()
          .AllowCredentials());   // required if refresh token uses httpOnly cookie
```
- `AllowAnyOrigin()` **cannot** be combined with `AllowCredentials()`. The redesign needs credentials (refresh cookie), so origins must be explicit.
- Put allowed origins in config per environment.

### 4.4 HTTPS
- Re-enable `app.UseHttpsRedirection()` (currently commented out) for non-dev, or document that TLS is terminated at the proxy.

### 4.5 Clean up `AccountController` / error handling
- Replace `dynamic` bodies with the typed DTOs in §6.
- Stop writing stack traces to `Console`. Return RFC 7807 `ProblemDetails`; never leak `ex.Message`/`InnerException.Message` to clients.
- Re-enable Identity lockout on password paths if password login is retained during transition.

---

## 5. Backend — JWT Claims must carry roles & permissions

Currently `GetClaims()` emits only `Name` + `Email`, so `[Authorize(Roles=...)]` can never work. Extend token minting:

- Add `ClaimTypes.NameIdentifier` (`sub` = user Id), `jti` (unique token id), and one `ClaimTypes.Role` per role.
- Resolve roles/permissions from `Core.Domain.Security.User` (domain) mapped to the Identity user by email.
- Keep the JWT small: put **roles** as claims; if permissions are numerous, expose them via a `/api/auth/me` endpoint rather than bloating the token.

Acceptance: a token minted for an admin user contains role claims, and `[Authorize(Roles="Administrator")]` on a test endpoint passes for admins and 403s for non-admins.

---

## 6. Backend — DTOs (replace `dynamic`)

Location: `src/Dto/Security/` (project `Dto`).

```csharp
namespace Dto.Security
{
    public record SendOtpRequest(
        [Required, EmailAddress] string Email);

    public record VerifyOtpRequest(
        [Required, EmailAddress] string Email,
        [Required, RegularExpression(@"^\d{6}$")] string Otp);

    public record GoogleSignInRequest(
        [Required] string IdToken);

    public record AuthResponse(
        string AccessToken,
        int ExpiresIn,          // seconds
        UserDto User);
        // NOTE: refresh token NOT in body — see §9 (httpOnly cookie)

    public record UserDto(
        string Id,
        string Email,
        string FirstName,
        string LastName,
        IEnumerable<string> Roles);
}
```

- Keep the existing `Token` record for the internal access+refresh pair; `AuthResponse` is the public shape.
- Apply `[ApiController]` on new controllers so model validation auto-returns 400 on invalid DTOs.

---

## 7. Backend — Email OTP

### 7.1 Email delivery service
No email infra exists; build one.

- Define `IEmailSender` (API-local) with `Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct)`.
- Provide an SMTP implementation (`MailKit` recommended over `System.Net.Mail` for modern TLS) **or** a transactional provider (SendGrid/SES) behind the same interface.
- Config section `Email` (host, port, from-address, credentials via secrets — never in `appsettings.json`).
- Provide a `NoOpEmailSender`/console sender for dev so local flows work without SMTP, selected by config/env.

### 7.2 OTP generation & storage
Reuse Identity's token store — **do not invent a parallel table**. Two acceptable implementations; pick one and document it:

**Option A (preferred — true 6-digit numeric, explicit control):**
- Generate a 6-digit code using `RandomNumberGenerator` (not `System.Random`), uniformly distributed.
- Store **`HMACSHA256(code)`** (keyed hash, key from secrets) plus:
  - expiry timestamp (5 minutes),
  - failed-attempt counter,
  - send cooldown timestamp.
- Persist via `UserManager.SetAuthenticationTokenAsync(user, "EmailOtp", "Default", <json payload>)` into `AspNetUserTokens`, or a small dedicated `OtpCodes` table if you prefer queryable attempt counters. Either is fine; the hash + expiry + attempt-count + constant-time compare are the hard requirements.
- Verify with a **constant-time comparison** (`CryptographicOperations.FixedTimeEquals`).

**Option B (Identity token provider):**
- Use a `DataProtectorTokenProvider`/TOTP provider via `GenerateUserTokenAsync`/`VerifyUserTokenAsync` with a short `TokenLifespan`. Simpler and tamper-proof, but codes are longer/alphanumeric unless you configure a numeric TOTP provider.

### 7.3 Rules (both options)
- **Expiry:** 5 minutes.
- **Single-use:** delete/invalidate the stored code immediately on successful verify.
- **Attempt limit:** invalidate the code after **5 failed attempts** (lock the *code*, force re-request — distinct from account lockout).
- **Resend cooldown:** 30 seconds between sends to the same email.
- **Rate limit:** max **3 send-otp requests per email per 15 minutes**, enforced via .NET `RateLimiter` middleware (see §8), plus a per-IP limit.

### 7.4 User-enumeration defense (hard requirement)
`send-otp` must behave identically whether or not the email exists:
- Always return the **same** 200 response: `{ "message": "If an account exists, a code has been sent.", "expiresInSeconds": 300 }`.
- When the user does not exist, perform **equivalent work** (dummy hash computation, no email) so timing does not leak existence.
- Apply the rate limit regardless of existence.

### 7.5 Endpoints — `AuthController` (`[Route("api/auth")]`, `[AllowAnonymous]` on these)
- `POST /api/auth/send-otp` → body `SendOtpRequest` → always 200 generic.
- `POST /api/auth/verify-otp` → body `VerifyOtpRequest` →
  - success: mint JWT (with roles/perms) + refresh token → return `AuthResponse` (+ set refresh cookie, §9),
  - failure: 400 `{ "error": "Invalid or expired code" }` (generic; do not distinguish "wrong" vs "expired").
- `GET /api/auth/me` → `[Authorize]` → returns `UserDto` + permissions for the current token.

---

## 8. Backend — Rate limiting

Use the built-in `Microsoft.AspNetCore.RateLimiting`:
- Policy `"otp-send"`: fixed/sliding window, 3 per 15 min keyed by email (normalized) **and** a per-IP partition.
- Policy `"otp-verify"`: e.g. 10 per 15 min per email+IP.
- Apply with `.RequireRateLimiting("otp-send")` on the endpoints.
- Return `429` with `Retry-After`.

---

## 9. Backend — Refresh tokens (rotation + revocation)

Current behavior stores a single refresh token on the user row and reissues without rotation. Harden:

### 9.1 Storage
- Add a `RefreshTokens` table (session-per-row), **additive** — does not touch existing tables:
  - `Id`, `UserId` (FK `AspNetUsers`), `TokenHash` (store hash, not raw), `ExpiresAt`, `CreatedAt`, `CreatedByIp`, `RevokedAt`, `ReplacedByTokenHash`, `UserAgent`.
- Keep the legacy `ApplicationUser.RefreshToken`/`RefreshTokenExpiryTime` columns for now (unused by the new flow) to avoid a destructive migration; mark deprecated.

### 9.2 Behavior
- **Rotation:** every `refresh` call issues a new refresh token and marks the old one `RevokedAt` + `ReplacedByTokenHash`.
- **Reuse detection:** if a refresh token that is already revoked is presented → treat as compromise → revoke the entire chain for that user and force re-auth.
- **Expiry:** refresh token 7 days (configurable); access token shorten to **10–15 min** (currently 60).
- **Logout:** `POST /api/auth/logout` (`[Authorize]`) revokes the current session's refresh token.

### 9.3 Delivery
- Return the **access token in the JSON body** (`AuthResponse.AccessToken`).
- Deliver the **refresh token as an httpOnly, Secure, SameSite=Strict (or Lax) cookie** scoped to the auth/refresh path. This mitigates XSS token theft. `TokenController.RefreshToken` reads the cookie instead of the body.
- Tighten `ClockSkew` in `TokenValidationParameters` to 1–2 min (default is 5).

---

## 10. Backend — Google SSO

- Frontend performs Google OAuth (Authorization Code + PKCE) and obtains a Google **ID token**.
- `POST /api/auth/google` (`[AllowAnonymous]`), body `GoogleSignInRequest { IdToken }`:
  1. Validate the ID token with `Google.Apis.Auth.GoogleJsonWebSignature.ValidateAsync(idToken, new ValidationSettings { Audience = [<configured client id>] })`. This checks signature against Google's JWKS, issuer, audience, expiry.
  2. Require `payload.EmailVerified == true`.
  3. `FindByEmailAsync(payload.Email)`:
     - **Found:** link external login via `UserManager.AddLoginAsync(user, new UserLoginInfo("Google", payload.Subject, "Google"))` if not already linked (uses standard `AspNetUserLogins`).
     - **Not found:** apply provisioning policy (see below).
  4. Mint the **same** `AuthResponse` (AccountGo JWT + refresh cookie). Google tokens never reach business APIs.
- **Provisioning policy (decide and document):** for a finance system, default to **invite-only / domain-restricted** (e.g. allow only configured hosted domains `payload.HostedDomain`). Auto-provisioning of arbitrary Google accounts should be **off** by default.
- Config section `Google` (ClientId, allowed hosted domains) — ClientId is not secret but keep in config; no client secret needed for ID-token validation.

---

## 11. Frontend

Two tracks — the doc supports either; confirm which is in scope before building the UI.

### 11.1 Track A — fix the existing MVC app (minimum to be correct)
- Store the API JWT (today it is discarded). After `verify-otp`/password sign-in, persist the access token server-side in the MVC session (protected) and keep the refresh cookie.
- In `src/AccountGoWeb/Controllers/BaseController.cs`, replace the per-request `new HttpClient()` + plaintext `UserName` header with:
  - a typed/`IHttpClientFactory` client,
  - `Authorization: Bearer <accessToken>` on every call,
  - remove the `UserName` header mechanism (it is not authentication).
- On `401`, call refresh, update the stored token, replay once; on refresh failure, sign out.

### 11.2 Track B — new SPA (passwordless UX)
Two-step flow:

**Step 1 — request code:** email field → `POST /api/auth/send-otp` → advance to step 2 on 200 (always, since response is generic).

**Step 2 — verify:** six single-digit inputs with:
1. **Auto-advance** focus on digit entry.
2. **Paste detection:** pasting a 6-digit code fills all boxes and auto-submits.
3. **Resend countdown:** 30s cooldown; "Resend" disabled until elapsed.
4. **Expiry countdown:** show remaining time (5:00).
5. **Google button:** "Continue with Google" → OAuth PKCE → `POST /api/auth/google`.

**Token handling (hard requirements):**
- Access token held **in memory only** (never `localStorage`).
- Refresh token in the **httpOnly cookie** set by the backend.
- **JWT interceptor:** inject `Authorization: Bearer` on all API calls.
- **Silent refresh on 401:** single-flight guard so concurrent 401s trigger exactly **one** refresh; replay queued requests; hard logout if refresh fails.

---

## 12. Implementation Sequence & Review Gates

Ordered so the system is never left in a *more* exposed state. **Stop at each gate for review.**

1. **Hardening (Gate 1):** key out of config + startup guard; global `FallbackPolicy`; `[AllowAnonymous]` on auth/refresh/swagger; CORS explicit origins; HTTPS. Temporarily mark existing `SignIn` `[AllowAnonymous]` so nothing breaks.
   *Verify:* unauthenticated request to any business endpoint → **401**; authenticated (existing password login) → 200.
2. **Claims (Gate 2):** add roles/permissions + `sub`/`jti` to JWT; add a test endpoint with `[Authorize(Roles="Administrator")]`.
   *Verify:* role-gated endpoint 200 for admin, 403 for non-admin.
3. **Frontend Bearer (Gate 3):** make the frontend actually send the JWT (Track A) end-to-end against existing login.
   *Verify:* full app works through the JWT path; no `UserName`-header auth remains.
4. **DTOs + error handling (Gate 4):** remove `dynamic`; `ProblemDetails`; no console stack traces.
5. **Email + OTP (Gate 5):** `IEmailSender`, OTP gen/store/verify, rate limiting, enumeration defense.
   *Verify:* OTP login issues a working JWT; expired/wrong code → generic 400; rate limit → 429; non-existent email → identical 200 + timing.
6. **Refresh rotation (Gate 6):** `RefreshTokens` table, rotation, reuse detection, httpOnly cookie, logout.
   *Verify:* refresh rotates; replaying an old refresh token revokes the chain.
7. **Google SSO (Gate 7):** `/api/auth/google`, ID-token validation, provisioning policy.
   *Verify:* valid Google ID token → AccountGo JWT; unverified email / disallowed domain → rejected.
8. **Passwordless UI (Gate 8):** new SPA flow (if Track B), then retire password login.

---

## 13. Acceptance Criteria (checklist)

- [ ] No business/admin endpoint is reachable without a valid AccountGo JWT (401 otherwise).
- [ ] `[AllowAnonymous]` is present **only** on: send-otp, verify-otp, google, token/refresh, (dev) swagger.
- [ ] JWT signing key is not present in any committed file; startup fails if missing/short.
- [ ] CORS lists explicit origins; no `AllowAnyOrigin`.
- [ ] JWT contains `sub`, `jti`, email, and role claims; role-based `[Authorize]` enforced.
- [ ] OTP: 6 digits, 5-min expiry, single-use, ≤5 attempts, 30s resend cooldown, 3/15min send rate limit, stored hashed, constant-time compare.
- [ ] send-otp is enumeration-safe (identical response + comparable timing for unknown emails).
- [ ] Refresh tokens are hashed at rest, rotated on use, reuse triggers chain revocation, delivered via httpOnly Secure cookie.
- [ ] Access token lifetime ≤15 min; `ClockSkew` ≤2 min.
- [ ] Google ID tokens validated against JWKS with audience + `email_verified`; provisioning policy enforced; Google tokens never accepted by business APIs.
- [ ] No `dynamic` request bodies; validation returns 400; errors are `ProblemDetails` with no stack traces/secret leakage.
- [ ] Frontend sends `Authorization: Bearer`; access token in memory; single-flight silent refresh; hard logout on refresh failure.

---

## 14. Risks & Notes for the implementer

- The current signing key is compromised (in git history) — rotating it **invalidates all existing tokens**; coordinate a cutover.
- Removing the frontend's `UserName` header will break any API code relying on `BaseController.GetUserNameFromRequestHeader()` — audit usages and switch them to the authenticated identity (`User.FindFirst(ClaimTypes.NameIdentifier)`).
- Keep the legacy `ApplicationUser.RefreshToken` columns during transition to avoid a destructive migration; remove in a later cleanup.
- Decide Track A vs Track B for the frontend **before** Gate 3; Track B (SPA) changes the token-storage story (cookie + in-memory) and CORS credentials requirements.
- Dev environment must have a working email path (console/no-op sender) so OTP can be tested without SMTP.

---

## 15. Deployment Topology & Cookie Architecture Guidance

### 15.1 Dual-Topology Model
The authentication system supports two distinct deployment topologies simultaneously:

1. **Direct SPA Topology (Browser to API directly):**
   - **Origin:** SPA frontend hosted on `app.example.com`, API hosted on `api.example.com`.
   - **Mechanism:** The browser makes cross-origin `fetch` calls with `credentials: "include"`.
   - **Refresh Cookie:** The API directly sets `accountgo_refresh_token` with `Path=/api`, `HttpOnly=true`, `SameSite=None` (when HTTPS), and `Secure=true`.
   - **Access Token:** Maintained strictly in-memory by JavaScript; rotated every 15 minutes by calling `/api/auth/refresh`.

2. **MVC BFF Topology (Backend-for-Frontend):**
   - **Origin:** AccountGoWeb hosted on `app.example.com`, API on `api.example.com` (or internal network).
   - **Mechanism:** The browser interacts with the MVC web server. The MVC server communicates with the API server-to-server.
   - **Refresh Cookie:** The MVC server receives the API's `Set-Cookie` header and sets its own `accountgo_refresh_token` cookie scoped to the frontend origin (`Path=/`, `HttpOnly=true`, `SameSite=Lax`, `Secure=true` when HTTPS).
   - **Refresh Rotation:** On API 401s, MVC's `BaseController` forwards the cookie to `/api/auth/refresh`, rotates it with a single-flight TTL-cached guard, and updates the `.AspNetCore.Cookies` authentication session.

### 15.2 Reverse Proxy & Trusted Networks
- In production, reverse proxies (Cloudflare, Nginx, AWS ALB, Traefik) offload TLS and forward the client IP via `X-Forwarded-For`.
- The API's `ForwardedHeadersOptions` enforces `ForwardLimit = 2` and retains default loopback proxy trust (`127.0.0.1`, `::1`).
- Custom ingress proxies and Kubernetes/VPC subnet CIDRs must be explicitly configured in `ReverseProxy:KnownNetworks` or `ReverseProxy:KnownProxies` in `appsettings.Production.json` to prevent header spoofing from untrusted public callers.
```
