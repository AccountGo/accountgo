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

### Workspace data tools

Besides the financial reports, `/mcp` exposes read-only workspace data tools (`src/Api/Mcp/Tools`): `get_workspace_context`, `get_dashboard_summary`, clients and invoices (`search_clients`, `get_client`, `search_invoices`, `get_invoice`, `get_receivables_aging`, `get_recurring_schedules`), vendors and bills (`search_vendors`, `get_vendor`, `search_bills`, `get_bill`, `get_payables_aging`), banking (`list_bank_accounts`, `get_bank_register`, `get_bank_reconciliations`, `get_bank_statement_lines`), journals (`search_journal_entries`, `get_journal_entry`), `search_items`, `get_tax_rates` and `get_linked_entries`. Books-review evidence adds `get_fiscal_years`, `get_tax_report`, `get_equity_statement`, `search_receipts`, `get_bank_outstanding_items`, `get_members_and_roles`, `get_record_history` and `get_stock_movements`, all in `ReviewMcpTools.cs`.

They do not query the database themselves. `McpControllerReader` runs the page's own GET action in-process, behind that action's filters (tenant classification, `RequireModule`, `RequirePermission`, `RequireOperation`, statement-import and workspace-role guards), as the member resolved from the caller assertion. A tool therefore returns the same records, statuses and totals as the screen and is refused exactly where the screen would be. Only `[HttpGet]` actions can be read this way. Full bank account, routing, IBAN and SWIFT numbers are stripped from every result. Each result carries `status`, `kind`, `data`, paging fields for lists, and `link` (a `/org/{slug}/…` page path). GL transactions carry `sourceDocumentId`, so the agent can open the invoice, bill or journal behind a ledger line.

`get_general_ledger_transactions` also filters by `search` (description, account code or name), `minAmount` and `maxAmount` (a transaction's total debits); the ledger endpoint takes the same parameters. `get_journal_entry` returns `attribution` (who prepared, who posted and when, and whether the poster was a preparer) when the member holds `audit.logs.view`. It is read from the preparer evidence rows and the journal's change history, so posted details are empty for journals posted before change history was recorded.
`get_receivables_aging` and `get_payables_aging` take an optional `asOfDate`. With it they read `Sales/ReceivablesAsOf` and `Purchasing/PayablesAsOf`, which rebuild each document's balance at the end of that day from ledger dates (document date, receipt or payment date, void posting date), so the total can be compared with the control account in the balance sheet at the same date. `unpostedTotal` is the part owed on documents with no ledger posting.

`get_books_checks` reads `Financials/BooksChecks`, which runs `BooksChecksService` for a period. Every figure is computed on the server so the agent quotes results instead of adding rows. Each check returns `Pass`, `Finding`, `Review` or `NotChecked`; an area is `NotChecked` when the member lacks its view permission (ledger, sales, purchases, banking, record history), so the endpoint never reveals records the member could not open.

#### Review and audit-support reads

These tools also use `McpControllerReader` and `WorkspaceDataMcpResponse`; they do not create audit opinions, change records, or implement automated books checks. Tool-level argument validation returns `InvalidRequest`; schema-binding errors remain MCP errors. Permission and entitlement refusals return empty payloads with `AccessDenied` or `PlanRestricted`.

| Tool | Existing GET action | Arguments and evidence |
|------|---------------------|------------------------|
| `get_fiscal_years` | `AdministrationController.FinancialYears` | Year boundaries and active flag, including the non-persisted current-year fallback. The existing GET exposes no lock/close status; the review programme must not infer closed state from this tool. |
| `get_tax_report` | `TaxReportsController.GetSettings`, `GetPeriods`, `GetReport`, `ListFilings` | Without dates, returns the combined `settings`, `periods` and `filings` payload. With both `from`/`to`, returns that period's report, preserving warnings, ledger check, filing snapshots, changed-since-filing and `documentsTruncated`. `includeAllDocuments=true` explicitly removes the document limit. Recent period count: 1–24. |
| `get_equity_statement` | `FinancialsController.EquityStatement` | Optional `from`/`to`, `basis` (Accrual/Cash), paired `compareFrom`/`compareTo`. Preserves partner columns and balance-sheet comparisons. |
| `search_receipts` | `SalesController.SalesReceipts`, `SalesReceipt` | With `receiptId`, returns that receipt through its detail GET. Otherwise optional text, `clientId`, `fromDate`/`toDate`, `skip`/`take`. Receipt summaries only; no posting/void status or allocation detail. Links open Sales because no standalone receipt route exists. |
| `get_bank_outstanding_items` | `BankingController.GetOutstandingAging` | `bankId`, optional `asOfDate`. Returns **aggregate aging buckets**, counts and stale flags, not individual items. Only currently uncleared entries dated through `asOfDate` are included; it does **not** reconstruct historical clearance status. |
| `get_members_and_roles` | `WorkspaceMembersController.Members` and `SecurityRoles` | Current members, governance roles, assigned functional roles and role permission definitions. Both GETs are available to authenticated workspace members; no additional admin restriction is introduced. Assignments are not historical evidence of period-end access. |
| `get_record_history` | `AuditActivityController.Activity` | Optional `entityType`/`entityId` pair, `includeChildren`, `actorUserId`, timestamp range, `cursor` and `limit`. Preserves `items`, `nextCursor` and `windowDays`. Requires `audit.logs.view`. Single-record history has no plan window. The workspace feed follows `AuditHistoryAccess`: single-record-only, recent 90 days, or full; Community has full history. Empty history does not prove no event occurred. |
| `get_stock_movements` | `StockController.Movements` | Optional `itemId`, `fromDate`/`toDate`, `skip`/`take`. Applies filters after the endpoint computes running quantities. Use an item filter for per-item quantities: the existing all-items endpoint accumulates across items. No physical-count evidence. |

Date-only arguments use `yyyy-MM-dd`; history accepts timestamps with an offset (UTC assumed when absent). Paged lists default to 25 rows and accept 1–100. History uses its native cursor and defaults to 50 rows. Receipt and stock lists are filtered and paged after the existing GET loads its population, so the output limit does not bound database work. All payloads pass through the existing recursive bank-number redaction, including history before/after values.

Fiscal-year expectations for the review programme remain limited to dates and the active flag. Retain the limitations on closed state, historical bank outstanding items, and tax document truncation. After deployment, refresh the catalog, enable the new tools, and smoke-test an authorized caller, a caller without permissions, and a restricted plan before owner promotion.

After deploying new tools, refresh the Anter catalog (`test_mcp_server_connection`) and enable them on the agent's MCP toolset.

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
