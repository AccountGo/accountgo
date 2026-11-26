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
        public const string SalesCreditsView = "sales.credits.view";
        public const string SalesCreditsCreate = "sales.credits.create";
        public const string SalesCreditsPost = "sales.credits.post";
        public const string SalesCreditsReverse = "sales.credits.reverse";
        public const string SalesReturnsView = "sales.returns.view";
        public const string SalesReturnsPost = "sales.returns.post";
        public const string SalesReturnsReverse = "sales.returns.reverse";
        public const string SalesRefundsView = "sales.refunds.view";
        public const string SalesRefundsCreate = "sales.refunds.create";
        public const string SalesRefundsPost = "sales.refunds.post";
        public const string SalesRefundsReverse = "sales.refunds.reverse";
        public const string SalesWriteOffsView = "sales.write_offs.view";
        public const string SalesWriteOffsCreate = "sales.write_offs.create";
        public const string SalesWriteOffsPost = "sales.write_offs.post";
        public const string SalesWriteOffsReverse = "sales.write_offs.reverse";

        // Financials
        public const string FinancialsAccountsView = "financials.accounts.view";
        public const string FinancialsAccountsManage = "financials.accounts.manage";
        public const string FinancialsJournalView = "financials.journal.view";
        public const string FinancialsJournalCreate = "financials.journal.create";
        public const string FinancialsJournalPost = "financials.journal.post";
        public const string FinancialsPeriodsClose = "financials.periods.close";
        public const string FinancialsSettlementsApply = "financials.settlements.apply";
        public const string FinancialsSettlementsReverse = "financials.settlements.reverse";

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
        public const string PurchasesCreditsView = "purchases.credits.view";
        public const string PurchasesCreditsCreate = "purchases.credits.create";
        public const string PurchasesCreditsPost = "purchases.credits.post";
        public const string PurchasesCreditsReverse = "purchases.credits.reverse";
        public const string PurchasesReturnsView = "purchases.returns.view";
        public const string PurchasesReturnsPost = "purchases.returns.post";
        public const string PurchasesReturnsReverse = "purchases.returns.reverse";
        public const string PurchasesAdvancesView = "purchases.advances.view";
        public const string PurchasesAdvancesCreate = "purchases.advances.create";
        public const string PurchasesAdvancesPost = "purchases.advances.post";
        public const string PurchasesAdvancesReverse = "purchases.advances.reverse";
        public const string PurchasesRefundsView = "purchases.refunds.view";
        public const string PurchasesRefundsCreate = "purchases.refunds.create";
        public const string PurchasesRefundsPost = "purchases.refunds.post";
        public const string PurchasesRefundsReverse = "purchases.refunds.reverse";

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

        // Documents library (stored files and record attachments)
        public const string DocumentsView = "documents.view";
        public const string DocumentsManage = "documents.manage";

        // Audit & Admin
        public const string AuditLogsView = "audit.logs.view";
        public const string AdminMembersManage = "admin.members.manage";
        public const string AdminRolesManage = "admin.roles.manage";
        public const string AdminSubscriptionManage = "admin.subscription.manage";
        public const string AdminTaxesManage = "admin.taxes.manage";
    }
}
