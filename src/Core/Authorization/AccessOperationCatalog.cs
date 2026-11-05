#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Core.Domain;
using Core.Domain.Security;
using Core.Entitlements;

namespace Core.Authorization
{
    /// <summary>
    /// Versioned operation and policy catalog for AccountGo access control.
    /// Keeps workspace governance capabilities strictly separate from functional business permissions.
    /// </summary>
    public static class AccessOperationCatalog
    {
        public const string CurrentVersion = "2026-11-01.v1";

        // --- Financial & General Ledger Operations ---
        public const string FinancialsAccountsView = "financials.accounts.view";
        public const string FinancialsAccountsManage = "financials.accounts.manage";
        public const string FinancialsAccountsExportSnapshot = "financials.accounts.export.snapshot";
        public const string FinancialsAccountsExportTemplate = "financials.accounts.export.template";
        public const string FinancialsAccountsExportStructure = "financials.accounts.export.structure";
        public const string FinancialsAccountsImportMerge = "financials.accounts.import.merge";
        public const string FinancialsAccountsImportReplace = "financials.accounts.import.replace";
        public const string FinancialsJournalView = "financials.journal.view";
        public const string FinancialsJournalCreate = "financials.journal.create";
        public const string FinancialsJournalPost = "financials.journal.post";
        public const string LinkedEntriesCreate = "financials.linked_entries.create";
        public const string FinancialsPeriodsClose = "financials.periods.close";
        public const string FinancialsReportsView = "financials.reports.view";
        public const string ReportsView = "reports.view";

        // --- Banking Operations ---
        public const string BankingAccountsView = SystemPermissions.BankingAccountsView;
        public const string BankingAccountsPicker = "banking.accounts.picker";
        public const string BankingAccountsManage = SystemPermissions.BankingAccountsManage;
        public const string BankingTransactionsCreate = SystemPermissions.BankingTransactionsCreate;
        public const string BankingReconcile = SystemPermissions.BankingReconcile;
        public const string BankingReconcileUndo = SystemPermissions.BankingReconcileUndo;

        // --- Sales Operations ---
        public const string SalesCustomersView = SystemPermissions.SalesCustomersView;
        public const string SalesCustomersManage = SystemPermissions.SalesCustomersManage;
        public const string SalesInvoicesView = SystemPermissions.SalesInvoicesView;
        public const string SalesInvoicesCreate = SystemPermissions.SalesInvoicesCreate;
        public const string SalesInvoicesPost = SystemPermissions.SalesInvoicesPost;
        public const string SalesInvoicesSendEmail = "sales.invoices.send_email";
        public const string SalesReceiptsView = SystemPermissions.SalesReceiptsView;
        public const string SalesReceiptsCreate = SystemPermissions.SalesReceiptsCreate;
        public const string SalesQuotationsView = SystemPermissions.SalesQuotationsView;

        // --- Purchasing Operations ---
        public const string PurchasesVendorsView = SystemPermissions.PurchasesVendorsView;
        public const string PurchasesVendorsManage = SystemPermissions.PurchasesVendorsManage;
        public const string PurchasesBillsView = SystemPermissions.PurchasesBillsView;
        public const string PurchasesBillsCreate = SystemPermissions.PurchasesBillsCreate;
        public const string PurchasesBillsPost = SystemPermissions.PurchasesBillsPost;
        public const string PurchasesPaymentsCreate = SystemPermissions.PurchasesPaymentsCreate;
        public const string PurchasesOrdersView = SystemPermissions.PurchasesOrdersView;

        // --- Inventory Operations ---
        public const string InventoryItemsView = SystemPermissions.InventoryItemsView;
        public const string InventoryItemsManage = SystemPermissions.InventoryItemsManage;
        public const string InventoryAdjustmentsView = SystemPermissions.InventoryAdjustmentsView;
        public const string InventoryAdjustmentsPost = SystemPermissions.InventoryAdjustmentsPost;

        // --- Donations Operations ---
        public const string DonationsView = SystemPermissions.DonationsView;
        public const string DonationsManage = SystemPermissions.DonationsManage;

        // --- Dashboard & Audit Operations ---
        public const string DashboardSummaryView = "dashboard.summary.view";
        public const string AuditLogsView = SystemPermissions.AuditLogsView;

        // --- Tax & Business Reference Configuration ---
        public const string TaxSettingsView = "admin.taxes.view";
        public const string AdminTaxesManage = SystemPermissions.AdminTaxesManage;
        public const string AdminMembersManage = SystemPermissions.AdminMembersManage;
        public const string AdminRolesManage = SystemPermissions.AdminRolesManage;
        public const string AdminSubscriptionManage = SystemPermissions.AdminSubscriptionManage;

        // --- Workspace Governance Operations (Distinct from Functional Permissions) ---
        public const string WorkspaceCurrentView = "workspace.current.view";
        public const string WorkspaceSettingsUpdate = "workspace.settings.update";
        public const string WorkspaceBusinessProfileUpdate = "workspace.business_profile.update";
        public const string WorkspaceSalesInvoiceSettingsUpdate = "workspace.sales_invoice_settings.update";
        public const string WorkspaceAccountingPolicyUpdate = "workspace.accounting_policy.update";
        public const string WorkspaceBooksLockUpdate = "workspace.books_lock.update";
        public const string WorkspaceOrganizationDelete = "workspace.organization.delete";
        public const string WorkspaceOwnershipTransfer = "workspace.ownership.transfer";
        public const string WorkspaceMembersView = "workspace.members.view";
        public const string WorkspaceMembersUpdateGovernanceRole = "workspace.members.update_governance_role";
        public const string WorkspaceMembersAssignFunctionalRoles = "workspace.members.assign_functional_roles";
        public const string WorkspaceMembersRemove = "workspace.members.remove";
        public const string WorkspaceAccessRequestsCreate = "workspace.access_requests.create";
        public const string WorkspaceAccessRequestsView = "workspace.access_requests.view";
        public const string WorkspaceAccessRequestsReview = "workspace.access_requests.review";
        public const string WorkspaceAccessRequestsCancel = "workspace.access_requests.cancel";
        public const string WorkspaceAccessSetup = "workspace.access_setup.execute";
        public const string WorkspaceInvitationsList = "workspace.invitations.list";
        public const string WorkspaceInvitationsCreate = "workspace.invitations.create";
        public const string WorkspaceInvitationsResend = "workspace.invitations.resend";
        public const string WorkspaceInvitationsRevoke = "workspace.invitations.revoke";
        public const string WorkspaceInvitationsAccept = "workspace.invitations.accept";
        public const string SelfInvitationsAccept = WorkspaceInvitationsAccept;
        public const string WorkspaceSecurityPolicyView = "workspace.security_policy.view";
        public const string WorkspaceSecurityPolicyManage = "workspace.security_policy.manage";
        public const string WorkspaceSubscriptionManage = "workspace.subscription.manage";
        public const string WorkspaceEmailSettingsManage = "workspace.email_settings.manage";
        public const string WorkspaceBrandingSettingsView = "workspace.branding_settings.view";
        public const string WorkspaceBrandingSettingsManage = "workspace.branding_settings.manage";
        public const string WorkspaceBrandingLogoView = "workspace.branding_logo.view";
        public const string WorkspaceBrandingLogoManage = "workspace.branding_logo.manage";

        // --- Platform & Service Principal Operations ---
        public const string PlatformRolesManage = "platform.roles.manage";
        public const string PlatformAuditDefinitionsManage = "platform.audit_definitions.manage";
        public const string PlatformSystemAdmin = "platform.system.admin";
        public const string ServiceEmailDomainReconcile = "service.email_domain.reconcile";

        public const string EmailReconciliationServicePrincipalId = "service:email-domain-reconciliation";

        private static readonly ConcurrentDictionary<string, AccessOperationPolicy> Policies =
            new(BuildDefaultPolicies(), StringComparer.Ordinal);

        public static IReadOnlyDictionary<string, AccessOperationPolicy> GetAll() => Policies;

        public static AccessOperationPolicy? GetPolicy(string operationKey) =>
            TryGet(operationKey, out var policy) ? policy : null;

        public static bool TryGet(string operationKey, out AccessOperationPolicy? policy)
        {
            if (string.IsNullOrWhiteSpace(operationKey))
            {
                policy = null;
                return false;
            }
            return Policies.TryGetValue(operationKey, out policy);
        }

        public static void RegisterOrUpdate(AccessOperationPolicy policy)
        {
            ArgumentNullException.ThrowIfNull(policy);
            Policies[policy.OperationKey] = policy;
        }

        public static string ForPermission(string permission) => permission;

        public static string ForWorkspaceRole(CompanyMemberRole minimumRole) =>
            minimumRole switch
            {
                CompanyMemberRole.Owner => WorkspaceOwnershipTransfer,
                CompanyMemberRole.Admin => WorkspaceSettingsUpdate,
                _ => WorkspaceCurrentView
            };

        /// <summary>
        /// Typed per-path migration inventory across HTTP controllers, MCP tools, application services,
        /// commercial modules, and background jobs.
        /// </summary>
        public static IReadOnlyList<AccessPathMigrationEntry> MigrationInventory { get; } = new[]
        {
            new AccessPathMigrationEntry("HTTP-FILTERS", "HTTP MVC Filters", "RequirePermissionAttribute / RequireWorkspaceRoleAttribute / RequireOperationAttribute", "*", AccessMigrationStatus.Migrated, "Shared Authorization Contract"),
            new AccessPathMigrationEntry("HTTP-FIN-ACCOUNTS", "HTTP & Direct Controller", "FinancialsController (Accounts, JournalEntries, Reports, COA Import/Export, Bank, YearEnd)", FinancialsAccountsView, AccessMigrationStatus.Migrated, "Financials & COA"),
            new AccessPathMigrationEntry("HTTP-FIN-JE-POST", "HTTP & Direct Controller", "FinancialsController.PostJournalEntry", FinancialsJournalPost, AccessMigrationStatus.Migrated, "Financials & General Ledger"),
            new AccessPathMigrationEntry("HTTP-DASHBOARD", "HTTP & Application Service", "DashboardController.GetSummary / DashboardService.GetDashboardSummary", DashboardSummaryView, AccessMigrationStatus.Migrated, "Dashboard Domain Scoping"),
            new AccessPathMigrationEntry("HTTP-WS-ORG", "HTTP Controller", "OrganizationsController (Settings, BusinessProfile, AccountingPolicy, BooksLock, OwnershipTransfer, Delete)", WorkspaceSettingsUpdate, AccessMigrationStatus.Migrated, "Workspace Governance"),
            new AccessPathMigrationEntry("HTTP-WS-MEMBERS", "HTTP Controller", "WorkspaceMembersController (Members, Update, CreateApproval, RevokeApproval, Remove)", WorkspaceMembersAssignFunctionalRoles, AccessMigrationStatus.Migrated, "Delegation & Dual-Control"),
            new AccessPathMigrationEntry("HTTP-WS-INVITATIONS", "HTTP Controller", "WorkspaceInvitationsController & InvitationsController (Create, Resend, Revoke, Accept)", WorkspaceInvitationsCreate, AccessMigrationStatus.Migrated, "Delegation & Live Re-Evaluation"),
            new AccessPathMigrationEntry("HTTP-SALES", "HTTP Controller", "SalesController (Customers, Invoices, Receipts, Quotations, Allocations)", SalesInvoicesView, AccessMigrationStatus.Migrated, "Sales & Receivables"),
            new AccessPathMigrationEntry("HTTP-PURCHASING", "HTTP Controller", "PurchasingController (Vendors, Bills, Payments, PurchaseOrders)", PurchasesBillsView, AccessMigrationStatus.Migrated, "Purchasing & Payables"),
            new AccessPathMigrationEntry("HTTP-BANKING", "HTTP Controller", "BankingController (Accounts, Statements, Reconciliation, Deposits, Transfers)", BankingAccountsView, AccessMigrationStatus.Migrated, "Banking & Reconciliation"),
            new AccessPathMigrationEntry("HTTP-INVENTORY", "HTTP Controller", "InventoryController & InventoryControlController (Items, Categories, Measurements, ICJ)", InventoryItemsView, AccessMigrationStatus.Migrated, "Inventory"),
            new AccessPathMigrationEntry("HTTP-DONATIONS", "HTTP Controller", "DonorsController & DonationsController", DonationsView, AccessMigrationStatus.Migrated, "Donations"),
            new AccessPathMigrationEntry("HTTP-TAX", "HTTP Controller", "TaxController & AdministrationController.Taxes", AdminTaxesManage, AccessMigrationStatus.Migrated, "Tax Reference Configuration"),
            new AccessPathMigrationEntry("HTTP-AUDIT-PLATFORM", "HTTP Controller", "AdministrationController & AuditController (AuditLogs, Roles, AuditableEntities)", AuditLogsView, AccessMigrationStatus.Migrated, "Audit & Platform Admin"),
            new AccessPathMigrationEntry("MCP-FINANCIAL-TOOLS", "MCP Tool Adapter", "FinancialMcpTools (GetChartOfAccounts, GetTrialBalance, GetBalanceSheet, GetIncomeStatement, GetGeneralLedgerDetail, GetCustomerAging, GetVendorBalances, GetFiscalYearStatus)", FinancialsAccountsView, AccessMigrationStatus.Migrated, "MCP Server"),
            new AccessPathMigrationEntry("MOD-EMAIL", "Commercial Module", "EmailSettingsEndpoints / InvoiceEmailEndpoint / EmailAuthorization / CustomEmailDomainReconciliationRunner", WorkspaceEmailSettingsManage, AccessMigrationStatus.Migrated, "Commercial Email Module"),
            new AccessPathMigrationEntry("MOD-BRANDING", "Commercial Module", "OrganizationBrandingEndpoints (GetAppearance, PutAppearance, GetLogo, PostLogo, DeleteLogo)", WorkspaceBrandingSettingsManage, AccessMigrationStatus.Migrated, "Commercial Branding Module")
        };

        private static IEnumerable<KeyValuePair<string, AccessOperationPolicy>> BuildDefaultPolicies()
        {
            var list = new List<AccessOperationPolicy>
            {
                // --- Financial & General Ledger ---
                Functional(
                    FinancialsAccountsView,
                    "POL-FIN-ACC-VIEW",
                    permissionsAll: new[] { SystemPermissions.FinancialsAccountsView },
                    modulesAny: new[] { SystemModules.Financials, SystemModules.Reports },
                    description: "View chart of accounts and account balances."),
                Functional(
                    FinancialsAccountsManage,
                    "POL-FIN-ACC-MANAGE",
                    permissionsAll: new[] { SystemPermissions.FinancialsAccountsManage },
                    modulesAny: new[] { SystemModules.Financials },
                    description: "Create, update, archive, or unarchive general ledger accounts."),
                Functional(
                    FinancialsAccountsExportSnapshot,
                    "POL-FIN-ACC-EXP-SNAPSHOT",
                    permissionsAll: new[] { SystemPermissions.FinancialsAccountsView },
                    modulesAny: new[] { SystemModules.Financials, SystemModules.Reports },
                    description: "Export chart of accounts snapshot with balances."),
                Functional(
                    FinancialsAccountsExportTemplate,
                    "POL-FIN-ACC-EXP-TEMPLATE",
                    permissionsAll: new[] { SystemPermissions.FinancialsAccountsView },
                    modulesAny: new[] { SystemModules.Financials, SystemModules.Reports },
                    description: "Export chart of accounts blank import template."),
                Functional(
                    FinancialsAccountsExportStructure,
                    "POL-FIN-ACC-EXP-STRUCTURE",
                    permissionsAll: new[] { SystemPermissions.FinancialsAccountsManage },
                    modulesAny: new[] { SystemModules.Financials },
                    description: "Export chart of accounts structure for administrative import/editing."),
                Functional(
                    FinancialsAccountsImportMerge,
                    "POL-FIN-ACC-IMP-MERGE",
                    permissionsAll: new[] { SystemPermissions.FinancialsAccountsManage },
                    modulesAny: new[] { SystemModules.Financials },
                    description: "Validate or commit merge-mode chart of accounts import."),
                new AccessOperationPolicy
                {
                    OperationKey = FinancialsAccountsImportReplace,
                    PolicyId = "POL-FIN-ACC-IMP-REPLACE",
                    PolicyVersion = CurrentVersion,
                    Domain = AccessPolicyDomain.WorkspaceComposite,
                    AllowedGovernanceRoles = new[] { CompanyMemberRole.Owner, CompanyMemberRole.Admin },
                    RequiredPermissionsAll = new[] { SystemPermissions.FinancialsAccountsManage },
                    RequiredModulesAny = new[] { SystemModules.Financials },
                    RequiresResolvedTenant = true,
                    RequiresMutableWorkflowState = true,
                    DisallowedWorkflowStates = new[] { "LedgerFrozen", "HasPostings" },
                    Description = "Replace entire chart of accounts on empty books (requires both Owner governance and financials.accounts.manage)."
                },
                Functional(
                    FinancialsJournalView,
                    "POL-FIN-JE-VIEW",
                    permissionsAll: new[] { SystemPermissions.FinancialsJournalView },
                    modulesAny: new[] { SystemModules.Financials, SystemModules.Reports },
                    description: "View journal entries and general ledger transactions."),
                Functional(
                    FinancialsJournalCreate,
                    "POL-FIN-JE-CREATE",
                    permissionsAll: new[] { SystemPermissions.FinancialsJournalCreate },
                    modulesAny: new[] { SystemModules.Financials },
                    disallowedStates: new[] { "Posted", "PeriodLocked" },
                    description: "Create or edit draft journal entries."),
                new AccessOperationPolicy
                {
                    OperationKey = FinancialsJournalPost,
                    PolicyId = "POL-FIN-JE-POST",
                    PolicyVersion = CurrentVersion,
                    Domain = AccessPolicyDomain.WorkspaceFunctional,
                    RequiredPermissionsAll = new[] { SystemPermissions.FinancialsJournalPost },
                    RequiredModulesAny = new[] { SystemModules.Financials },
                    ModuleAccess = ModuleAccess.Write,
                    RequiresResolvedTenant = true,
                    RequiresResourceOwnershipCheck = true,
                    RequiresMutableWorkflowState = true,
                    RequiresSeparationOfDuties = true,
                    DisallowedWorkflowStates = new[] { "Posted", "Void", "PeriodLocked" },
                    Description = "Post or reverse journal entries with workflow and separation-of-duty checks."
                },
                Functional(
                    LinkedEntriesCreate,
                    "POL-FIN-LINKED-ENTRY-CREATE",
                    permissionsAll: new[] { SystemPermissions.FinancialsJournalPost },
                    modulesAny: new[] { SystemModules.Financials },
                    description: "Create a linked journal pair while preserving each workspace's posting policy."),
                Functional(
                    FinancialsPeriodsClose,
                    "POL-FIN-PERIOD-CLOSE",
                    permissionsAll: new[] { SystemPermissions.FinancialsPeriodsClose },
                    modulesAny: new[] { SystemModules.Financials },
                    description: "Execute fiscal year-end close or lock accounting books."),
                Functional(
                    FinancialsReportsView,
                    "POL-FIN-REPORTS-VIEW",
                    permissionsAny: new[] { SystemPermissions.ReportsView, SystemPermissions.FinancialsAccountsView, SystemPermissions.FinancialsJournalView },
                    modulesAny: new[] { SystemModules.Financials, SystemModules.Reports },
                    description: "View financial statements (Trial Balance, Balance Sheet, Income Statement, Cash Flow)."),
                Functional(
                    ReportsView,
                    "POL-REPORTS-VIEW",
                    permissionsAll: new[] { SystemPermissions.ReportsView },
                    modulesAny: new[] { SystemModules.Financials, SystemModules.Reports },
                    description: "View financial and operational reports."),

                // --- Banking ---
                Functional(
                    BankingAccountsView,
                    "POL-BNK-ACC-VIEW",
                    permissionsAll: new[] { SystemPermissions.BankingAccountsView },
                    modulesAny: new[] { SystemModules.Financials, SystemModules.Sales, SystemModules.Purchases },
                    description: "View bank accounts and balances."),
                Functional(
                    BankingAccountsPicker,
                    "POL-BNK-PICKER",
                    permissionsAny: new[] { SystemPermissions.BankingAccountsView, SystemPermissions.SalesReceiptsCreate, SystemPermissions.PurchasesPaymentsCreate },
                    modulesAny: new[] { SystemModules.Financials, SystemModules.Sales, SystemModules.Purchases },
                    description: "View bank accounts in receipt, payment, and transfer pickers."),
                Functional(
                    BankingAccountsManage,
                    "POL-BNK-ACC-MANAGE",
                    permissionsAll: new[] { SystemPermissions.BankingAccountsManage },
                    modulesAny: new[] { SystemModules.Financials, SystemModules.Sales, SystemModules.Purchases },
                    description: "Create, update, or archive bank accounts."),
                Functional(
                    BankingTransactionsCreate,
                    "POL-BNK-TX-CREATE",
                    permissionsAll: new[] { SystemPermissions.BankingTransactionsCreate },
                    modulesAny: new[] { SystemModules.Financials, SystemModules.Sales, SystemModules.Purchases },
                    description: "Record bank deposits, transfers, or statement expenses/income."),
                Functional(
                    BankingReconcile,
                    "POL-BNK-REC-PERFORM",
                    permissionsAll: new[] { SystemPermissions.BankingReconcile },
                    modulesAny: new[] { SystemModules.Financials },
                    description: "Perform bank reconciliation and statement matching."),
                Functional(
                    BankingReconcileUndo,
                    "POL-BNK-REC-UNDO",
                    permissionsAll: new[] { SystemPermissions.BankingReconcileUndo },
                    modulesAny: new[] { SystemModules.Financials },
                    description: "Undo or reopen bank reconciliation."),

                // --- Sales ---
                Functional(
                    SalesCustomersView,
                    "POL-SLS-CUST-VIEW",
                    permissionsAll: new[] { SystemPermissions.SalesCustomersView },
                    modulesAny: new[] { SystemModules.Sales },
                    description: "View customers, contacts, and customer balances."),
                Functional(
                    SalesCustomersManage,
                    "POL-SLS-CUST-MANAGE",
                    permissionsAll: new[] { SystemPermissions.SalesCustomersManage },
                    modulesAny: new[] { SystemModules.Sales },
                    description: "Create, update, archive, or delete customers."),
                Functional(
                    SalesInvoicesView,
                    "POL-SLS-INV-VIEW",
                    permissionsAll: new[] { SystemPermissions.SalesInvoicesView },
                    modulesAny: new[] { SystemModules.Sales },
                    description: "View sales invoices and render invoice PDFs."),
                Functional(
                    SalesInvoicesCreate,
                    "POL-SLS-INV-CREATE",
                    permissionsAll: new[] { SystemPermissions.SalesInvoicesCreate },
                    modulesAny: new[] { SystemModules.Sales },
                    disallowedStates: new[] { "Issued", "Open", "Paid", "Void" },
                    description: "Create or edit draft sales invoices."),
                Functional(
                    SalesInvoicesPost,
                    "POL-SLS-INV-POST",
                    permissionsAll: new[] { SystemPermissions.SalesInvoicesPost },
                    modulesAny: new[] { SystemModules.Sales },
                    description: "Post, issue, or void sales invoices."),
                Functional(
                    SalesInvoicesSendEmail,
                    "POL-SLS-INV-SEND-EMAIL",
                    permissionsAny: new[] { SystemPermissions.SalesInvoicesCreate, SystemPermissions.SalesInvoicesPost },
                    description: "Send or queue a sales invoice email."),
                Functional(
                    SalesReceiptsView,
                    "POL-SLS-RCPT-VIEW",
                    permissionsAll: new[] { SystemPermissions.SalesReceiptsView },
                    modulesAny: new[] { SystemModules.Sales },
                    description: "View customer receipts and allocations."),
                Functional(
                    SalesReceiptsCreate,
                    "POL-SLS-RCPT-CREATE",
                    permissionsAll: new[] { SystemPermissions.SalesReceiptsCreate },
                    modulesAny: new[] { SystemModules.Sales },
                    description: "Record customer receipts and allocations."),
                Functional(
                    SalesQuotationsView,
                    "POL-SLS-QUO-VIEW",
                    permissionsAll: new[] { SystemPermissions.SalesQuotationsView },
                    modulesAny: new[] { SystemModules.Sales },
                    description: "View sales quotations."),

                // --- Purchases ---
                Functional(
                    PurchasesVendorsView,
                    "POL-PUR-VEND-VIEW",
                    permissionsAll: new[] { SystemPermissions.PurchasesVendorsView },
                    modulesAny: new[] { SystemModules.Purchases },
                    description: "View vendors and vendor balances."),
                Functional(
                    PurchasesVendorsManage,
                    "POL-PUR-VEND-MANAGE",
                    permissionsAll: new[] { SystemPermissions.PurchasesVendorsManage },
                    modulesAny: new[] { SystemModules.Purchases },
                    description: "Create, update, or archive vendors."),
                Functional(
                    PurchasesBillsView,
                    "POL-PUR-BILL-VIEW",
                    permissionsAll: new[] { SystemPermissions.PurchasesBillsView },
                    modulesAny: new[] { SystemModules.Purchases },
                    description: "View purchase bills."),
                Functional(
                    PurchasesBillsCreate,
                    "POL-PUR-BILL-CREATE",
                    permissionsAll: new[] { SystemPermissions.PurchasesBillsCreate },
                    modulesAny: new[] { SystemModules.Purchases },
                    description: "Create or edit draft purchase bills."),
                Functional(
                    PurchasesBillsPost,
                    "POL-PUR-BILL-POST",
                    permissionsAll: new[] { SystemPermissions.PurchasesBillsPost },
                    modulesAny: new[] { SystemModules.Purchases },
                    description: "Approve, post, or void purchase bills."),
                Functional(
                    PurchasesPaymentsCreate,
                    "POL-PUR-PMT-CREATE",
                    permissionsAll: new[] { SystemPermissions.PurchasesPaymentsCreate },
                    modulesAny: new[] { SystemModules.Purchases },
                    description: "Record or void bill payments."),
                Functional(
                    PurchasesOrdersView,
                    "POL-PUR-ORD-VIEW",
                    permissionsAll: new[] { SystemPermissions.PurchasesOrdersView },
                    modulesAny: new[] { SystemModules.Purchases },
                    description: "View purchase orders and receipts."),

                // --- Inventory ---
                Functional(
                    InventoryItemsView,
                    "POL-INV-ITEM-VIEW",
                    permissionsAll: new[] { SystemPermissions.InventoryItemsView },
                    modulesAny: new[] { SystemModules.Inventory, SystemModules.Sales, SystemModules.Purchases },
                    description: "View catalog and inventory items."),
                Functional(
                    InventoryItemsManage,
                    "POL-INV-ITEM-MANAGE",
                    permissionsAll: new[] { SystemPermissions.InventoryItemsManage },
                    modulesAny: new[] { SystemModules.Inventory, SystemModules.Sales, SystemModules.Purchases },
                    description: "Create, update, archive, or delete catalog items."),
                Functional(
                    InventoryAdjustmentsView,
                    "POL-INV-ADJ-VIEW",
                    permissionsAll: new[] { SystemPermissions.InventoryAdjustmentsView },
                    modulesAny: new[] { SystemModules.Inventory },
                    description: "View inventory control journal and stock movements."),
                Functional(
                    InventoryAdjustmentsPost,
                    "POL-INV-ADJ-POST",
                    permissionsAll: new[] { SystemPermissions.InventoryAdjustmentsPost },
                    modulesAny: new[] { SystemModules.Inventory },
                    description: "Record inventory stock adjustments."),

                // --- Donations ---
                Functional(
                    DonationsView,
                    "POL-DON-VIEW",
                    permissionsAll: new[] { SystemPermissions.DonationsView },
                    modulesAny: new[] { SystemModules.Donations },
                    description: "View donors and donation invoices."),
                Functional(
                    DonationsManage,
                    "POL-DON-MANAGE",
                    permissionsAll: new[] { SystemPermissions.DonationsManage },
                    modulesAny: new[] { SystemModules.Donations },
                    description: "Create, update, or delete donors and donation invoices."),

                // --- Dashboard & Audit ---
                Functional(
                    DashboardSummaryView,
                    "POL-DASH-SUMMARY-VIEW",
                    permissionsAny: new[]
                    {
                        SystemPermissions.ReportsView,
                        SystemPermissions.FinancialsAccountsView,
                        SystemPermissions.FinancialsJournalView,
                        SystemPermissions.SalesInvoicesView,
                        SystemPermissions.PurchasesBillsView,
                        SystemPermissions.BankingAccountsView
                    },
                    description: "View domain-scoped dashboard summary metrics."),
                Functional(
                    AuditLogsView,
                    "POL-AUDIT-LOGS-VIEW",
                    permissionsAll: new[] { SystemPermissions.AuditLogsView },
                    description: "View workspace audit logs."),

                // --- Tax & Business Reference Admin ---
                Governance(
                    TaxSettingsView,
                    "POL-ADM-TAX-VIEW",
                    new[] { CompanyMemberRole.Owner, CompanyMemberRole.Admin, CompanyMemberRole.Member },
                    description: "View workspace tax rates, tax groups, and item tax groups."),
                Functional(
                    AdminTaxesManage,
                    "POL-ADM-TAX-MANAGE",
                    permissionsAll: new[] { SystemPermissions.AdminTaxesManage },
                    description: "Create, update, or delete tax rates, tax groups, and item tax groups."),
                Functional(
                    AdminMembersManage,
                    "POL-ADM-MEM-MANAGE",
                    permissionsAll: new[] { SystemPermissions.AdminMembersManage },
                    description: "Manage workspace members via functional permission grant."),
                Functional(
                    AdminRolesManage,
                    "POL-ADM-ROLES-MANAGE",
                    permissionsAll: new[] { SystemPermissions.AdminRolesManage },
                    description: "Manage workspace security role assignments via functional permission grant."),
                Functional(
                    AdminSubscriptionManage,
                    "POL-ADM-SUB-MANAGE",
                    permissionsAll: new[] { SystemPermissions.AdminSubscriptionManage },
                    description: "Manage workspace subscription and billing via functional permission grant."),

                // --- Workspace Governance (Strictly separate from functional permissions) ---
                Governance(
                    WorkspaceCurrentView,
                    "POL-GOV-WS-VIEW",
                    new[] { CompanyMemberRole.Owner, CompanyMemberRole.Admin, CompanyMemberRole.Member },
                    description: "View current workspace metadata and caller's own resolved capabilities."),
                Governance(
                    WorkspaceMembersView,
                    "POL-GOV-MEM-VIEW",
                    new[] { CompanyMemberRole.Owner, CompanyMemberRole.Admin, CompanyMemberRole.Member },
                    description: "List workspace members and functional role catalog."),
                Governance(
                    WorkspaceSettingsUpdate,
                    "POL-GOV-WS-UPDATE",
                    new[] { CompanyMemberRole.Owner, CompanyMemberRole.Admin },
                    description: "Update workspace name, slug, or unposted initial currency."),
                Governance(
                    WorkspaceBusinessProfileUpdate,
                    "POL-GOV-BIZ-PROFILE",
                    new[] { CompanyMemberRole.Owner, CompanyMemberRole.Admin },
                    description: "Update workspace business contact and address profile."),
                Governance(
                    WorkspaceSalesInvoiceSettingsUpdate,
                    "POL-GOV-INV-SETTINGS",
                    new[] { CompanyMemberRole.Owner, CompanyMemberRole.Admin },
                    description: "Update workspace invoice display preferences."),
                new AccessOperationPolicy
                {
                    OperationKey = WorkspaceAccountingPolicyUpdate,
                    PolicyId = "POL-GOV-ACC-POLICY",
                    PolicyVersion = CurrentVersion,
                    Domain = AccessPolicyDomain.WorkspaceComposite,
                    AllowedGovernanceRoles = new[] { CompanyMemberRole.Owner, CompanyMemberRole.Admin },
                    RequiredPermissionsAll = new[] { SystemPermissions.FinancialsAccountsManage },
                    RequiresResolvedTenant = true,
                    Description = "Update workspace accounting framework, basis, fiscal year, or inventory costing policy."
                },
                Functional(
                    WorkspaceBooksLockUpdate,
                    "POL-FIN-BOOKS-LOCK",
                    permissionsAll: new[] { SystemPermissions.FinancialsPeriodsClose },
                    description: "Set or clear workspace books lock date."),
                Governance(
                    WorkspaceOrganizationDelete,
                    "POL-GOV-WS-DELETE",
                    new[] { CompanyMemberRole.Owner },
                    requiresStepUp: true,
                    description: "Soft-delete the current workspace (Owner + MFA step-up when enrolled)."),
                Governance(
                    WorkspaceOwnershipTransfer,
                    "POL-GOV-OWN-TRANSFER",
                    new[] { CompanyMemberRole.Owner },
                    requiresStepUp: true,
                    description: "Transfer workspace ownership to another active member (changes governance only; never alters functional roles)."),
                Governance(
                    WorkspaceMembersUpdateGovernanceRole,
                    "POL-GOV-MEM-ROLE",
                    new[] { CompanyMemberRole.Owner, CompanyMemberRole.Admin },
                    description: "Change a member's governance role within Owner/Admin limits."),
                new AccessOperationPolicy
                {
                    OperationKey = WorkspaceMembersAssignFunctionalRoles,
                    PolicyId = "POL-GOV-MEM-FUNC-ASSIGN",
                    PolicyVersion = CurrentVersion,
                    Domain = AccessPolicyDomain.WorkspaceGovernance,
                    AllowedGovernanceRoles = new[] { CompanyMemberRole.Owner, CompanyMemberRole.Admin },
                    RequiresResolvedTenant = true,
                    RequiresDelegationCheck = true,
                    Description = "Assign or revoke functional roles on a workspace member (subject to F5 anti-self-grant and delegation scope rules)."
                },
                Governance(
                    WorkspaceMembersRemove,
                    "POL-GOV-MEM-REMOVE",
                    new[] { CompanyMemberRole.Owner, CompanyMemberRole.Admin, CompanyMemberRole.Member },
                    description: "Remove a workspace member or self-leave (subject to target hierarchy and last-owner protection)."),
                Governance(
                    WorkspaceAccessRequestsCreate,
                    "POL-GOV-ACREQ-CREATE",
                    new[] { CompanyMemberRole.Owner, CompanyMemberRole.Admin, CompanyMemberRole.Member },
                    description: "Create an access request for functional roles for self or on-behalf."),
                Governance(
                    WorkspaceAccessRequestsView,
                    "POL-GOV-ACREQ-VIEW",
                    new[] { CompanyMemberRole.Owner, CompanyMemberRole.Admin, CompanyMemberRole.Member },
                    description: "View workspace access requests."),
                Governance(
                    WorkspaceAccessRequestsReview,
                    "POL-GOV-ACREQ-REVIEW",
                    new[] { CompanyMemberRole.Owner, CompanyMemberRole.Admin },
                    description: "Review (approve/reject) an access request for functional roles with independent verification."),
                Governance(
                    WorkspaceAccessRequestsCancel,
                    "POL-GOV-ACREQ-CANCEL",
                    new[] { CompanyMemberRole.Owner, CompanyMemberRole.Admin, CompanyMemberRole.Member },
                    description: "Cancel a pending access request."),
                Governance(
                    WorkspaceAccessSetup,
                    "POL-GOV-AC-SETUP",
                    new[] { CompanyMemberRole.Owner },
                    description: "Execute single-administrator initial access bootstrap setup."),
                Governance(
                    WorkspaceInvitationsList,
                    "POL-GOV-INV-LIST",
                    new[] { CompanyMemberRole.Owner, CompanyMemberRole.Admin },
                    description: "List pending workspace invitations."),
                new AccessOperationPolicy
                {
                    OperationKey = WorkspaceInvitationsCreate,
                    PolicyId = "POL-GOV-INV-CREATE",
                    PolicyVersion = CurrentVersion,
                    Domain = AccessPolicyDomain.WorkspaceGovernance,
                    AllowedGovernanceRoles = new[] { CompanyMemberRole.Owner, CompanyMemberRole.Admin },
                    RequiresResolvedTenant = true,
                    RequiresDelegationCheck = true,
                    Description = "Invite a user to the workspace with optional functional roles (subject to F5 delegation scope)."
                },
                Governance(
                    WorkspaceInvitationsResend,
                    "POL-GOV-INV-RESEND",
                    new[] { CompanyMemberRole.Owner, CompanyMemberRole.Admin },
                    description: "Resend a pending workspace invitation."),
                Governance(
                    WorkspaceInvitationsRevoke,
                    "POL-GOV-INV-REVOKE",
                    new[] { CompanyMemberRole.Owner, CompanyMemberRole.Admin },
                    description: "Revoke a pending workspace invitation."),
                new AccessOperationPolicy
                {
                    OperationKey = WorkspaceInvitationsAccept,
                    PolicyId = "POL-SELF-INV-ACCEPT",
                    PolicyVersion = CurrentVersion,
                    Domain = AccessPolicyDomain.SelfService,
                    RequiresResolvedTenant = false,
                    RequiresDelegationCheck = true,
                    Description = "Accept a workspace invitation for the authenticated user's verified email."
                },
                Governance(
                    WorkspaceSecurityPolicyView,
                    "POL-GOV-SEC-VIEW",
                    new[] { CompanyMemberRole.Owner, CompanyMemberRole.Admin, CompanyMemberRole.Member },
                    description: "View workspace MFA security policy."),
                Governance(
                    WorkspaceSecurityPolicyManage,
                    "POL-GOV-SEC-MANAGE",
                    new[] { CompanyMemberRole.Owner, CompanyMemberRole.Admin },
                    requiresStepUp: true,
                    description: "Update workspace MFA security policy or reset a member's MFA factors (Owner/Admin + MFA step-up)."),
                Governance(
                    WorkspaceSubscriptionManage,
                    "POL-GOV-SUB-MANAGE",
                    new[] { CompanyMemberRole.Owner },
                    description: "Manage workspace plan, subscription, or module opt-outs."),
                new AccessOperationPolicy
                {
                    OperationKey = WorkspaceEmailSettingsManage,
                    PolicyId = "POL-GOV-EMAIL-SETTINGS",
                    PolicyVersion = CurrentVersion,
                    Domain = AccessPolicyDomain.WorkspaceGovernance,
                    AllowedGovernanceRoles = new[] { CompanyMemberRole.Owner, CompanyMemberRole.Admin },
                    RequiredFeature = "custom_email",
                    RequiresResolvedTenant = true,
                    Description = "Configure or verify custom outbound email domain and sender settings (Owner/Admin + custom_email entitlement)."
                },
                new AccessOperationPolicy
                {
                    OperationKey = WorkspaceBrandingSettingsView,
                    PolicyId = "POL-GOV-BRAND-SET-VIEW",
                    PolicyVersion = CurrentVersion,
                    Domain = AccessPolicyDomain.WorkspaceGovernance,
                    AllowedGovernanceRoles = new[] { CompanyMemberRole.Owner, CompanyMemberRole.Admin },
                    RequiresResolvedTenant = true,
                    Description = "View workspace invoice appearance settings."
                },
                new AccessOperationPolicy
                {
                    OperationKey = WorkspaceBrandingSettingsManage,
                    PolicyId = "POL-GOV-BRAND-SET-MANAGE",
                    PolicyVersion = CurrentVersion,
                    Domain = AccessPolicyDomain.WorkspaceGovernance,
                    AllowedGovernanceRoles = new[] { CompanyMemberRole.Owner, CompanyMemberRole.Admin },
                    RequiredFeature = "white_label",
                    RequiresResolvedTenant = true,
                    Description = "Update workspace invoice appearance settings (Owner/Admin + white_label entitlement)."
                },
                new AccessOperationPolicy
                {
                    OperationKey = WorkspaceBrandingLogoView,
                    PolicyId = "POL-BRAND-LOGO-VIEW",
                    PolicyVersion = CurrentVersion,
                    Domain = AccessPolicyDomain.WorkspaceFunctional,
                    AllowedGovernanceRoles = new[] { CompanyMemberRole.Owner, CompanyMemberRole.Admin },
                    RequiredPermissionsAny = new[] { SystemPermissions.SalesInvoicesView },
                    RequiredModulesAny = new[] { SystemModules.Sales },
                    RequiresResolvedTenant = true,
                    Description = "Download or render workspace invoice logo (Owner/Admin or sales.invoices.view + sales module)."
                },
                new AccessOperationPolicy
                {
                    OperationKey = WorkspaceBrandingLogoManage,
                    PolicyId = "POL-GOV-BRAND-LOGO-MANAGE",
                    PolicyVersion = CurrentVersion,
                    Domain = AccessPolicyDomain.WorkspaceGovernance,
                    AllowedGovernanceRoles = new[] { CompanyMemberRole.Owner, CompanyMemberRole.Admin },
                    RequiredFeature = "white_label",
                    RequiresResolvedTenant = true,
                    Description = "Upload or delete workspace invoice logo (Owner/Admin + white_label entitlement)."
                },

                // --- Platform & Service Principal Policies ---
                new AccessOperationPolicy
                {
                    OperationKey = PlatformRolesManage,
                    PolicyId = "POL-PLAT-ROLES-MANAGE",
                    PolicyVersion = CurrentVersion,
                    Domain = AccessPolicyDomain.PlatformAdmin,
                    RequiresResolvedTenant = false,
                    Description = "Manage global platform security role templates (SystemAdministrators only)."
                },
                new AccessOperationPolicy
                {
                    OperationKey = PlatformAuditDefinitionsManage,
                    PolicyId = "POL-PLAT-AUDIT-DEFS",
                    PolicyVersion = CurrentVersion,
                    Domain = AccessPolicyDomain.PlatformAdmin,
                    RequiresResolvedTenant = false,
                    Description = "Mutate globally shared AuditableEntity and AuditableAttribute definitions (SystemAdministrators only)."
                },
                new AccessOperationPolicy
                {
                    OperationKey = PlatformSystemAdmin,
                    PolicyId = "POL-PLAT-SYS-ADMIN",
                    PolicyVersion = CurrentVersion,
                    Domain = AccessPolicyDomain.PlatformAdmin,
                    RequiresResolvedTenant = false,
                    Description = "Execute platform-level system administration actions (SystemAdministrators only)."
                },
                new AccessOperationPolicy
                {
                    OperationKey = ServiceEmailDomainReconcile,
                    PolicyId = "POL-SVC-EMAIL-RECONCILE",
                    PolicyVersion = CurrentVersion,
                    Domain = AccessPolicyDomain.ServicePrincipal,
                    AllowedServicePrincipalId = EmailReconciliationServicePrincipalId,
                    RequiredFeature = "custom_email",
                    RequiresResolvedTenant = true,
                    Description = "Execute background custom email domain DNS/provider reconciliation for an active entitled workspace."
                }
            };

            return list.Select(p => new KeyValuePair<string, AccessOperationPolicy>(p.OperationKey, p));
        }

        private static AccessOperationPolicy Functional(
            string operationKey,
            string policyId,
            IReadOnlyList<string>? permissionsAll = null,
            IReadOnlyList<string>? permissionsAny = null,
            IReadOnlyList<string>? modulesAny = null,
            string? requiredFeature = null,
            bool requiresMutableWorkflow = false,
            IReadOnlyList<string>? disallowedStates = null,
            ModuleAccess moduleAccess = ModuleAccess.Read,
            string description = "") => new()
        {
            OperationKey = operationKey,
            PolicyId = policyId,
            PolicyVersion = CurrentVersion,
            Domain = AccessPolicyDomain.WorkspaceFunctional,
            RequiredPermissionsAll = permissionsAll ?? Array.Empty<string>(),
            RequiredPermissionsAny = permissionsAny ?? Array.Empty<string>(),
            RequiredModulesAny = modulesAny ?? Array.Empty<string>(),
            ModuleAccess = requiresMutableWorkflow || operationKey.EndsWith(".manage", StringComparison.Ordinal) || operationKey.EndsWith(".create", StringComparison.Ordinal) || operationKey.EndsWith(".post", StringComparison.Ordinal) || operationKey.EndsWith(".pay", StringComparison.Ordinal) || operationKey.EndsWith(".void", StringComparison.Ordinal) || operationKey.EndsWith(".import", StringComparison.Ordinal) || operationKey.EndsWith(".perform", StringComparison.Ordinal) || operationKey.EndsWith(".allocate", StringComparison.Ordinal) || operationKey.EndsWith(".close", StringComparison.Ordinal) || operationKey.EndsWith(".send_email", StringComparison.Ordinal)
                ? ModuleAccess.Write
                : moduleAccess,
            RequiredFeature = requiredFeature,
            RequiresResolvedTenant = true,
            RequiresMutableWorkflowState = requiresMutableWorkflow,
            DisallowedWorkflowStates = disallowedStates ?? Array.Empty<string>(),
            Description = description
        };

        private static AccessOperationPolicy Governance(
            string operationKey,
            string policyId,
            IReadOnlyList<CompanyMemberRole> allowedRoles,
            string? requiredFeature = null,
            bool requiresStepUp = false,
            string description = "") => new()
        {
            OperationKey = operationKey,
            PolicyId = policyId,
            PolicyVersion = CurrentVersion,
            Domain = AccessPolicyDomain.WorkspaceGovernance,
            AllowedGovernanceRoles = allowedRoles,
            RequiredFeature = requiredFeature,
            RequiresResolvedTenant = true,
            RequiresMfaStepUp = requiresStepUp,
            Description = description
        };
    }
}
