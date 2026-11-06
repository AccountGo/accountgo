# Onboarding and first workflows

## Choose the journey

Start with the user's goal: create a workspace, finish a signup-created workspace, prepare existing books, or learn a first transaction workflow. Use current operating settings, entitlements and permissions when available; otherwise state the relevant conditions. Do not repeatedly ask for facts already supplied by trusted workspace reads.

For a practical walkthrough, explain what to click now, what should appear, the resulting status, and the next meaningful milestone. Offer one next step at a time when accompanying a user through the UI. A checklist and a posted document are different completion signals.

## Guide to an observable result

Read [workspace-operating-settings.md](workspace-operating-settings.md) before personalizing the journey. Use the platform guide's trusted-context rules: the skill alone cannot inspect live operating policy or permissions. If no authenticated read is available, ask for the relevant visible Workspace setup or Journal Approval value and give conditional guidance.

- State the goal, known setup and relevant access requirement in plain language.
- Explain what should appear and branch on the actual rollout, wizard mode, product surface, collaboration lifecycle, write access and effective review policy.
- Explain document effects at the decision point: draft, issued/approved, posted, paid, emailed, matched and reconciled are different milestones.
- Check the actual resulting workspace, document status/balance, GL transaction or finished reconciliation. The checklist is not a readiness certification.
- When blocked, explain the evidenced gate and next permitted action; do not assume an upgrade or Owner bypass will solve it.

A walkthrough does not authorize posting, paying, emailing, inviting, changing access or subscriptions, or lowering independent-review controls. Follow the user's authorized scope and explain consequences before an action.

## Create a new workspace

Open `/create-org` from workspace selection/switcher. The current full wizard runs:

1. **About your workspace**: **What is this workspace for?** → **Freelance or self-employed** or **A business or company** (then **What kind of business?**). Then **Also keep your personal money?** when offered, and **Who will use this workspace?** → **Just me** or **Me and others**. These answers do not change the plan. If rollout capabilities disable this experience, **Standard workspace setup** replaces the choices; do not instruct the user to select hidden options. Details below.
2. **What do you need?**: choose an offered **Invoicing**, **Full accounting**, or **Enterprise** plan. Operator-assigned/non-self-service plans may be omitted. Current catalog and allowed transitions determine real availability; do not promise a price or enterprise entitlement from these labels alone.
3. **Create your workspace**: name, workspace address/slug and optional legal name. The slug becomes the workspace URL.
4. **Money & time**: **Currency**, **Books start date**, and **Fiscal year** (Calendar, Apr–Mar, Jul–Jun, or Custom start month). Preview the first fiscal year. Currency is the books currency; values are not automatically converted later.
5. **How you report**: **Reporting framework**, **Tax posture**, **What do you sell?**, and conditional **Inventory costing**. The wizard maps IFRS for SMEs, US GAAP (private), and Full IFRS to Accrual; Cash basis to Cash. This describes product behavior, not a professional conclusion that choosing a framework ensures compliance. Inventory choices are Weighted average/FIFO when relevant; the selector can appear from industry/enterprise choice without independently enabling the inventory module.
6. **Review & create**: review setup, access, journal workflow, currency, books start, fiscal year, framework, tax and industry; select **Create workspace**. The provisioning process creates accounts/defaults without sample transactions, then opens the dashboard.

Explain the operating consequences before completion: Just me initially allows the preparer to post under the solo rule; Me and others keeps the Owner's access but initially requires independent journal review. A teammate must accept and hold the right access before they can review. Availability is determined by trusted setup capabilities.

### What the About answers change

The About step asks three things in order. None of them changes the plan or price; the plan is chosen in step 2.

**1. What is this workspace for?**

| Choice | Books it creates |
|---|---|
| **Freelance or self-employed** | Business purpose, **Sole proprietor** profile: equity is 30100 **Owner's Capital** with drawings (30800) for money taken for personal use. Dashboard uses work wording (**Money in**, **Money out**, **Taken for personal use**). This is the profile that can link to personal books. |
| **A business or company** | Business purpose. A follow-up **What kind of business?** asks **Company**, **Partnership**, **Cooperative** or **Non-profit**; each uses its own equity accounts. |

The wizard does not offer "Personal finances" as a purpose. Personal money has its own flow: personal books (below).

**2. Also keep your personal money?** (shown after the choice above, only when the person can still add personal books)

- **Yes, set up my free personal books too** creates a separate personal-books workspace and links it to the new work workspace.
- **Not now** skips it. They can add it later from the workspace switcher.
- Personal books are free and don't use the person's free-workspace slot. If the review step shows a **Personal books** row, it reads **Free, linked to this workspace** or **Not now**.
- If the work workspace is created but personal books fail, the page says the workspace is ready and points to the switcher.

**3. Who will use this workspace? Just me or Me and others**

| | Just me | Me and others |
|---|---|---|
| Owner access | Full workspace access (within the plan) | Full workspace access (within the plan) |
| Journal posting | **Single-user posting**: the Owner prepares and posts their own journals | **Independent review**: another Owner or Admin who can post journals approves each journal. Until someone accepts an invitation with that access, manual journals wait for review |
| Collaboration status | Solo (`SoloActive`); the checklist omits Invite a teammate | Team setup pending (`TeamSetupPending`) until an invitee accepts, then Team active |
| Later change | Owner can change **Journal Approval**; Team & Members offers **Return to just me** when eligible | Same |

Choose **Me and others** only when a second person will actually review journals (partner, bookkeeper, accountant). A solo user who picks it blocks their own manual journals until a reviewer joins. Invoices, bills and payments are not blocked by the review rule; it applies to journal posting.

## Personal books

Personal books are a separate workspace for household money. Use this section when someone asks "how do I track my personal money", "do I have personal books", or "can my freelance workspace be my personal one".

**Identify them without plan keys.** A personal-books workspace shows **· Personal** next to its name in the workspace switcher and the dashboard, and the app treats it as `isPersonalBooks` (the `currentOrg` flag). Do not infer it from the plan name, and do not state plan prices or limits from memory; read `/api/entitlements` when exact limits matter.

**What they are**
- One personal-books workspace per person. It cannot be transferred to someone else.
- Real double-entry books underneath, with one equity account, 30100 **Accumulated net worth** (profile **Individual**). The household wording is a presentation layer, not a separate ledger.
- No Sales module, so no invoices or clients. Modules still decide what exists: a workspace with sales keeps its sales items.
- Always single-user posting. The accounting policy (entity type, reporting framework, tax regime, inventory costing) is locked; the server rejects changes with `personal_books_policy_locked` and any approval rule other than single-user with `personal_books_single_user`. Fiscal year and books start follow the normal rules.

**How to create them**
1. In the wizard, answer **Yes, set up my free personal books too** on the About step, or
2. Use **Only want to track personal money?** on that step, or
3. In the workspace switcher choose **Add personal books**, then enter **Name** and **Currency**, optionally tick **Link to {work workspace}**, and select **Create personal books**.

If the person already has a personal-purpose workspace and it is eligible, the switcher shows **Use as my personal books** on it; the dialog **Use {name} as your personal books?** registers that existing workspace instead of creating a new one. The workspace keeps its data. It then follows personal-books rules, including no upgrade prompts. Turning a personal-books workspace back into freelance books is not a self-service action; it is an operator correction that is blocked while the workspace is linked or has linked entries.

**What they see**

| Area | In personal books |
|---|---|
| Sidebar | **Dashboard**, **Bank accounts**, **Bills**, **Transactions** (the general ledger), **Reports**, **People**, **Settings**. **Products & services** is hidden. Routes and permissions are unchanged. |
| Dashboard | Household labels: **Earned income**, **Money from my freelance work**, **Owed to you**, **You owe**, **Net worth (book value)**, **Who owes you**, **Bills coming due**. A **Linked to {work workspace}** chip appears when linked. |
| Settings | Title **Settings**, subtitle **Manage your personal books.** Tabs: **Profile** (name, currency, owner details, danger zone), **Books** (fiscal year, start month, books start date, books lock, year-end close), **Roles & Permissions**, **Plan**. Branding, Integrations, Invoices, Email, Taxes and Journal Approval are hidden; a hidden tab in the URL falls back to Profile. |
| Plan tab | **Personal books — free** and a read-only module list. No plan ladder, upgrade or billing actions anywhere, including limit messages (**This action is unavailable in these books.**). |
| Reports | **Net worth (book value)** (Assets, Debts, Net worth), **Money in and out** (Earned income and **Money from my freelance work** on separate lines, spending by category, **Net**), **Cash movement** (one list of cash in and out with opening and closing cash), and **Trial balance** under **Advanced**. |

Wording to use and avoid: say net worth, book value, money in and out. Do not call these "personal financial statements", and do not say profit, revenue or deductible for personal books; they are historical-cost household records, not current-value statements or tax advice.

## Link work and personal books (linked entries)

A freelance (sole proprietor) workspace can link to the owner's personal books. One work workspace links to one personal workspace. The link is made at creation (the wizard answer, or the **Link to {work workspace}** tick in **Add personal books**); the app has no unlink screen. Company, partnership, cooperative and non-profit workspaces never get linked-entry actions; that is by design.

Linking is done from a bank statement line on **Bank statement import** (not by hand in the ledger), in the work workspace only. Unmatched withdrawals show a **Personal** button; unmatched deposits show **Money I put in**. Each opens **Link this bank line**, which creates a balanced journal in each workspace:

| Action | Work books | Personal books |
|---|---|---|
| **Personal** → **Personal spending** (withdrawal; choose a **Household spending category**) | Owner's drawings (30800) up, work bank down | The household expense, with the matching credit to **Money from my freelance work** (40900) |
| **Personal** → **Move to a personal bank account** (choose a **Personal bank account**) | Owner's drawings up, work bank down | The personal bank account up, credit to 40900 |
| **Personal** → **Split part of this purchase as a work expense** (**Work expense category**, **Work amount**) | The work part is expensed, the rest goes to drawings | Only the personal part is recorded |
| **Money I put in** (deposit) | Work bank up, **Owner's Capital** (30100) up | 40900 reduced, personal bank down |

Rules to explain:
- The posting date must fall in an open fiscal year in both workspaces; the error names the workspace that blocks it. If the work workspace requires independent review, both sides wait as a pending pair until approved; rejecting or cancelling deletes both drafts.
- Account 40900 **Money from my freelance work** is created in the personal books on the first linked entry. It is reported apart from earned income, never inside it.
- A pair's journals cannot be edited or posted singly. Correct them with **Reverse** on the linked entry, which reverses both sides and leaves the originals. In a workspace that requires independent review, reversal is not supported yet; the Owner must switch journal posting to single-user, reverse, and switch it back (both changes are audited).

## Finish setup after signup

A signup-created workspace can be left uninitialized until the Owner verifies email. Trusted `operatingPolicy.pendingSignupContinuation` drives the continuation; do not infer it from the URL or an old local draft alone.

The continuation route is `/create-org?continue={orgSlug}`. It presents only **About your workspace** and **Review & create**. Review shows stored workspace facts read-only. Finishing sends About answers to initialize the existing workspace; it does **not** change plan, identity, currency, books date, fiscal year, tax/framework/industry/inventory or create a second workspace. Plan changes happen afterward in Subscription & Modules through allowed billing transitions. The current final button still reads **Create workspace**; explain the continuation's actual effect if that wording confuses the user.

## Get books ready

The dashboard's **Get books ready** card is shown only for managing users while not dismissed and successfully loaded. It is a nonblocking helper, not an enforced readiness certification.

| Item | When shown | Destination / completion behavior |
| --- | --- | --- |
| Confirm your tax rates | Tax regime is not `None` | `/settings?tab=taxes`; hint warns of a 0% placeholder. Always unfinished in the current builder. Verify actual rates before the first taxed document. |
| Add your bank account | `financials` available | `/banking`; marked done when at least one nonarchived bank account exists. Opening balance may still need work. |
| Skim your chart of accounts | `financials` available | `/financials/accounts`; remains unfinished in current builder. |
| Add your legal name and contact | Always in the built checklist | `/settings`; done is based on presence of legal name only, not verification of every contact field. |
| Invite a teammate | Omitted only for `SoloActive` | `/members`; optional and always unfinished in builder. Available separately even when omitted from the checklist. |

Dismissal persists via the onboarding checklist dismiss endpoint. The card does not track actual tax confirmation, COA review or invitation completion. Do not claim “all tasks complete” or block posting because the card says tasks remain. Its absence can mean dismissed, no management access, or unavailable data, rather than completed books.

Recommended finance preparation: confirm business identity and actual tax rates; inspect seeded control accounts and books start; set up bank/cash accounts and reconciled opening balances without duplicating cash history; confirm permissions and effective journal policy. These are operational recommendations, not extra product-enforced wizard steps.

## First invoice → payment

Use [sales-and-clients.md](sales-and-clients.md).

1. Confirm sales access, actual tax configuration and payment destination.
2. **Invoices & Sales → Clients → New client**; save the client or create it in the invoice editor.
3. Start **New invoice**, choose the client, dates/terms and lines; save a draft.
4. Review amounts and recipient, then issue. Issuance affects the sales balance and posts when accounting applies; draft creation does not. Sending is a separate external action requiring the UI's invoice-number confirmation.
5. Record the received payment against the issued invoice and select the bank destination offered. Verify remaining balance and payment status; do not equate an emailed invoice with money received.

Success: correct issued document, then receipt and expected balance; when accounting is available, trace the source posting rather than adding a duplicate manual journal.

## First vendor bill → payment

Use [purchasing-and-bills.md](purchasing-and-bills.md).

1. Confirm purchases access; create **New vendor** or select an existing vendor.
2. **New bill** → vendor, supplier reference, dates/terms and lines/accounts/tax → **Save draft**.
3. Review, then **Save & approve** or approve the saved draft using the displayed control. Approval records the payable and accounting posting; saving alone does not.
4. Pay the approved bill, choose the payment source and verify remaining balance. Capture a real payment once; do not re-enter it as an unrelated bank transaction.

Success: approved bill, recorded payment, expected outstanding balance and source links.

## First journal → review → post

Use [financials-and-gl.md](financials-and-gl.md). Confirm financials write access and effective journal workflow first.

Prepare balanced lines using postable accounts → **Save draft**. In single-user mode, a permitted user can **Post journal**. Under independent review, a different eligible Owner/Admin records approval on the saved unchanged draft; the posting user supplies **Approval request ID** and **Approver user ID**, then posts. Editing after approval or policy changes may require renewed review. Do not invent a generic “submit to approval inbox” step.

Success: posted status/GL transaction with balanced lines, not just a saved draft or an approval ID.

## First bank statement → reconcile

Use [banking-and-review.md](banking-and-review.md). Create/adopt the correct account and opening balance first, then use the available statement import capability, review/match imported rows and reconcile against the actual statement. Imported data, linked documents, posted cash and reconciled cash are separate milestones.

## If the journey differs

Re-read operating settings after invitations, role changes, return-to-solo or policy changes. For a missing button, inspect the actual denial and layered gates in [workspace-operating-settings.md](workspace-operating-settings.md). Ask for the visible message or selected setting only when authenticated state is unavailable. Keep the user progressing with available tasks without pretending a blocked posting succeeded.


## Example openings for a guided conversation

**User:** “I have just joined. What should I do first?”

**Assistant:** “Are you finishing setup for a workspace you created, or joining one someone invited you to?”

Ask this only when the journey is unknown. An invited member should not be sent through new-workspace creation. If the workspace is already established, ask which first task they want to complete and guide that workflow with their access.

**User:** “I verified my email and see Create workspace again.”

**Assistant:** “If you are finishing setup for your existing workspace, you should see About your workspace and then a review of its stored details. That flow finishes the existing workspace even though the final button says Create workspace. Does the review show your existing workspace?”

Skip the conditional question when trusted continuation state is already available. If the full new-workspace wizard is shown, do not reassure the user that it cannot create a duplicate.

**User:** “Me and others is selected, but my accountant has not joined yet.”

**Assistant:** “Your team setup is still in progress. You can prepare a journal draft, but independent review needs another eligible Owner/Admin with journal-posting access to accept and approve it. Invite your accountant through Team & Members and confirm the access they will receive.”

Only suggest drafting when the caller has draft access. The accountant's job title alone does not make them an eligible reviewer.

For blocked access, changing the original choices, or a support handoff, use [Access recovery and setup changes](access-recovery-and-setup-changes.md).
