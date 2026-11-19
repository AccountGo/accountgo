# Deployment to Cloudflare

**AccountGo Cloud** (`*.accountgo.dev`) is deployed from a private ops repository. This public repo runs CI only; hosted deploys are triggered manually (ops SHA deploy or Actions → Notify ops to deploy).

**Self-host:** copy `src/*/wrangler.example.jsonc` → `wrangler.jsonc`, set your domains, then add the `ConnectionStrings:PostgresConnection` secret and:

```bash
cd src/Api && pnpm install && npx wrangler deploy
cd src/Web && pnpm install && pnpm build && pnpm run deploy
```

See [Deployment Guide](../DEPLOYMENT.md).
