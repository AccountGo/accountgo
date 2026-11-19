---
name: accountgo-ui-e2e
description: Run live browser-based UI smoke tests and end-to-end (E2E) walkthroughs against a running AccountGo instance. Use whenever the user asks to "perform ui end to end testing", "run ui smoke test", "test the UI in the browser", "live browser testing", "verify UI flows", or check/update user guides and tutorials against the live web interface.
---

# AccountGo UI End-to-End Testing

Run repeatable, live browser UI smoke tests and end-to-end walkthroughs against AccountGo using an available browser automation tool (browser subagent, Chrome DevTools/MCP, or Playwright).

## 1. Pre-Flight: Verify Local Services

Before opening the browser, confirm the target endpoints are reachable:

```bash
curl -sS -o /dev/null -w "api:%{http_code}\n" http://localhost:8001/health
curl -sS -o /dev/null -w "web:%{http_code}\n" http://localhost:3000
```

- **API (`http://localhost:8001/health`)** must return `200` (`"status":"healthy"`).
- **Web SPA (`http://localhost:3000`)** must return `200` (Vite proxies `/api/*` to `http://localhost:8001`).
- If either service is down and the user hasn't started it, start it locally (see `accountgo-local-dev`) or report which port is refusing connections.

## 2. Connect the Browser Tool

- Use your active browser tool or `browser` subagent to open the target URL.
- If no browser tool is active in the session, inform the user (e.g., recommend `/browser`) before proceeding.
- If Chrome remote debugging fails (`DevToolsActivePort` not found), stop and ask the user to enable remote debugging at `chrome://inspect/#remote-debugging`.

## 3. Authenticate & Enter a Workspace

1. Navigate to `http://localhost:3000` (or the specific route requested by the user).
2. Check authentication state:
   - If redirected to `/login`: in local `Development` without `RESEND_API_KEY`, OTP sign-up/sign-in auto-fills the code; otherwise ask the user for credentials or let them sign in.
   - If at `/select-org` with `0` organizations: ask before creating a disposable test workspace unless the user explicitly requested a full create-and-test run.
3. Keep runs **read-only / non-destructive by default** (no deleting records or posting permanent accounting entries) unless the user explicitly requests write/mutation E2E flows.

## 4. Execute the UI Checklist

### A. Default Smoke Test Scope
Walk through each active area and confirm data loads without stuck spinners or layout breaks:
- **Navigation & Layout**: Main sidebar, responsive collapse, top bar, and module sub-navigation tabs.
- **Dashboard**: KPI cards, charts, and summary widgets.
- **Sales**: Customers, Invoices, Quotations, and Sales sub-tabs (e.g., Customer Credits when enabled).
- **Purchases**: Vendors, Bills, and Purchases sub-tabs (e.g., Supplier Credits when enabled).
- **General Ledger & Financials**: Chart of Accounts, Journal Entries, Banking, and Year-end close entry point.
- **Reports**: Trial Balance, Income Statement, Balance Sheet.
- **Settings**: Company settings, tax/fiscal settings, and Team & Members.

### B. Guide & Tutorial Verification Mode
When testing or updating user guides/tutorials:
- Follow the documented steps verbatim in the live UI.
- Flag any drift in navigation paths, button labels, form fields, permissions, or empty-state copy.
- Capture screenshots or recordings if the user needs updated visuals.

## 5. Inspect Console & Network on Every Route

Track and record across all visited pages:
- **Console errors** and uncaught page exceptions.
- **Failed network requests** (`4xx`/`5xx`), CORS errors, or CSP violations.
- **UI/UX defects**: broken layouts, clipped text, infinite loading states, missing form `id`/`name` attributes, or misleading empty states.

## 6. Report Structure

Always return a concise report using this format:

```markdown
### UI E2E Test Summary

| Area | Route / Endpoint | Status | Notes |
|---|---|---|---|
| Entry & Auth | `/`, `/select-org` | PASS / FAIL / BLOCKED | ... |
| ... | ... | ... | ... |

### Findings
- **Blockers (Errors / 4xx-5xx / Broken Flows):** ...
- **Minor UI / Console Polish:** ...
```
