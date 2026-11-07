# Security

## Responsible disclosure

Report vulnerabilities to **security@accountgo.dev**. Please include reproduction steps and impact. Do not open a public GitHub issue for sensitive reports until a fix is coordinated.

## Hosted product (AccountGo Cloud)

- Served at the Cloudflare edge (Workers + Containers), with data in PostgreSQL.
- Production deploy credentials and resource identifiers are **not** stored in this public repository.
- Authentication uses short-lived JWTs; refresh tokens are HttpOnly cookies with `SameSite=Lax`.
- Google Sign-In security includes:
  - Strict cryptographically secure nonces via `__Host-ag_gnonce` cookie (`SameSite=Strict`, `HttpOnly`, `Secure`, `Max-Age=600`).
  - Single-use token redemption tracking in the database (`ExternalTokenRedemptions`) preventing replay attacks across instances.
  - `Sec-Fetch-Site` validation blocking cross-site CSRF/post requests (`cross_site`).
  - Strict account linking policies: verified emails only, authoritativeness checks (`hd` domain / Google accounts), and 1:1 Google-to-user account binding (`google_already_linked`).
  - Rate limiting on `/api/auth/google/start` (30/min per IP) to preserve authentication budget.
  - Required COOP posture (`same-origin-allow-popups` or absent; never `same-origin`) to maintain secure cross-origin popup isolation without breaking GIS.
- Tenant isolation is enforced in the application layer for multi-workspace data.

## Self-hosting

You are responsible for:

- Generating and rotating `SECRET_KEY` / `INTERNAL_SYNC_KEY`
- Restricting Cloudflare API tokens
- Backups of your database
- TLS, WAF, and access control on your account

Use `wrangler.example.jsonc` templates; never copy production IDs from third parties into your config.

## Security updates

Dependabot opens PRs for vulnerable dependencies. Prefer merging security updates promptly.
