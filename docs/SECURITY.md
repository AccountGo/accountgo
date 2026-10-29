# Security

## Responsible disclosure

Report vulnerabilities to **security@accountgo.dev**. Please include reproduction steps and impact. Do not open a public GitHub issue for sensitive reports until a fix is coordinated.

## Hosted product (AccountGo Cloud)

- Served at the Cloudflare edge (Workers + Containers + D1).
- Production deploy credentials and resource identifiers are **not** stored in this public repository.
- Authentication uses short-lived JWTs; refresh tokens are HttpOnly cookies with `SameSite=Lax`.
- Tenant isolation is enforced in the application layer for multi-workspace data.

## Self-hosting

You are responsible for:

- Generating and rotating `SECRET_KEY` / `INTERNAL_SYNC_KEY`
- Restricting Cloudflare API tokens
- Backups of your D1 (or other) database
- TLS, WAF, and access control on your account

Use `wrangler.example.jsonc` templates; never copy production IDs from third parties into your config.

## Security updates

Dependabot opens PRs for vulnerable dependencies. Prefer merging security updates promptly.
