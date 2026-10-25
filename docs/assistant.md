# AccountGo Assistant

In-app AI sidekick powered by [`@anter/ai-chat-sdk`](https://github.com/anter-ai/ai-chat-sdk) and the live Anter agent `accountgo-assistant`.

## UX

- Header **Sparkles** control opens a resizable chat side panel.
- Desktop (`viewport > 1024`): split pane. Panel width is **30–40% of the viewport**, clamped so the host column keeps at least **600px** (`HOST_MIN_PX`). Opening the assistant collapses the nav sidebar; closing restores the previous mode.
- Tablet/phone (`viewport ≤ 1024`): SDK overlay drawer (host width unchanged).
- Available to all authenticated workspace users. A paid-subscription gate can be added later (Stripe not wired yet).

## Frontend

| Piece | Role |
|-------|------|
| `AssistantProvider` | Open/close + sidebar collapse |
| `AssistantMount` | `ChatProvider` + `ChatSidepanelLayout` |
| `AssistantAdapter` | `ChatAdapter` → `/api/ai/chat/*` |
| `panelSizing.ts` | Viewport % → SDK layout % + host floor |

Context variables on every turn:
- **Server-owned (always overwritten):** `userId`, `sessionId`, `organizationId`, `companyId`, `companySlug` from JWT + resolved `ITenantContext`.
- **Client-allowed:** `pageRoute`, `pageTitle` (and other non-identity hints). Client-supplied tenant/identity keys are stripped.

## Backend

`AiChatController` proxies to `https://agents.anter.ai/api/v1/external/agent-runner/...` with the project API key. Configure in `src/Api/.env`:

```bash
Anter__BaseUrl=https://agents.anter.ai
Anter__ApiKey=...
Anter__ProjectId=abproj_...
Anter__AgentSlug=accountgo-assistant
```

Never put Anter credentials in Vite / Cloudflare Workers SPA env. On the API Worker, set secrets (not `vars`):

```bash
cd src/Api
npx wrangler secret put Anter__ApiKey
npx wrangler secret put Anter__ProjectId
```

`Anter__BaseUrl` and `Anter__AgentSlug` are plain `vars` in `wrangler.jsonc`.

## MCP (Phase 3 readiness)

AccountGo already exposes financial MCP tools at `POST/GET /mcp` (auth required). The Anter agent can be connected to that endpoint from the Anter Agent Builder when a stable public API URL and auth model for agent→MCP calls are ready. Until then the assistant runs in advisory mode using user-supplied context and accounting principles.

Suggested context for tool calls once MCP is linked: `companySlug` / `companyId` from `contextVariables`, plus the caller's JWT or a dedicated MCP assertion — mirror AlphaPal's pass-through pattern if needed.

A draft revision of `accountgo-assistant` documents these context variables. Promote that draft in Anter Agent Builder when ready (promotion is UI-only).
