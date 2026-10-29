# Settings, Members, Roles & Subscriptions Guide

This reference covers user management under **Team & Members** (`/org/{orgSlug}/members`) and administration under **Organization Settings** (`/org/{orgSlug}/settings`).

---

## 1. Team & Members (`/org/{orgSlug}/members`)

All workspace members can view the member directory. Only **Owners** and **Admins** can invite new members, change access roles, or revoke invitations.

### Workspace Role Hierarchy
1. **Owner**: Has full, unrestricted control of the workspace, subscription, and billing. There is exactly one Owner per workspace. Ownership can be transferred to another active member.
2. **Admin**: Has full access to all workspace features, settings, members, and financial records. Can invite members and configure security roles.
3. **Member**: Standard team member whose capabilities are strictly governed by their assigned **Security Roles**.

### Inviting a New Team Member
1. In the left sidebar, click **Team & Members** under Administration (`/org/{orgSlug}/members`).
2. Click the **Invite member** button in the top-right header.
3. In the invite form, provide:
   - **Email**: The teammate's email address.
   - **Workspace role**: Choose **Member** (default) or **Admin** (only available if you are the Owner).
   - **Security roles**: If the role is **Member**, check the security roles that apply to this user (e.g., *Invoicing Staff*, *General Accountant*, *Sales Representative*).
4. Click **Send invitation**. The recipient receives an invitation email with a secure link to join the workspace (`/invite/{token}`).

### Managing Pending Invitations
Below the active members list, the **Pending invitations** section displays pending invites:
- **Resend**: Sends a fresh invitation email with an updated expiration window.
- **Revoke**: Cancels the invitation link immediately.

### Editing Access, Removing Members & Leaving
- **Edit Access**: Click **Edit access** next to any Member to adjust their workspace role or check/uncheck security roles, then click **Save access**.
- **Remove Member**: Click **Remove** to revoke a user's access to this workspace.
- **Leave Workspace**: Members or Admins can click **Leave** to remove themselves from the organization. An Owner cannot leave until they transfer ownership.
- **Transfer Ownership**: If you are the Owner, click **Transfer ownership** next to an active member. They become the new Owner and you are demoted to Admin.

---

## 2. Organization Settings (`/org/{orgSlug}/settings`)

Accessible **only** to users with the **Owner** or **Admin** workspace role. Non-admin members navigating here receive a Permission Denied notice.

Settings are organized into groups in the vertical navigation menu:

```
Workspace
├── General Profile
└── Whitelabel & Branding
Sales
└── Invoices
Sales & Purchases
└── Taxes
Accounting (visible when financials module is active)
└── Financial Rules & Banking
Access & Billing
├── Roles & Permissions
└── Subscription & Modules
```

### A. General Profile (`?tab=general`)
- **Organization Name**: The display name of the company or legal entity.
- **Short Name / Company Code**: Optional short identifier.
- **Tax / CRA Registration**: Official business tax registration number.
- **Workspace Slug**: The URL identifier (e.g., `acme-corp` in `/org/acme-corp/...`). Changing this changes the URL for all workspace links.
- **Currency**: Primary reporting currency (e.g., `USD`, `PHP`, `CAD`, `EUR`).
- **Business Details**: Street address, city, country, phone, and website.
- **Delete Workspace**: Located at the bottom of the General Profile tab. Requires typing the organization slug to confirm permanent deletion.

### B. Whitelabel & Branding (`?tab=whitelabel`)
- **Brand Name**: Overrides the header title (defaults to AccountGo).
- **Theme**: Select between interface themes (such as `accountgo`, `tokyo-night`, `sapphire`).
- **Color Scheme**: Choose **Light**, **Dark**, or **System** (matches OS preferences).
- **Custom Primary Color**: Pick a custom brand accent color applied to buttons and active indicators.

### C. Invoices Settings (`?tab=invoices`)
- **Invoice Numbering**: Configure sequence prefixes, minimum padding digits, and the next invoice sequence number.
- **Default Payment Terms**: Set the default terms for new clients and draft invoices (e.g., *Net 30*).
- **Bill To Email Display**: Toggle whether the client's email is printed in the Bill To section on invoices and PDF downloads.

### D. Taxes (`?tab=taxes`)
*(Available when `sales` or `purchases` module is active; requires `admin.taxes.manage` permission)*
- Configure tax rates (e.g., VAT, Sales Tax, GST/HST), tax codes, and whether taxes are inclusive or exclusive.
- Group taxes into Tax Groups and Item Tax Groups for automatic calculation on invoice and bill line items.

### E. Financial Rules & Banking (`?tab=financials`)
*(Available when the `financials` module is active)*
- **Fiscal Years**: Define fiscal year start and end dates (e.g., Jan 1 - Dec 31). Mark the active year for ledger period reporting.
- **Payment Terms Master**: Create or edit payment terms (*Net 15*, *Net 30*, *Due on Receipt*, etc.) and due day offsets.
- **Cash & Bank Accounts**: Map bank accounts and petty cash funds to their corresponding General Ledger asset accounts.

### F. Roles & Permissions (`?tab=roles`)
- **Security Role Matrix**: Create custom roles (e.g., *Sales Rep*, *Accounts Payable Clerk*) and toggle granular permissions across Sales, Purchasing, General Ledger, Inventory, and Administration.
- **Workspace MFA Policy**: Enforce Multi-Factor Authentication (OTP / Passkeys):
  - *Optional* — Members can choose whether to enable MFA.
  - *RequiredForAdmins* — Mandatory for Owners and Admins.
  - *RequiredForAll* — Mandatory for all workspace users, with an optional grace period.

### G. Subscription & Modules (`?tab=subscription`)
- **Plan Tiers**:
  - **Invoicing Plan**: Focused on sales invoices, quotes, client management, and payments. General Ledger and financial statements are disabled.
  - **Accounting Plan**: Full double-entry accounting engine, general ledger, trial balance, balance sheet, profit & loss, cash flow, and bank tracking.
  - **Enterprise Plan**: Multi-branch, custom audit logs, priority compute, and custom whitelabeling.
- **Active Modules Checklist**: Toggle optional modules on or off:
  - `sales` (Invoices & Clients)
  - `purchases` (Bills & Vendors)
  - `financials` (General Ledger & Chart of Accounts)
  - `inventory` (Stock tracking & adjustments)
  - `reports` (Trial Balance, P&L, Balance Sheet, Cash Flow)
  - `audit` (Security & entity audit trail logs)
