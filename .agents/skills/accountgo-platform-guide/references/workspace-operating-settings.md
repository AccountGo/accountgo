# Workspace operating settings

## Read the dimensions independently

Trusted workspace reads are `GET /api/organizations/current` (contains `operatingPolicy`) and `GET /api/organizations/current/operating-settings` (detailed summary). Setup availability is `GET /api/organizations/setup-capabilities`. Use only an authenticated capability actually available in the host; these paths do not create assistant tools.

| Dimension | Values / source | What it determines |
| --- | --- | --- |
| Purpose | `Business`, `Personal`, `Unspecified` | Recorded use; Personal can affect dashboard presentation. Does not buy modules or grant access. |
| Collaboration intent | `JustMe`, `WithOthers`, `Unspecified` | Setup answer, not proof of present reviewer capacity or effective policy. |
| Collaboration state | `SoloActive`, `TeamSetupPending`, `TeamActive`, `LegacyUnclassified` | Current collaboration lifecycle. Never infer it solely from member count. |
| Functional access | Effective permissions; `ownAccessProfileKey`, `ownAccessProfileDisplayName` | Which business actions the caller can perform. **Full workspace access** remains bounded by the plan and approval rules. |
| Workspace role | Owner, Admin, Member | Administrative authority. Ownership alone does not imply journal posting permission. |
| Journal workflow | `journalWorkflow`; `journalApproval.mode` when supplied | Effective normal journal rule: `SingleActorAllowed`, `IndependentReviewerRequired`, or `CatalogDefault`. |
| Entitlements | `/api/entitlements` | Effective plan, modules, read-only modules, features, limits, lifecycle and allowed transitions. |
| Accounting policy | `/api/organizations/current/accounting-policy` | Books start, framework/basis, taxes and inventory defaults; distinct from operating policy. |

Detailed operating settings also expose `policyRevision`, `soloSetupEnabled`, `allowedNextActions`, and `pendingSignupContinuation`. For Owner/Admin, `journalApproval` includes `provenance`, `eligibleOtherReviewerCount` and `canChange`. Ordinary Members do not receive that governance detail; null does not mean there is no policy.

## What users should see

| Current state | Expected experience | Next guidance |
| --- | --- | --- |
| New Business + Just me, enabled and eligible setup | Owner with Full workspace access within the plan; `SoloActive`; initially single-user journal posting | Begin the first workflow. Dashboard checklist omits Invite a teammate; Team & Members remains available. |
| New Business + Me and others | Owner retains Full workspace access; `TeamSetupPending`; UI initializes independent journal review | Invite another qualified reviewer. Until they accept and have posting access, independent posting may be blocked. |
| TeamActive | Team collaboration recorded after acceptance; permissions and current journal rule still apply | Check effective workflow instead of assuming every team requires review or every member can approve. |
| LegacyUnclassified | Setup summary can say purpose/collaboration were not recorded; no automatic Full workspace access grant | Check eligibility and available access setup actions; one current member is insufficient evidence of always-solo history. |
| Setup rollout unavailable | About step says **Standard workspace setup** and purpose/collaboration choices are not saved | Use standard functional access setup. Do not promise automatic solo initialization. |
| Pending signup continuation | Existing workspace needs About answers after verification; continuation is About + read-only Review | Finish that workspace, not a duplicate. |

Setup is rollout/enrollment dependent. Personal finances may show **Coming soon**, and backend release also requires approved Individual equity mapping. Never present the presence of Personal-related code as proof that Personal onboarding is released.

## Where to inspect setup versus change policy

1. Owner/Admin opens **Organization Settings → General Profile** (`/settings` or `?tab=general`). **Workspace setup** displays **Purpose**, **Who uses it**, **Your access**, **Journal posting**, and (when classified) **Collaboration status**. It is read-only.
2. Effective journal policy is under **Access & Billing → Journal Approval** (`/settings?tab=approvals`), shown when financials is available to an Owner/Admin. The **Journal approval policy** card shows current mode, **Set by**, and **Other reviewers who can approve**.
3. Only the Owner can change it: **Allow single-user posting** removes the second-person check and is audited; **Require independent review** requires qualified reviewer capacity. An Admin can view the card but cannot change the policy.
4. Enabling independent review is checked for every eligible preparer having a different active Owner/Admin who can post and whose plan permits it. A raw headcount, pending invite, or ordinary Member with posting permission is not sufficient.

**Display caveat grounded in `workspaceSetupLabels.ts`:** the General Profile summary prioritizes collaboration intent when wording **Journal posting**, and can fall back to “Owner · Full workspace access” even without a current access-profile result. Those rows can differ from effective policy or actual caller permissions after changes. Use the Journal Approval card/current policy and effective permissions for actionable answers. Explain this discrepancy rather than promising the summary grants access.

## Solo/team transitions and controls

- **Team & Members** can show **Your access summary**, **Solo workspace (just me)**, **Solo Workspace Setup Available**, **Confirm solo setup**, **Controlled Recovery Required**, **Set up your access**, and **Return to just me**, depending on server eligibility/status. Do not promise every banner to every Owner.
- Legacy confirmation is Owner-only, eligibility checked, revision checked, and audited. Historical memberships, accepted invitations, or ambiguous ownership affect eligibility; current member count alone cannot authorize it. An explicitly established independent-review rule is preserved even if the banner suggests single-actor posting.
- Sending an invitation alone does not activate a team. Acceptance transitions collaboration; existing Owner access is preserved. Do not claim an invitation automatically tightens or relaxes journal approval.
- **Return to just me** is a deliberate Owner action offered by `allowedNextActions` plus UI checks. Other active humans must be removed/suspended first. The UI confirmation revokes outstanding invitations. Existing grants and approval rules are preserved; this does not restore missing Full workspace access or automatically enable self-posting.
- Role changes/removing a reviewer can trigger a policy-capacity conflict. The UI may offer an explicit Owner-confirmed switch to single-user posting. Explain the consequence; otherwise retain independent review and add a suitable reviewer.
- A pending invitation may require explicit reconfirmation on resend if the journal policy changed after issuance.
- Policy mutations use `expectedPolicyRevision`. On a stale revision/conflict, reload current state and review the updated choice before retrying. Do not blindly replay an old confirmation.

## Recovery and changing setup choices

For rejected or missing Owner access setup, independent role requests, controlled recovery and Business/Personal conversion limits, read [Access recovery and setup changes](access-recovery-and-setup-changes.md). Being the only active Owner can make an action visible without satisfying backend history checks.

## Missing-control diagnosis

Check in order: current workspace → module/feature and read-only status → effective permission → document state/period restrictions → approval policy and reviewer eligibility → metered resource limit. Report whichever gate the response or UI actually identifies. Multiple gates may apply.

Examples:

- “I'm the Owner but General Ledger is missing”: check `financials` and `financials.journal.view`; Full workspace access is separate. Team & Members may offer owner access setup or functional role requests.
- “Just me, but I cannot post”: check current journal mode and posting permission. Returning to solo preserves independent review; consult Journal Approval.
- “Accounting plan, but no reports”: check `reports` capability and `reports.view`, not just plan name.
- “I can see records but cannot add”: inspect read-only modules and limits, then permission/status. Do not immediately recommend an upgrade.
