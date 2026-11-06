# Personal books and linked workspaces

## What personal books are

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

**Use as my personal books** appears in the switcher on a workspace only when all of these hold: the signed-in person is its Owner, its recorded purpose is Personal, it is not linked as a work workspace, and the person has no personal books yet. The dialog **Use {name} as your personal books?** only registers the existing workspace; plan, modules, chart of accounts and entries are unchanged. It then gets the personal-books screens and policy locks. Its **Plan** tab stays the normal plan tab if the workspace is on a paid plan, and modules still decide what exists (a workspace with sales keeps its sales items). Turning a personal-books workspace back into freelance books is not a self-service action; it is an operator correction that is blocked while the workspace is linked or has linked entries.

**What they see**

| Area | In personal books |
|---|---|
| Sidebar | **Dashboard**, **Bank accounts**, **Bills**, **Transactions** (the general ledger), **Reports**, **People**, **Settings**. **Products & services** is hidden. Routes and permissions are unchanged. |
| Dashboard | Household labels: **Earned income**, **Money from my freelance work**, **Owed to you**, **You owe**, **Net worth (book value)**, **Who owes you**, **Bills coming due**. A **Linked to {work workspace}** chip appears when linked. |
| Settings | Title **Settings**, subtitle **Manage your personal books.** Tabs: **Profile** (name, currency, owner details, danger zone), **Books** (fiscal year, start month, books start date, books lock, year-end close), **Roles & Permissions**, **Plan**. Branding, Integrations, Invoices, Email, Taxes and Journal Approval are hidden; a hidden tab in the URL falls back to Profile. |
| Plan tab | **Personal books — free** and a read-only module list (an adopted workspace on a paid plan keeps its normal plan tab). No plan ladder, upgrade or billing actions anywhere, including limit messages (**This action is unavailable in these books.**). |
| Reports | **Net worth (book value)** (Assets, Debts, Net worth), **Money in and out** (Earned income and **Money from my freelance work** on separate lines, spending by category, **Net**), **Cash movement** (one list of cash in and out with opening and closing cash), and **Trial balance** under **Advanced**. |

Wording to use and avoid: say net worth, book value, money in and out. Do not call these "personal financial statements", and do not say profit, revenue or deductible for personal books; they are historical-cost household records, not current-value statements or tax advice.

## Account names and codes are defaults, not constants

Account codes and names in this guide (30100, 30800, 40900, Owner's Capital, Owner's Drawings, Accumulated net worth, Money from my freelance work) are what the standard chart uses. A workspace can rename accounts or its entity profile can relabel equity (for example a partnership uses partners' names), so a real chart can differ.

- When an answer depends on a specific account, call the chart of accounts tool (`get_chart_of_accounts`) for the current workspace and quote the name and code it returns. If no workspace data is available, say "the owner's drawings account (30800 in the standard chart)" and say it should be checked.
- Never state a balance or invent an account from this guide. Use ledger tools for balances.
- Linked entries look up the accounts by code: owner's drawings 30800 and owner's capital 30100 in the work workspace, and 40900 in the personal workspace (created on first use). If a workspace has removed or renumbered one of these, linking fails with a message that the required account is not configured; send the owner to the chart of accounts to restore it rather than suggesting another account.

## Link work and personal books (linked entries)

A freelance (sole proprietor) workspace can link to the owner's personal books. One work workspace links to one personal workspace. The link is made at creation (the wizard answer, or the **Link to {work workspace}** tick in **Add personal books**); the app has no unlink screen. Company, partnership, cooperative and non-profit workspaces never get linked-entry actions; that is by design.

Linking is done from a bank statement line on **Bank statement import** (not by hand in the ledger), in the work workspace only. Unmatched withdrawals show a **Personal** button; unmatched deposits show **Money I put in**. Each opens **Link this bank line**, which creates a balanced journal in each workspace:

| Action | Work books | Personal books |
|---|---|---|
| **Personal** → **Personal spending** (withdrawal; choose a **Household spending category**) | **Owner's Drawings** (30800) up, work bank down | The household expense, with the matching credit to **Money from my freelance work** (40900) |
| **Personal** → **Move to a personal bank account** (choose a **Personal bank account**) | **Owner's Drawings** up, work bank down | The personal bank account up, credit to 40900 |
| **Personal** → **Split part of this purchase as a work expense** (**Work expense category**, **Work amount**) | The work part is expensed, the rest goes to drawings | Only the personal part is recorded |
| **Money I put in** (deposit) | Work bank up, **Owner's Capital** (30100) up (the equity account is named Owner's Capital for a sole proprietor; there is no account called Owner's Contributions) | 40900 reduced, personal bank down |

Rules to explain:
- The posting date must fall in an open fiscal year in both workspaces; the error names the workspace that blocks it. If the work workspace requires independent review, both sides wait as a pending pair until approved; rejecting or cancelling deletes both drafts.
- Account 40900 **Money from my freelance work** is created in the personal books on the first linked entry, under the income header 40000, as a revenue-class (credit-side) account. It is reported on its own line, apart from earned income, never inside it.
- A pair's journals cannot be edited or posted singly. Correct them with **Reverse** on the linked entry, which reverses both sides and leaves the originals. In a workspace that requires independent review, reversal is not supported yet; the Owner must switch journal posting to single-user, reverse, and switch it back (both changes are audited).

## One bank account used for both work and personal money

Typical question: "I have a Freelance workspace and a Personal workspace, linked, and I use the same bank account. How is it reconciled?"

Answer from this model; do not describe the two workspaces as unlinked ledgers with no cross-workspace posting, and do not invent accounts:

1. **Only the freelance (work) workspace holds that bank account and reconciles it.** Import the statement on **Bank statement import** and run the reconciliation under **Banking** in the freelance workspace (`/org/{freelance-slug}/banking`). The personal workspace does not need to reconcile the same account; its books show household spending against **Money from my freelance work** (40900), not a bank ledger for that account.
2. For each statement line that is not a business item, use the link action instead of leaving it unexplained: withdrawals → **Personal**; deposits the owner put in → **Money I put in**. The work books then record owner's drawings (30800) or owner's capital (30100), so every dollar on the statement is explained and the work bank ledger agrees with the statement ending balance.
3. A purchase that is partly business: **Personal** → **Split part of this purchase as a work expense** (**Work expense category**, **Work amount**). Only the personal remainder reaches the personal books.
4. If the money goes to a genuinely separate personal bank account of the owner, use **Personal** → **Move to a personal bank account** and choose that **Personal bank account** (a cash account in the personal workspace). The work books record the same drawing, the personal bank account goes up, and 40900 is credited. That personal account is then reconciled in the personal workspace against its own statement.
5. Business receipts and bills stay in the work workspace as normal invoices, payments and bills; they are not mirrored. A client payment is Dr Bank / Cr Accounts Receivable; revenue is recognised when the invoice is issued.
6. Limits: the posting date must be in an open fiscal year in both workspaces; with independent review the pair waits for approval; correct linked journals with **Reverse** on the linked entry, not by editing. Only freelance (sole proprietor) workspaces get these actions.
7. These actions exist only in a linked freelance (sole proprietor) workspace; a company, partnership, cooperative or non-profit workspace has no **Personal** or **Money I put in** buttons, and the answer for them is to keep personal money out of those books.
8. Optional advice, after the answer: a separate business account makes statements cleaner, but it is a recommendation, not a requirement.

Do not tell the user to book mirror equity entries by hand in both workspaces, to use accounts called "Owner's Contributions", "Due from Business" or "Personal Income", or to use a `/banking` route without `/org/{orgSlug}`.

