# Code evidence and maintenance

Reviewed 2026-11-04 against local checkout revision `097edb28` at `/Users/mp/Dev-Workspaces/github/accountgo`. The working tree was clean before skill edits. This checkout contains newer setup behavior than the task worktree at `dfb4cb6b`; the user-requested checkout is the source of truth. This records repository evidence, not confirmation of production rollout.

Source paths below are relative to the repository root. Re-read the implementation before changing claims when the code advances. Product/planning documents can explain intent but do not override implemented routes, gates or service behavior.

| Claim / domain | Primary implementation |
| --- | --- |
| Routes and special integration settings access | `src/Web/src/App.tsx` |
| Sidebar labels, always-present Dashboard, administration visibility | `src/Web/src/components/Sidebar.tsx` |
| Effective permission array without Owner/Admin bypass | `src/Web/src/layouts/OrgLayout.tsx` (`evaluateHasPermission`) |
| Entitlements, read-only modules, statement import, limits | `src/Web/src/lib/entitlements.ts` |
| Setup summary and historical-intent wording caveat | `src/Web/src/components/WorkspaceSetupCard.tsx`, `src/Web/src/lib/workspaceSetupLabels.ts` |
| Journal Approval section/buttons | `src/Web/src/pages/Settings.tsx`, `src/Web/src/components/JournalApprovalPolicyCard.tsx` |
| Operating API fields and policy mutation authorization | `src/Api/Controllers/OrganizationsController.cs` |
| State, eligibility, return-to-solo, policy capacity/revision guards | `src/Api/Service/WorkspaceOperatingPolicyService.cs`, `WorkspaceApprovalPolicyResolver.cs`, `WorkspaceReviewerEligibility.cs` in the same directory |
| Initialization, enrollment and Personal release restrictions | `src/Api/Service/WorkspaceInitializationApplicator.cs`, `src/Api/Options/WorkspaceExperienceOptions.cs` |
| Membership/invite transitions and governance | `src/Web/src/pages/Members.tsx`, `src/Api/Controllers/WorkspaceMembersController.cs`, `WorkspaceInvitationsController.cs`, `InvitationsController.cs`; `src/Api/Service/WorkspaceInvitationTransition.cs` |
| Owner access visibility versus strict self-grant eligibility | `src/Web/src/pages/Members.tsx`, `src/Api/Controllers/WorkspaceMembersController.cs`, `src/Api/Service/ContractAVerifier.cs` |
| Purpose conversion limits and accounting-setting locks | `src/Api/Controllers/OrganizationsController.cs`, `src/Web/src/pages/Settings.tsx` |
| New wizard, continuation step list and submission | `src/Web/src/pages/CreateOrg/CreateOrgPage.tsx`, `lib/policyDefaults.ts`, `hooks/useCreateOrgWizard.ts`, `steps/*.tsx` in that directory; `src/Web/src/pages/loginContinuation.ts` |
| Checklist inclusion/done/dismiss behavior | `src/Web/src/lib/onboardingChecklist.ts`, `src/Web/src/components/OnboardingChecklist.tsx` |
| Dashboard product surface and recorded-cash measure | `src/Web/src/pages/Dashboard.tsx`, `src/Web/src/lib/dashboardProduct.ts` |
| Clients/vendors and contact dialogs | `src/Web/src/pages/Parties.tsx`, `PartyDetail.tsx`; `src/Web/src/components/PartyFormDialog.tsx` |
| Invoice draft/issue/payment/action gates | `src/Web/src/pages/InvoiceEdit.tsx`, `InvoiceDetail.tsx`; `src/Web/src/components/SendInvoiceDialog.tsx` |
| Recurring invoices | `src/Web/src/pages/RecurringSchedules.tsx`, `RecurringScheduleEdit.tsx`, `RecurringScheduleDetail.tsx` |
| Bills and payment source | `src/Web/src/pages/BillEdit.tsx`, `BillDetail.tsx`; `src/Web/src/components/PayBillDialog.tsx` |
| Items and stock | `src/Web/src/pages/ProductsServices.tsx`, `Inventory.tsx` |
| Saved journal review IDs and posting | `src/Web/src/pages/JournalEntries.tsx`; `src/Api/Controllers/FinancialsController.cs` |
| Posted ledger / COA / reports controls | `src/Web/src/pages/GeneralLedger.tsx`, `ChartOfAccounts.tsx`, `Reports.tsx` |
| Banking, import, review and reconciliation | `src/Web/src/pages/Banking.tsx`, `BankRegister.tsx`, `BankStatementImport.tsx`, `IncomingReview.tsx`, `Reconcile.tsx`; `src/Web/src/components/BankAccountFormDialog.tsx`, `BankingTabs.tsx` |
| Integration capability-specific surfaces | `src/Web/src/lib/integrations.tsx`, `src/Web/src/components/IntegrationsSettingsTab.tsx` |
| Current chat context contract | `src/Web/src/assistant/contextVariables.ts`, `src/Api/Controllers/AiChatController.cs`, `src/Api/Service/Ai/AnterRunStreamBody.cs` |
| Existing financial MCP tools | `src/Api/Mcp/Tools/FinancialMcpTools.cs` |

## Assistant integration boundary

`accountgo-assistant` is the configured upstream Anter agent. Updating these repository skills does not itself upload/promote an Anter agent revision or change the chat context contract. The reviewed financial MCP tool file exposes financial reads, not an operating-settings read tool. Do not instruct the assistant to invoke a nonexistent `get_operating_settings` tool.

For fully personalized answers, the runtime must expose an authenticated current-state read or a server-derived policy/permission snapshot with trustworthy ownership. The local skill already supports conditional help and UI-based verification when that is unavailable. Merely adding client context variables would not establish trustworthy policy state. Agent Builder promotion/history described in architecture docs is not proof of the currently deployed version.

## Behavioral review cases

Use these when reviewing changes; judge the actual decision, not exact phrasing:

| User situation | Expected behavior |
| --- | --- |
| Owner lacks journal view/create grants | Explain administrative role versus functional access; do not assert automatic bypass. |
| JustMe/SoloActive plus effective independent rule | Follow effective review; return-to-solo preserves it. |
| WithOthers + TeamSetupPending + no eligible reviewer | Explain accepted reviewer and posting access required; do not claim a pending invitation can approve. |
| General setup summary says self-post but Journal Approval says independent | Follow effective policy; disclose summary's original-intent wording. |
| Signup continuation | About + stored read-only Review; no plan/currency/book settings changes and no duplicate workspace. |
| financials absent | Invoicing dashboard remains, Banking can still appear; GL/report availability depends on respective modules/permissions. |
| Read-only module / exhausted limit | Explain read-versus-write or specific allowance; do not diagnose every issue as role access. |
| Checklist tax/COA/invite stays unfinished | Explain current builder tracking limits; do not claim posting is blocked. |
| Admin opens Journal Approval | Can inspect policy; only Owner can change it. |
| Single member with historical collaboration | No automatic legacy solo eligibility; use server eligibility. |
| Missing live policy/tool | Conditional guidance and targeted UI check; no invented inspection or tool call. |
| Visible Set up my access rejected for revoked invitation history | Explain strict history eligibility; offer independent role request only with a qualified other approver, otherwise support/operator handoff. |
| Sole Owner without any recovery action or eligible approver | Do not fabricate a recovery wizard; identify deployment support/operator and prepare an accurate private handoff. |
| Change existing Business purpose to Personal | Explain read-only purpose and no exposed conversion flow; do not reuse continuation or promise transaction migration. |
| Invited member asks how to start | Guide work in the existing workspace; do not send them to create a duplicate workspace. |
| Full new-workspace wizard shown after verification | Establish which journey is active before promising the button initializes an existing workspace. |
| Personal option Coming soon | Explain rollout availability without promising Personal onboarding. |

## CFO interpretation guardrails

Drafts do not constitute posted books. Sending is not payment. Importing is not posting or reconciliation. Use document source postings instead of duplicate manual entries. “Recorded cash + receivables − bills” on the dashboard is neither available cash nor a cash forecast, and is unavailable when any input is unauthorized/unloaded. Framework selection and report basis controls describe application configuration; they are not certification of GAAP/IFRS compliance.
