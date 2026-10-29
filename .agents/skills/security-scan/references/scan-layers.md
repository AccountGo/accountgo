# Security Scan — Layer Reference

Loaded by `/security-scan` during execution. Detection patterns, OWASP Top
10:2025 mappings, and remediation examples per layer.

## Layer 1: Package Vulnerabilities — A03:2025 Software Supply Chain Failures

```bash
dotnet list package --vulnerable --include-transitive
```

Severity mapping:

| Severity | CVSS | Typical impact |
|----------|------|----------------|
| Critical | 9.0-10.0 | Remote code execution, authentication bypass |
| High | 7.0-8.9 | Privilege escalation, data exposure |
| Medium | 4.0-6.9 | Denial of service, information disclosure |
| Low | 0.1-3.9 | Minor information leakage |

Remediation: bump to the patched version. If no patch exists, document the risk
and apply compensating controls.

## Layer 2: Secrets Detection

Scan `.cs`, `.json`, `.yml`, `.yaml`, `.xml`, `.config` files.

```
HIGH-CONFIDENCE PATTERNS (almost always a real secret):
- "Password=" / "Pwd=" in connection strings outside appsettings.Development.json
- "Bearer " followed by a base64 token in source code
- "-----BEGIN PRIVATE KEY-----" / "-----BEGIN RSA PRIVATE KEY-----"
- AWS: "AKIA" + 16 alphanumeric characters
- Azure Storage/Service Bus key patterns

MEDIUM-CONFIDENCE (need context):
- "ApiKey"/"Secret"/"Token" variables with string literal assignments
- Base64 strings > 40 chars in source
- Connection strings with server addresses in non-Development config

FALSE-POSITIVE INDICATORS (skip):
- appsettings.Development.json values (dev-only)
- Placeholders: "your-key-here", "changeme", "TODO", empty strings
- Test fixtures with obviously fake values
- "UserSecretsId" in .csproj (that's the fix, not the problem)
```

```csharp
// BAD — hardcoded connection string
var connectionString = "Server=prod-db;Database=Orders;User=admin;Password=S3cret!";

// GOOD — configuration; secrets live in user-secrets (dev) or Key Vault/env (prod)
var connectionString = builder.Configuration.GetConnectionString("OrdersDb");
```

## Layer 3: OWASP Code Patterns

Mapped to the OWASP Top 10:2025 taxonomy.

```
A05:2025 — Injection (SQL)
  Detect: string concatenation in SQL, raw SQL with user input
  Pattern: FromSqlRaw($"SELECT * FROM Orders WHERE Id = '{userInput}'")
  Fix: FromSqlInterpolated($"... WHERE Id = {userInput}") — parameterized —
       or LINQ / EF.Functions.Like

A05:2025 — Injection (XSS)
  Detect: raw HTML output without encoding in Razor/Blazor
  Pattern: @Html.Raw(userInput)
  Fix: Razor's default encoding (@userInput) or explicit sanitization

A08:2025 — Software or Data Integrity Failures (insecure deserialization)
  Detect: BinaryFormatter, JsonConvert with TypeNameHandling.All
  Fix: System.Text.Json (no type-name handling by default);
       if Newtonsoft required: TypeNameHandling.None + explicit converters

A04:2025 — Cryptographic Failures
  Detect: MD5/SHA1 for security purposes, ECB mode, hardcoded keys
  Fix: SHA256 minimum; HMACSHA256 for authentication; AES-GCM for encryption;
       Rfc2898DeriveBytes for password-derived keys

A01:2025 — Broken Access Control (IDOR)
  Detect: endpoints using user-supplied IDs without ownership verification
  Pattern: GET /orders/{id} returns any order regardless of owner
  Fix: ownership check — where o.Id == id && o.CustomerId == currentUser.Id
```

## Layer 4: Auth Configuration — A07:2025 Authentication Failures / A01:2025 Broken Access Control

```
CHECKLIST:
1. All endpoints have explicit auth attributes
   - find_references("AllowAnonymous") — list deliberately public endpoints
   - find_references("Authorize") — list protected endpoints
   - Gap: endpoints with neither (behavior depends on ambient global config)

2. JWT validation is strict
   - ValidateIssuer / ValidateAudience / ValidateLifetime /
     ValidateIssuerSigningKey — all true
   - ClockSkew: 1 minute max (the 5-minute default is too generous)

3. Policies are specific
   - BAD: bare [Authorize] — only checks "is authenticated"
   - GOOD: [Authorize(Policy = "OrderAdmin")] — role/claim-based

4. No bypass patterns
   - UseAuthentication() before UseAuthorization()
   - No global AllowAnonymous accidentally opening everything
   - API key validation in middleware, not per-controller
```

```csharp
// BAD — weak validation: anyone can issue tokens, expired tokens accepted
options.TokenValidationParameters = new()
{
    ValidateIssuer = false,
    ValidateAudience = false,
    ValidateLifetime = false,
    IssuerSigningKey = new SymmetricSecurityKey("short-key"u8.ToArray()) // < 256 bits
};

// GOOD — validate everything, strict skew, 256-bit+ key from configuration
options.TokenValidationParameters = new()
{
    ValidateIssuer = true,
    ValidIssuer = builder.Configuration["Jwt:Issuer"],
    ValidateAudience = true,
    ValidAudience = builder.Configuration["Jwt:Audience"],
    ValidateLifetime = true,
    ClockSkew = TimeSpan.FromMinutes(1),
    ValidateIssuerSigningKey = true,
    IssuerSigningKey = new SymmetricSecurityKey(
        Convert.FromBase64String(builder.Configuration["Jwt:Key"]!))
};
```

## Layer 5: CORS Configuration — A02:2025 Security Misconfiguration

```csharp
// CRITICAL — wildcard origin with credentials (browsers block the combo,
// but it signals a misunderstanding of CORS)
policy.AllowAnyOrigin().AllowCredentials();

// HIGH — wildcard origin: any website can read API responses.
// Acceptable ONLY for truly public data feeds.
policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();

// GOOD — explicit origins, methods, and headers from configuration
policy.WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()!)
      .AllowCredentials()
      .WithMethods("GET", "POST", "PUT", "DELETE")
      .WithHeaders("Content-Type", "Authorization");
```

Also check: exposed headers leaking internals, overly broad methods, and
dev-vs-prod policy separation.

## Layer 6: Data Protection — A04:2025 Cryptographic Failures / A09:2025 Logging & Alerting Failures

```
CHECKS:
1. PII in logs — email, phone, SSN, card numbers in log statements
   Rule: log identifiers (IDs), not identity data
2. Over-broad responses — returning full entities (password hash included)
   Fix: response DTOs that exclude sensitive fields
3. Sensitive data stored plaintext — API keys, tokens in the database
   Fix: IDataProtector before storage; never roll your own encryption
4. Secrets in appsettings.json
   Fix: user secrets (dev), Key Vault / environment variables (prod)
```

```csharp
// BAD — PII in logs
logger.LogInformation("Order placed by {Email} for {CreditCard}",
    order.CustomerEmail, order.PaymentCard);

// GOOD — identifiers only
logger.LogInformation("Order {OrderId} placed by customer {CustomerId}",
    order.Id, order.CustomerId);
```

## Finding Format and Report Template

Each finding: `#### [SEVERITY] File:Line — Title` with OWASP category,
description, impact, and remediation (code before/after). Never "fix this" —
always the specific change.

```markdown
## Security Scan Report

**Project:** MyApp | **Date:** 2026-03-04 | **Scanner:** Claude (static analysis)

> This is a static analysis scan. It catches known patterns but does not replace
> penetration testing, dynamic analysis, or threat modeling.

### Summary

| Severity | Count |
|----------|-------|
| Critical | 0 |
| High | 2 |
| Medium | 3 |
| Low | 1 |

### Findings

#### [HIGH] src/Orders/Features/SearchOrders.cs:34 — SQL Injection (A05:2025)
Current: FromSqlRaw($"SELECT * FROM Orders WHERE Name LIKE '%{search}%'")
Impact: attacker can read/modify/delete any data in the database.
Fix: db.Orders.Where(o => EF.Functions.Like(o.Name, $"%{search}%"))

#### [HIGH] src/Api/Program.cs:12 — Missing authorization on DELETE endpoint (A01:2025)
...

### Layer Results

| Layer | Status | Findings |
|-------|--------|----------|
| 1. Package Vulnerabilities | PASS | 0 CVEs |
| 2. Secrets Detection | PASS | No hardcoded secrets |
| 3. OWASP Code Patterns | FAIL | 1 SQL injection, 1 insecure deserialization |
| 4. Auth Configuration | WARN | 2 endpoints missing explicit auth |
| 5. CORS Configuration | PASS | Origins properly restricted |
| 6. Data Protection | WARN | PII found in 2 log statements |
```
