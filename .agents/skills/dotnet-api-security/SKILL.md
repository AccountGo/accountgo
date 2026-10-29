---
name: dotnet-api-security
description: "Implementing API auth. Identity, OAuth/OIDC, JWT bearer, passkeys (WebAuthn), CORS, rate limiting."
---

# dotnet-api-security

API-level authentication, authorization, and security patterns for ASP.NET Core. This skill owns API auth implementation: ASP.NET Core Identity configuration, OAuth 2.0/OIDC integration, JWT bearer token handling, passkey (WebAuthn) authentication, CORS policies, Content Security Policy headers, and rate limiting.

**Auth ownership:** This skill owns API-level auth patterns. Blazor-specific auth UI (AuthorizeView, CascadingAuthenticationState, client-side token handling) -- see [skill:dotnet-blazor-auth] when it lands. OWASP security principles (cross-cutting vulnerability mitigations) -- see [skill:dotnet-security-owasp].

**Out of scope:** OWASP Top 10 mitigations and deprecated security patterns -- see [skill:dotnet-security-owasp]. Secrets management and secure configuration -- see [skill:dotnet-secrets-management]. Cryptographic algorithm selection -- see [skill:dotnet-cryptography]. Blazor auth UI components -- see [skill:dotnet-blazor-auth].

Cross-references: [skill:dotnet-security-owasp] for OWASP security principles, [skill:dotnet-secrets-management] for secrets handling, [skill:dotnet-cryptography] for cryptographic best practices.

---

## ASP.NET Core Identity

ASP.NET Core Identity provides user management, password hashing, role-based authorization, and two-factor authentication out of the box. It is the recommended starting point for applications that manage their own user accounts.

```csharp
builder.Services.AddIdentityApiEndpoints<ApplicationUser>(options =>
{
    // Password requirements
    options.Password.RequiredLength = 12;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireDigit = true;

    // Lockout
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;

    // User
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

var app = builder.Build();

app.MapIdentityApi<ApplicationUser>(); // Maps /register, /login, /refresh, /manage endpoints
```

### Identity API Endpoints (.NET 8+)

`MapIdentityApi<TUser>()` provides pre-built token-based authentication endpoints for SPAs and mobile clients without Razor UI:

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/register` | POST | Create a new user account |
| `/login` | POST | Authenticate and receive tokens |
| `/refresh` | POST | Refresh an expired access token |
| `/confirmEmail` | GET | Confirm email address |
| `/manage/info` | GET/POST | Get/update user profile |
| `/manage/2fa` | POST | Configure two-factor authentication |

---

## OAuth 2.0 / OpenID Connect

For applications that delegate authentication to an external identity provider (Entra ID, Auth0, Okta, Keycloak), configure OIDC middleware.

```csharp
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
})
.AddCookie()
.AddOpenIdConnect(options =>
{
    options.Authority = builder.Configuration["Oidc:Authority"];
    options.ClientId = builder.Configuration["Oidc:ClientId"];
    options.ClientSecret = builder.Configuration["Oidc:ClientSecret"];
    options.ResponseType = OpenIdConnectResponseType.Code; // Authorization Code Flow
    options.SaveTokens = true;
    options.GetClaimsFromUserInfoEndpoint = true;

    options.Scope.Add("openid");
    options.Scope.Add("profile");
    options.Scope.Add("email");

    options.MapInboundClaims = false; // Preserve original claim types
    options.TokenValidationParameters.NameClaimType = "name";
    options.TokenValidationParameters.RoleClaimType = "roles";
});
```

**Gotcha:** `MapInboundClaims = false` prevents the Microsoft OIDC handler from remapping standard JWT claims (e.g., `sub` to `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier`). Set this to `false` to preserve the original claim types from the identity provider.

---

## JWT Bearer Token Authentication

For API-only scenarios where the client sends a JWT in the `Authorization` header:

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Jwt:Authority"];
        options.Audience = builder.Configuration["Jwt:Audience"];

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromMinutes(1) // Default is 5 min; tighten for security
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

// Protect endpoints
app.MapGet("/api/profile", (ClaimsPrincipal user) =>
    TypedResults.Ok(new { Name = user.Identity?.Name }))
    .RequireAuthorization();
```

### Policy-Based Authorization

```csharp
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AdminOnly", policy =>
        policy.RequireRole("Admin"))
    .AddPolicy("PremiumUser", policy =>
        policy.RequireClaim("subscription", "premium"))
    .SetFallbackPolicy(new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());
```

---

## Passkeys / WebAuthn (.NET 10)

.NET 10 introduces built-in passkey (WebAuthn/FIDO2) support for passwordless authentication. Passkeys use public-key cryptography and are phishing-resistant.

```csharp
// .NET 10: Add passkey support to Identity
builder.Services.AddIdentityApiEndpoints<ApplicationUser>(options =>
{
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders()
.AddPasskeys(); // Enable WebAuthn passkey authentication

var app = builder.Build();

app.MapIdentityApi<ApplicationUser>();
// Passkey registration and authentication endpoints are added automatically
```

### Passkey Registration Flow

1. Client calls `/passkey/register/options` to get a `PublicKeyCredentialCreationOptions` challenge
2. Client creates a credential using the Web Authentication API (`navigator.credentials.create`)
3. Client sends the attestation response to `/passkey/register`
4. Server validates and stores the credential

### Passkey Authentication Flow

1. Client calls `/passkey/login/options` to get a `PublicKeyCredentialRequestOptions` challenge
2. Client signs the challenge using `navigator.credentials.get`
3. Client sends the assertion response to `/passkey/login`
4. Server validates the assertion and issues a session/token

**Key benefits:** No passwords to phish, no credentials stored server-side (only public keys), built-in resistance to replay attacks.

---

## CORS Policies

Cross-Origin Resource Sharing (CORS) controls which origins can call your API. Always use explicit, named policies -- never use `AllowAnyOrigin()` in production.

```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("Production", policy =>
    {
        policy.WithOrigins(
                "https://app.example.com",
                "https://admin.example.com")
            .WithMethods("GET", "POST", "PUT", "DELETE")
            .WithHeaders("Content-Type", "Authorization")
            .SetPreflightMaxAge(TimeSpan.FromMinutes(10)); // Cache preflight
    });

    options.AddPolicy("Development", policy =>
    {
        policy.WithOrigins("https://localhost:5173") // Vite dev server
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

var app = builder.Build();
app.UseCors(app.Environment.IsDevelopment() ? "Development" : "Production");
```

### Common CORS Pitfalls

- **`AllowAnyOrigin()` + `AllowCredentials()`** is rejected at runtime by ASP.NET Core. But `SetIsOriginAllowed(_ => true)` + `AllowCredentials()` silently allows all origins -- never use this pattern.
- **Preflight caching:** Without `SetPreflightMaxAge`, browsers send an OPTIONS request before every cross-origin request. Set a reasonable cache duration (10-60 minutes) to reduce latency.
- **Wildcard headers with credentials:** `AllowAnyHeader()` combined with `AllowCredentials()` works in ASP.NET Core but may behave unexpectedly in some browsers. Prefer explicit header lists.
- **CORS middleware order:** `UseCors()` must be called after `UseRouting()` and before `UseAuthorization()`.

---

## Content Security Policy (CSP)

Content Security Policy headers prevent XSS, clickjacking, and other injection attacks by controlling which resources the browser can load.

```csharp
app.Use(async (context, next) =>
{
    // API-focused CSP -- restrict all content sources
    context.Response.Headers.Append(
        "Content-Security-Policy",
        "default-src 'none'; frame-ancestors 'none'");

    // Additional security headers
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.Append("Permissions-Policy",
        "camera=(), microphone=(), geolocation=()");

    await next();
});
```

For APIs serving HTML responses (Razor Pages, Blazor Server), use a more permissive CSP with nonces:

```csharp
app.Use(async (context, next) =>
{
    var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
    context.Items["CspNonce"] = nonce;

    context.Response.Headers.Append(
        "Content-Security-Policy",
        $"default-src 'self'; script-src 'self' 'nonce-{nonce}'; style-src 'self' 'nonce-{nonce}'");

    await next();
});
```

---

## Rate Limiting

ASP.NET Core includes built-in rate limiting middleware (`Microsoft.AspNetCore.RateLimiting`, .NET 7+). Four algorithms are available: fixed window, sliding window, token bucket, and concurrency limiter.

### Fixed Window

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("fixed", limiterOptions =>
    {
        limiterOptions.PermitLimit = 100;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueLimit = 0; // Reject immediately when limit reached
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

var app = builder.Build();
app.UseRateLimiter();

app.MapGet("/api/products", GetProducts)
    .RequireRateLimiting("fixed");
```

### Sliding Window

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.AddSlidingWindowLimiter("sliding", limiterOptions =>
    {
        limiterOptions.PermitLimit = 100;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.SegmentsPerWindow = 6; // 10-second segments
        limiterOptions.QueueLimit = 0;
    });
});
```

### Token Bucket

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.AddTokenBucketLimiter("token", limiterOptions =>
    {
        limiterOptions.TokenLimit = 100;
        limiterOptions.ReplenishmentPeriod = TimeSpan.FromSeconds(10);
        limiterOptions.TokensPerPeriod = 10;
        limiterOptions.QueueLimit = 0;
    });
});
```

### Concurrency Limiter

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.AddConcurrencyLimiter("concurrent", limiterOptions =>
    {
        limiterOptions.PermitLimit = 10; // Max 10 concurrent requests
        limiterOptions.QueueLimit = 5;
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
});
```

### Per-User Rate Limiting

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("per-user", httpContext =>
    {
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(userId,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1)
            });
    });
});
```

**Gotcha:** `UseRateLimiter()` must be called after `UseRouting()` and before `UseAuthorization()` and endpoint mapping to apply correctly.

---

## Agent Gotchas

1. **Do not use `AllowAnyOrigin()` in production CORS policies** -- always specify explicit origins. See [skill:dotnet-security-owasp] for CORS security implications.
2. **Do not forget `MapInboundClaims = false`** when using external OIDC providers -- without it, claim types are remapped to long XML namespace URIs, breaking role and name lookups.
3. **Do not hardcode JWT signing keys in source code or `appsettings.json`** -- use user secrets for development and environment variables or managed identity for production. See [skill:dotnet-secrets-management].
4. **Do not set `ClockSkew` to `TimeSpan.Zero`** -- small clock differences between token issuer and validator will cause spurious 401 errors. Use 1-2 minutes.
5. **Do not forget middleware order** -- `UseAuthentication()` must come before `UseAuthorization()`, and `UseCors()` must come before `UseAuthorization()`.
6. **Do not use `AllowAnyMethod()` and `AllowAnyHeader()` together in production** -- explicitly list allowed methods and headers to follow the principle of least privilege.
7. **Do not skip rate limiting on authentication endpoints** -- `/login` and `/register` are common brute-force targets. Apply rate limiting to prevent credential stuffing.
8. **Do not use exception-driven rejection in auth paths** -- use defensive parsing (`TryFromBase64String`, length validation) on attacker-controlled input instead.

---

## Prerequisites

- .NET 8.0+ (LTS baseline for Identity API endpoints, JWT bearer, CORS, rate limiting)
- .NET 10.0 for passkey/WebAuthn support
- `Microsoft.AspNetCore.Authentication.JwtBearer` for JWT bearer authentication
- `Microsoft.AspNetCore.Authentication.OpenIdConnect` for OIDC integration
- `Microsoft.AspNetCore.RateLimiting` (included in shared framework .NET 7+)

---

## References

- [ASP.NET Core Security](https://learn.microsoft.com/en-us/aspnet/core/security/?view=aspnetcore-10.0)
- [ASP.NET Core Identity](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity?view=aspnetcore-10.0)
- [JWT Bearer Authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/jwt-bearer?view=aspnetcore-10.0)
- [OAuth 2.0 / OIDC](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/social/?view=aspnetcore-10.0)
- [CORS in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/cors?view=aspnetcore-10.0)
- [Rate Limiting Middleware](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0)
- [WebAuthn/Passkeys](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/passkeys?view=aspnetcore-10.0)
