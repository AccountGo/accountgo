# AccountGo Assistant

In-app AI sidekick powered by [`@anter/ai-chat-sdk`](https://github.com/anter-ai/ai-chat-sdk) and the live Anter agent `accountgo-assistant` (`ragnt_yIhh41xJIIBmPpRc3s9SG` in project `abproj_1hAA2dKHr1L32cGDNgZQy`).

## UX

- Header **Sparkles** control opens a resizable chat side panel.
- Desktop (`viewport > 1024`): split pane. Panel width is **30–40% of the viewport**, clamped so the host column keeps at least **600px** (`HOST_MIN_PX`). Opening the assistant collapses the nav sidebar; closing restores the previous mode.
- Tablet/phone (`viewport ≤ 1024`): SDK overlay drawer (host width unchanged).
- Available to all authenticated workspace users. A paid-subscription gate can be added later (Stripe not wired yet).

## Frontend

| Piece | Role |
|-------|------|
| `AssistantProvider` | Open/close + sidebar collapse + synchronous `authFetch` wiring |
| `AssistantMount` | `ChatProvider` (keyed by workspace `orgKey`) + `ChatSidepanelLayout` |
| `AssistantAdapter` | `ChatAdapter` → `/api/ai/chat/*` |
| `panelSizing.ts` | Viewport % → SDK layout % + host floor |

Context variables on every turn:
- **Server-owned (always overwritten):** `userId` (workspace-scoped `ag_c{companyId}_u_{userId}`), `accountGoUserId`, `sessionId`, `organizationId`, `companyId`, `companySlug` from JWT + resolved `ITenantContext`.
- **Client-allowed:** `pageRoute`, `pageTitle` (and other non-identity hints). Client-supplied tenant/identity keys are stripped.

## Backend

`AiChatController` proxies to `https://agents.anter.ai/api/v1/external/agent-runner/...` with the project API key. Configure in `src/Api/.env`:

```bash
ANTER_BASE_URL=https://agents.anter.ai
ANTER_API_KEY=...
ANTER_PROJECT_ID=abproj_1hAA2dKHr1L32cGDNgZQy
ANTER_AGENT_SLUG=accountgo-assistant
```

Never put Anter credentials in Vite / Cloudflare Workers SPA env. On the API Worker, set `ANTER_API_KEY` as a secret:

```bash
cd src/Api
npx wrangler secret put ANTER_API_KEY
```

`ANTER_BASE_URL`, `ANTER_PROJECT_ID`, and `ANTER_AGENT_SLUG` are configured in `wrangler.jsonc`.

## Zero-Trust Multi-Tenant Isolation & MCP Pass-Through

1. **Workspace-Scoped Sessions (`ag_c{companyId}_u_{userId}`):** Every `/api/ai/chat/*` endpoint requires an active, resolved `ITenantContext` and scopes the upstream Anter `userId` to `(companyId, userId)` without colons. Switching companies in the UI (`<ChatProvider key={orgKey}>`) isolates chat sessions per workspace.
2. **Per-Run Signed Caller Assertion (`X-Mcp-Caller-Assertion`):** On `run-stream` and `executions/{id}/resume`, `AiChatController` mints a short-lived HMAC-SHA256 caller assertion (`v1.<base64url(payload)>.<base64url(hmac)>`) via `IMcpCallerAssertionService` binding `(uid, cid, org, iat, exp)` and forwards it in Anter's `mcpPassThroughHeaders["X-Mcp-Caller-Assertion"]` overlay.
3. **MCP Endpoint Verification (`/mcp`):** `TenantResolutionMiddleware` validates both direct MCP clients (`Authorization: Bearer <jwt>` + `X-AccountGo-Org`) and shared-agent calls from Anter AI (`Authorization: Bearer <ANTER_API_KEY>` + `X-Mcp-Caller-Assertion`), verifying active membership and permissions in the database on every tool execution.
