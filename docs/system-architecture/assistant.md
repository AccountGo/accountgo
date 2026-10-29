# AccountGo Assistant

In-app AI sidekick powered by [`@anter/ai-chat-sdk`](https://github.com/anter-ai/ai-chat-sdk) and an Anter agent (default slug `accountgo-assistant`). Configure your own Anter project id and API key via environment / Worker secrets — do not commit production IDs.

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
- **Server-owned (always overwritten):** `userId` (workspace-scoped `ag_c{companyId}_u_{userId}`), `accountGoUserId`, `sessionId`, `organizationId`, `companyId`, `companySlug` from JWT + resolved `ITenantContext`; plus entitlement hints `companyPlan`, `activeModules` (comma-separated), `companyRole`, `subscriptionStatus` from `IAdministrationService.GetCurrentSubscription()`.
- **Client-allowed:** `pageRoute`, `pageTitle` (and other non-identity hints). Client-supplied tenant/identity/entitlement keys are stripped.

Financial MCP tools never throw on plan or permission gates: they return `Status` `PlanRestricted` or `AccessDenied` with a clear `Message` (and empty report payloads) so the agent can explain upgrade paths without tripping Anter’s tool-failure circuit breaker. Unresolved tenant and argument validation still throw.

## Agent prompt (Anter `accountgo-assistant`)

Layer 2 lives in Anter Agent Builder (draft → promote in UI). Paste / merge these rules into the root agent instructions:

1. **Gate on `activeModules`, not `companyPlan` alone.** Call financial MCP tools (`get_chart_of_accounts`, `get_trial_balance`, `get_balance_sheet`, `get_income_statement`, `get_general_ledger_transactions`) only when `activeModules` contains `financials` or `reports`. An Accounting workspace may still have those modules turned off.
2. **Use `companyPlan` for upgrade copy only** (e.g. Invoicing → Accounting / Enterprise under Settings > Subscription).
3. **Status-first tool results.** If a financial tool returns `status` of `PlanRestricted` or `AccessDenied`, explain `message` to the user. Do **not** interpret empty `accounts` / `items` / `transactions` as empty books, and do **not** retry sibling financial tools in a loop.
4. **Invoicing without financial modules:** quotes, invoices, payments, and vendor bills only — no GL, trial balance, balance sheet, or P&L via tools.
5. **`companyRole`:** Owners/Admins typically have report access; Members need explicit permissions. On `AccessDenied`, tell the user to ask a workspace admin.

See also [assistant-agent-entitlements.md](./assistant-agent-entitlements.md).

## Backend

`AiChatController` proxies to `https://agents.anter.ai/api/v1/external/agent-runner/...` with the project API key. Configure in `src/Api/.env`:

```bash
ANTER_BASE_URL=https://agents.anter.ai
ANTER_API_KEY=...
ANTER_PROJECT_ID=...
ANTER_AGENT_SLUG=accountgo-assistant
```


Never put Anter credentials in Vite / Cloudflare Workers SPA env. On the API Worker, set `ANTER_API_KEY` as a secret:

```bash
cd src/Api
npx wrangler secret put ANTER_API_KEY
```

`ANTER_BASE_URL` and `ANTER_AGENT_SLUG` can be set in `wrangler.jsonc` vars (or environment). Set `ANTER_PROJECT_ID` via environment / Worker vars for your own Anter project — do not commit production project IDs.

## Zero-Trust Multi-Tenant Isolation & MCP Pass-Through

1. **Workspace-Scoped Sessions (`ag_c{companyId}_u_{userId}`):** Every `/api/ai/chat/*` endpoint requires an active, resolved `ITenantContext` and scopes the upstream Anter `userId` to `(companyId, userId)` without colons. Switching companies in the UI (`<ChatProvider key={orgKey}>`) isolates chat sessions per workspace.
2. **Per-Run Signed Caller Assertion (`X-Mcp-Caller-Assertion`):** On `run-stream` and `executions/{id}/resume`, `AiChatController` mints a short-lived HMAC-SHA256 caller assertion (`v1.<base64url(payload)>.<base64url(hmac)>`) via `IMcpCallerAssertionService` binding `(uid, cid, org, iat, exp)` and forwards it in Anter's `mcpPassThroughHeaders["X-Mcp-Caller-Assertion"]` overlay.
3. **MCP Endpoint Verification (`/mcp`):** `TenantResolutionMiddleware` validates both direct MCP clients (`Authorization: Bearer <jwt>` + `X-AccountGo-Org`) and shared-agent calls from Anter AI (`Authorization: Bearer <ANTER_API_KEY>` + `X-Mcp-Caller-Assertion`), verifying active membership and permissions in the database on every tool execution.
