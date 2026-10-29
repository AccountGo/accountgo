namespace Core.Domain.Security
{
    public static class SystemModules
    {
        public const string Sales = "sales";
        public const string Financials = "financials";
        public const string Purchases = "purchases";
        public const string Inventory = "inventory";
        public const string Reports = "reports";
        public const string Donations = "donations";
        public const string Audit = "audit";
    }

    public static class SystemPlans
    {
        public const string Invoicing = "invoicing";
        public const string Accounting = "accounting";
        public const string Enterprise = "enterprise";
    }

    public static class SystemPermissions
    {
        // Sales
        public const string SalesCustomersView = "sales.customers.view";
        public const string SalesCustomersManage = "sales.customers.manage";
        public const string SalesInvoicesView = "sales.invoices.view";
        public const string SalesInvoicesCreate = "sales.invoices.create";
        public const string SalesInvoicesPost = "sales.invoices.post";
        public const string SalesReceiptsView = "sales.receipts.view";
        public const string SalesReceiptsCreate = "sales.receipts.create";
        public const string SalesQuotationsView = "sales.quotations.view";

        // Financials
        public const string FinancialsAccountsView = "financials.accounts.view";
        public const string FinancialsAccountsManage = "financials.accounts.manage";
        public const string FinancialsJournalView = "financials.journal.view";
        public const string FinancialsJournalCreate = "financials.journal.create";
        public const string FinancialsJournalPost = "financials.journal.post";

        // Banking (#413)
        public const string BankingAccountsView = "banking.accounts.view";
        public const string BankingAccountsManage = "banking.accounts.manage";
        public const string BankingTransactionsCreate = "banking.transactions.create";
        public const string BankingReconcile = "banking.reconcile";
        public const string BankingReconcileUndo = "banking.reconcile.undo";

        // Purchases
        public const string PurchasesVendorsView = "purchases.vendors.view";
        public const string PurchasesVendorsManage = "purchases.vendors.manage";
        public const string PurchasesOrdersView = "purchases.orders.view";
        public const string PurchasesBillsView = "purchases.bills.view";
        public const string PurchasesBillsCreate = "purchases.bills.create";
        public const string PurchasesBillsPost = "purchases.bills.post";
        public const string PurchasesPaymentsCreate = "purchases.payments.create";

        // Inventory
        public const string InventoryItemsView = "inventory.items.view";
        public const string InventoryItemsManage = "inventory.items.manage";
        public const string InventoryAdjustmentsView = "inventory.adjustments.view";
        public const string InventoryAdjustmentsPost = "inventory.adjustments.post";

        // Reports
        public const string ReportsView = "reports.view";

        // Donations
        public const string DonationsView = "donations.view";
        public const string DonationsManage = "donations.manage";

        // Audit & Admin
        public const string AuditLogsView = "audit.logs.view";
        public const string AdminMembersManage = "admin.members.manage";
        public const string AdminRolesManage = "admin.roles.manage";
        public const string AdminSubscriptionManage = "admin.subscription.manage";
        public const string AdminTaxesManage = "admin.taxes.manage";
    }
}
