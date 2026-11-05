# Access recovery and changes after setup

Read this for “I am the only Owner and cannot work”, rejected access setup, missing recovery controls, or changing the original workspace choices. Use [workspace-operating-settings.md](workspace-operating-settings.md) for interpreting current state. Paths here are relative to `/org/{orgSlug}` unless explicitly API paths.

## Recover access using the action actually offered

Start at **Team & Members** (`/members`) in the intended workspace. Establish the caller's role, current members, access summary and visible error/banner. If trusted state is unavailable, ask for the exact visible message; do not ask the user to obtain raw API payloads.

| What is shown | Next step | What it does not establish |
| --- | --- | --- |
| **Solo Workspace Setup Available → Confirm solo setup** | Review and confirm only if the Owner wants the eligible solo setup | Banner wording does not override an explicitly retained independent-review policy. |
| **Set up your access → Set up my access** | Select functional roles and save using the procedure below | A visible button is not proof the backend will authorize the grant. |
| **Request functional roles** | Submit a role request for an independent eligible reviewer | Submission is not granted access and the requester cannot self-approve. |
| **Controlled Recovery Required** | Preserve the exact message and use the escalation route below | The banner does not provide a recovery wizard or guaranteed availability. |
| No relevant action, or the page fails to load | Confirm workspace and signed-in account, use **Refresh** once, then capture the result | Missing controls can reflect unavailable state; do not conclude the workspace is unrecoverable from absence alone. |

### Set up my access

1. Open **Team & Members → Set up my access**. The form is **Set up your functional roles**.
2. Select the offered **Functional roles** needed for the intended work. Do not invent role names or promise a particular managed profile is offered.
3. Choose **Save and activate roles**.
4. On success, the page reloads its data and updates current permissions. Verify the intended menu/action is now available; module, read-only and journal policy checks still apply.
5. On rejection, use the exact error to select the next route. Repeatedly clicking Save or returning to just me does not remove historical restrictions.

**Important eligibility caveat:** the UI offers this action using a broad “active Owner with no other active members” check, excluding the eligible-solo-confirmation branch. The backend additionally requires continuous original-owner evidence, uninitialized/unclassified operating policy, no other membership records, and no invitation, ownership/policy-transition or collaboration history. Even a revoked invitation can block this particular self-grant route. Therefore an initialized Solo workspace, a former team, or a transferred Owner can see the action but be rejected.

This `owner-access-setup` action is distinct from the older one-time `access-setup` bootstrap. The older route has its own availability gate and is not a supported bypass when the current UI action fails. Describe the user's visible action rather than directing them to invoke endpoints manually.

### Request functional roles

1. Open **Team & Members → Request functional roles**, when offered.
2. Select the needed **Functional roles**. An administering caller can choose **Target member**; leave **Myself** when requesting their own access.
3. Enter **Reason / Business justification**, then **Submit request**.
4. A different authorized active Owner/Admin reviews the request in Team & Members. When offered, **Approve → Approve request** applies the roles immediately. The requester and beneficiary cannot approve their own request.
5. Check the applied status and resulting access. Do not treat a Pending request as complete or confuse this role-request process with journal review approvals.

If no independent eligible reviewer exists, submitting another request does not solve that absence. Do not suggest inviting a nominal approver, transferring ownership, removing history or changing journal policy to bypass access recovery. A genuine collaboration change is a separate user decision, not a recovery shortcut.

### When self-service cannot complete

The reviewed UI displays **Controlled Recovery Required** as an informational banner; it does not contain a controlled-recovery action. The backend can also report that controlled recovery is unavailable. No complete self-service recovery procedure is implemented in the reviewed surfaces for an Owner rejected by the grant checks with no eligible independent approver.

Explain the limit directly, then give a concrete handoff:

- For a hosted workspace, use the support channel provided by that deployment. For a self-hosted workspace, contact the person/team operating the AccountGo installation. If no contact is known, ask whether the workspace is hosted or self-hosted to identify the right recipient. Do not invent an in-app support button, email address or recovery SLA.
- Provide a short issue summary containing workspace slug, signed-in account identifier, administrative role, exact error/banner, time of attempt, which action failed, whether any other active Owner/Admin exists, and relevant invitation/ownership history. Share account details through the private support route, not a public issue.
- State what is requested: review the failed access eligibility and identify an authorized recovery process. Do not promise support can restore a grant, change policy or bypass historical safeguards.
- Do not request passwords, tokens, database edits or deletion of audit/history records. Do not claim a support request was submitted without actually having an authorized sending capability.

Example handoff: “I am the Owner of workspace [slug]. Set up my access fails with [exact message]. There are [number] other active Owner/Admin members. Relevant history: [known facts]. Please review eligibility and advise the authorized recovery route.” Include only facts actually known.

## Which original choices can change?

| User wants to change | Supported direction in the reviewed product | Limit to explain |
| --- | --- | --- |
| Business ↔ Personal purpose | No existing-workspace purpose conversion UI/API is exposed in the reviewed settings/organization controller | Workspace setup is read-only. Personal onboarding availability is a separate question; it does not enable conversion. |
| Just me → sharing with others | **Team & Members → Invite member**, then accepted membership | Sending an invitation alone does not activate team collaboration or automatically change journal review. |
| Team → just me | **Return to just me** when the Owner is eligible and the action is offered | Other active members must be addressed first; confirmation revokes outstanding invitations and preserves grants/journal policy. |
| Whether the preparer can post journals | **Organization Settings → Journal Approval** | Owner-only change, financials availability and reviewer-capacity validation apply. |
| Functional business access | The eligible access setup or independent role-request routes above | Ownership/plan changes do not replace functional grants. |
| Plan or optional modules | **Organization Settings → Subscription & Modules** | Current catalog, allowed billing transitions and included/required module rules apply. |
| Organization name, slug or currency | **General Profile** offers organization settings | Currency changes are rejected after the first journal posting; changing currency never converts historical amounts. |
| Framework, basis, fiscal year or books start | Accounting-policy update contract exists | These fields lock after the first journal posting. A settings update endpoint is not proof there is a corresponding editor in the UI; do not invent clicks for fields absent from the guide. |

Finishing signup setup is not a general settings editor and cannot be reused to rewrite an initialized workspace. Do not recommend creating a replacement workspace as a conversion/recovery fix without a deliberate migration decision; the guide documents no automatic transfer of transactions, balances or history.

## Conversational examples

Use these as decision examples, adapting to known state. Keep implementation names and enum values out of ordinary user answers unless troubleshooting requires them.

**User:** “I am the only Owner. Set up my access is missing.”

**Assistant:** “Open Team & Members in the affected workspace and use Refresh. You may see Confirm solo setup or Set up my access depending on its history. If neither appears, tell me the banner or error shown so I can identify the available route.”

If trusted state already proves those routes unavailable, skip the refresh/question and give the escalation steps. Do not repeat a check the user has already completed.

**User:** “Set up my access says Invitation history exists. I revoked the invite.”

**Assistant:** “Revoking the invitation does not remove the history check for this access setup. If Request functional roles is available, a different eligible Owner/Admin must approve the request. If nobody can independently approve, this needs a recovery review through your deployment's support contact or installation operator.”

**User:** “I want this Business workspace to become Personal.”

**Assistant:** “Workspace setup shows your recorded purpose, but it is read-only. The current guide has no supported conversion flow for an existing workspace. Personal finances being available during new-workspace setup would not change that. An existing workspace with transactions needs a supported migration decision before replacement.”

**User:** “I returned to just me. Why do journals still require approval?”

**Assistant:** “Return to just me keeps the journal approval policy you already had. As the Owner, you can review it under Organization Settings → Journal Approval. Allow single-user posting removes the second-person check and records that policy change; returning to solo alone does not make that change.”
