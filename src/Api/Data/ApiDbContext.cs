using Core.Domain;
using Core.Domain.Auditing;
using Core.Domain.Financials;
using Core.Domain.Items;
using Core.Domain.Purchases;
using Core.Domain.Sales;
using Core.Domain.Security;
using Core.Domain.TaxSystem;
using Microsoft.EntityFrameworkCore;

namespace Api.Data
{
    public class ApiDbContext : DbContext
    {
        public ApiDbContext()
        {

        }
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlServer(@"Server=tcp:accountgo.database.windows.net,1433;Database=accountgo;User ID=accountgo;Password=TBJX3BpxCsW782GLETPnFmeSRNoSN3sggjjppNgSPg4BUyaNq4i8K5TGwZfnnDRJ;Trusted_Connection=False;Encrypt=True;Connection Timeout=30;MultipleActiveResultSets=True;");
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
        }
        public virtual DbSet<Account> Accounts { get; set; }
        public virtual DbSet<AccountClass> AccountClasses { get; set; }
        public virtual DbSet<Address> Addresses { get; set; }
        public virtual DbSet<AuditLog> AuditLogs { get; set; }
        public virtual DbSet<AuditableAttribute> AuditableAttributes { get; set; }
        public virtual DbSet<AuditableEntity> AuditableEntities { get; set; }
        public virtual DbSet<Bank> Banks { get; set; }
        public virtual DbSet<Company> Companies { get; set; }
        public virtual DbSet<CompanySetting> CompanySettings { get; set; }
        public virtual DbSet<Contact> Contacts { get; set; }
        public virtual DbSet<Customer> Customers { get; set; }
        public virtual DbSet<CustomerAllocation> CustomerAllocations { get; set; }
        public virtual DbSet<FinancialYear> FinancialYears { get; set; }
        public virtual DbSet<GeneralLedgerHeader> GeneralLedgerHeaders { get; set; }
        public virtual DbSet<GeneralLedgerLine> GeneralLedgerLines { get; set; }
        public virtual DbSet<GeneralLedgerSetting> GeneralLedgerSettings { get; set; }
        public virtual DbSet<InventoryControlJournal> InventoryControlJournals { get; set; }
        public virtual DbSet<Item> Items { get; set; }
        public virtual DbSet<ItemCategory> ItemCategories { get; set; }
        public virtual DbSet<ItemTaxGroup> ItemTaxGroups { get; set; }
        public virtual DbSet<ItemTaxGroupTax> ItemTaxGroupTaxs { get; set; }
        public virtual DbSet<JournalEntryHeader> JournalEntryHeaders { get; set; }
        public virtual DbSet<JournalEntryLine> JournalEntryLines { get; set; }
        public virtual DbSet<Log> Logs { get; set; }
        public virtual DbSet<MainContraAccount> MainContraAccounts { get; set; }
        public virtual DbSet<Measurement> Measurements { get; set; }
        public virtual DbSet<PaymentTerm> PaymentTerms { get; set; }
        public virtual DbSet<PurchaseInvoiceHeader> PurchaseInvoiceHeaders { get; set; }
        public virtual DbSet<PurchaseInvoiceLine> PurchaseInvoiceLines { get; set; }
        public virtual DbSet<PurchaseOrderHeader> PurchaseOrderHeaders { get; set; }
        public virtual DbSet<PurchaseOrderLine> PurchaseOrderLines { get; set; }
        public virtual DbSet<PurchaseReceiptHeader> PurchaseReceiptHeaders { get; set; }
        public virtual DbSet<PurchaseReceiptLine> PurchaseReceiptLines { get; set; }
        public virtual DbSet<SalesDeliveryHeader> SalesDeliveryHeaders { get; set; }
        public virtual DbSet<SalesDeliveryLine> SalesDeliveryLines { get; set; }
        public virtual DbSet<SalesInvoiceHeader> SalesInvoiceHeaders { get; set; }
        public virtual DbSet<SalesInvoiceLine> SalesInvoiceLines { get; set; }
        public virtual DbSet<SalesOrderHeader> SalesOrderHeaders { get; set; }
        public virtual DbSet<SalesOrderLine> SalesOrderLines { get; set; }
        public virtual DbSet<SalesQuoteHeader> SalesQuoteHeaders { get; set; }
        public virtual DbSet<SalesQuoteLine> SalesQuoteLines { get; set; }
        public virtual DbSet<SalesReceiptHeader> SalesReceiptHeaders { get; set; }
        public virtual DbSet<SalesReceiptLine> SalesReceiptLines { get; set; }
        public virtual DbSet<SecurityGroup> SecurityGroups { get; set; }
        public virtual DbSet<SecurityPermission> SecurityPermissions { get; set; }
        public virtual DbSet<SecurityRole> SecurityRoles { get; set; }
        public virtual DbSet<SecurityRolePermission> SecurityRolePermissions { get; set; }
        public virtual DbSet<SecurityUserRole> SecurityUserRoles { get; set; }
        public virtual DbSet<SequenceNumber> SequenceNumbers { get; set; }
        public virtual DbSet<Tax> Taxes { get; set; }
        public virtual DbSet<TaxGroup> TaxGroups { get; set; }
        public virtual DbSet<TaxGroupTax> TaxGroupTaxes { get; set; }
        public virtual DbSet<User> Users { get; set; }
        public virtual DbSet<Vendor> Vendors { get; set; }
        public virtual DbSet<VendorPayment> VendorPayments { get; set; }
        public virtual DbSet<Party> Parties { get; set; }
    }
}
