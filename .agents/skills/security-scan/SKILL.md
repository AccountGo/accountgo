---
name: security-scan
description: >
  Deep security scanning for .NET applications across 6 layers: vulnerable packages,
  secrets detection, OWASP code patterns, auth configuration, CORS policy, and
  data protection. Produces severity-rated findings with specific remediation steps.
  Load this skill when: "security scan", "security audit", "check for vulnerabilities",
  "find secrets", "OWASP", "auth review", "CORS check", "security review",
  "penetration test prep", "CVE check", "vulnerability scan", "hardcoded password",
  "data protection", "security posture".
---

# /security-scan — 6-Layer Security Pipeline

## What

Runs a defense-in-depth static scan across 6 layers. A project with zero CVEs
can still have hardcoded secrets, SQL injection, and missing auth — each layer
catches a different vulnerability class. Findings map to the **OWASP Top
10:2025** taxonomy and are rated Critical/High/Medium/Low by exploitability,
impact, and exposure — a Critical SQL injection on a public endpoint outranks a
Low info-disclosure on an admin page.

Detection patterns, OWASP mappings, remediation code, and the report template
live in `references/scan-layers.md` — read it before executing.

**Honesty rule:** this is static analysis, not a penetration test. It catches
known patterns but misses business-logic flaws, complex authorization bypasses,
and runtime-only vulnerabilities. Every report states this.

## When

- Pre-release security gate — full scan, non-negotiable before production
- "Security scan", "security audit", "find secrets", "CVE check", "OWASP"
- After a dependency update (Layer 1), auth changes (Layer 4), config changes
  (Layer 2), or logging changes (Layer 6)
- Pre-pentest preparation — fix static issues before paying for a pentest
- Incident response and quarterly reviews

## How

### Step 1: Choose Layers

| Scenario | Layers |
|----------|--------|
| Pre-release gate / pentest prep / incident / quarterly | All 6 |
| After dependency update | 1 |
| New endpoint added | 3, 4, 5 |
| Auth system changes | 4 |
| Config file changes | 2 |
| Logging changes | 6 |
| Public API exposure | 3, 4, 5 |
| Internal-only service | 1, 2, 3 |

### Step 2: Execute the Layers

Read `references/scan-layers.md` for the detection patterns per layer.
Delegate deep auth and secrets review to the `security-auditor` agent, pairing
the `authentication` and `configuration` skills.

| # | Layer | OWASP 2025 | Method |
|---|-------|-----------|--------|
| 1 | Package vulnerabilities | A03 Supply Chain | `dotnet list package --vulnerable --include-transitive` |
| 2 | Secrets detection | — | Pattern scan over .cs/.json/.yml/.xml/.config |
| 3 | OWASP code patterns | A05 Injection, A08 Integrity, A04 Crypto, A01 Access Control | Source scan: raw SQL, `Html.Raw`, `BinaryFormatter`, MD5/SHA1, IDOR |
| 4 | Auth configuration | A07 Authentication, A01 Access Control | `get_endpoint_map` — every route's auth posture in one call; flag `unmarked` endpoints; then JWT validation settings |
| 5 | CORS policy | A02 Misconfiguration | Wildcard origins, credentials combos, method/header breadth |
| 6 | Data protection | A04 Crypto, A09 Logging & Alerting | PII in logs, over-broad responses, plaintext sensitive storage |

### Step 3: Rate with Context

Severity must match actual risk — over-classification causes alert fatigue and
buries the real Critical:

- Test-fixture "secrets" and appsettings.Development.json values are expected —
  skip or mark INFO, don't flag as HIGH
- A missing XML comment is never a security finding
- Reserve Critical for exploitable-now issues: injection on public endpoints,
  exposed production secrets, auth bypass

### Step 4: Report

Every finding: `[SEVERITY] file:line — title`, OWASP category, what's wrong,
impact if exploited, and remediation code (before/after). Produce the summary
table + per-layer status table from the reference template, prefixed with the
static-analysis disclaimer.

## Example

```
User: /security-scan before we ship

Claude: Running all 6 layers...

| Layer | Status | Findings |
|-------|--------|----------|
| 1. Packages | PASS | 0 CVEs (142 packages incl. transitive) |
| 2. Secrets | PASS | 0 real secrets (2 dev-only values skipped) |
| 3. OWASP Patterns | FAIL | 1 SQL injection |
| 4. Auth Config | WARN | 2 endpoints missing explicit auth attributes |
| 5. CORS | PASS | Explicit origins from configuration |
| 6. Data Protection | WARN | Customer email logged at Information level |

[HIGH] SearchOrders.cs:34 — SQL Injection (A05:2025)
  FromSqlRaw($"...LIKE '%{search}%'") → attacker controls the query.
  Fix: db.Orders.Where(o => EF.Functions.Like(o.Name, $"%{search}%"))

[MEDIUM] OrderEndpoints.cs:18,31 — no [Authorize]/[AllowAnonymous]; behavior
  depends on ambient global policy. Make intent explicit per endpoint.

[LOW] OrderPlacedHandler.cs:22 — PII in logs (A09:2025). Log CustomerId, not email.

Note: static analysis only — this does not replace a penetration test.
```

## Related

- `references/scan-layers.md` — detection patterns, OWASP 2025 mappings, report template
- `/verify` — Phase 5 runs a lightweight version of this scan per change set
- `/health-check` — Dimension 7 (Security Posture) is the spot-check version
- `authentication` / `configuration` — remediation patterns for Layers 4 and 2
