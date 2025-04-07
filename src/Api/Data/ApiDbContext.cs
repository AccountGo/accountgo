using Api.Data.Seed;
using Core.Domain;
using Core.Domain.Auditing;
using Core.Domain.Financials;
using Core.Domain.Items;
using Core.Domain.Purchases;
using Core.Domain.Sales;
using Core.Domain.Security;
using Core.Domain.TaxSystem;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace Api.Data
{
    public class ApiDbContext : DbContext
    {
        public ApiDbContext(DbContextOptions<ApiDbContext> options)
            : base(options)
        {
        }
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<MainContraAccount>(entity =>
                {
                    entity.HasOne(e => e.MainAccount)
                    .WithOne()
                    .OnDelete(DeleteBehavior.NoAction);
                }
            );

            builder.Entity<CustomerAllocation>(b =>
            {
                b.HasOne("Core.Domain.Sales.Customer", "Customer")
                .WithMany("CustomerAllocations")
                .HasForeignKey("CustomerId")
                .OnDelete(DeleteBehavior.NoAction);

                b.HasOne("Core.Domain.Sales.SalesInvoiceHeader", "SalesInvoiceHeader")
                .WithMany("CustomerAllocations")
                .HasForeignKey("SalesInvoiceHeaderId")
                .OnDelete(DeleteBehavior.NoAction);

                b.HasOne("Core.Domain.Sales.SalesReceiptHeader", "SalesReceiptHeader")
                .WithMany("CustomerAllocations")
                .HasForeignKey("SalesReceiptHeaderId")
                .OnDelete(DeleteBehavior.NoAction);
            }
            );

            /* Medhat START */
            builder.Entity<GeneralLedgerLine>()
                .Property(p => p.Amount)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<JournalEntryLine>()
                .Property(p => p.Amount)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<InventoryControlJournal>()
                .Property(p => p.INQty)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<InventoryControlJournal>()
                .Property(p => p.OUTQty)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<InventoryControlJournal>()
                .Property(p => p.TotalAmount)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<InventoryControlJournal>()
                .Property(p => p.TotalCost)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<Item>()
                .Property(p => p.Cost)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<Item>()
                .Property(p => p.Price)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<PurchaseInvoiceLine>()
                .Property(p => p.Amount)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<PurchaseInvoiceLine>()
                .Property(p => p.Cost)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<PurchaseInvoiceLine>()
                .Property(p => p.Discount)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<JournalEntryLine>()
                .Property(p => p.Amount)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<PurchaseInvoiceLine>()
                .Property(p => p.Quantity)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<PurchaseInvoiceLine>()
                .Property(p => p.ReceivedQuantity)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<PurchaseOrderLine>()
                .Property(p => p.Amount)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<PurchaseOrderLine>()
                .Property(p => p.Quantity)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<PurchaseOrderLine>()
                .Property(p => p.Cost)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<PurchaseOrderLine>()
                .Property(p => p.Discount)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<PurchaseReceiptLine>()
                .Property(p => p.Amount)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<PurchaseReceiptLine>()
                .Property(p => p.Cost)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<PurchaseReceiptLine>()
                .Property(p => p.Discount)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<PurchaseReceiptLine>()
                .Property(p => p.Quantity)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<PurchaseReceiptLine>()
                .Property(p => p.ReceivedQuantity)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<VendorPayment>()
                .Property(p => p.Amount)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<CustomerAllocation>()
                .Property(p => p.Amount)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<SalesDeliveryLine>()
                .Property(p => p.Discount)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<SalesDeliveryLine>()
                .Property(p => p.Price)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<SalesDeliveryLine>()
                .Property(p => p.Quantity)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<PurchaseReceiptLine>()
                .Property(p => p.Quantity)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<SalesInvoiceHeader>()
                .Property(p => p.ShippingHandlingCharge)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<SalesInvoiceLine>()
                .Property(p => p.Amount)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<SalesInvoiceLine>()
                .Property(p => p.Discount)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<SalesInvoiceLine>()
                .Property(p => p.Quantity)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<SalesOrderLine>()
                .Property(p => p.Amount)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<SalesOrderLine>()
                .Property(p => p.Discount)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<SalesOrderLine>()
                .Property(p => p.Quantity)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<SalesQuoteLine>()
                .Property(p => p.Amount)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<SalesQuoteLine>()
                .Property(p => p.Discount)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<SalesQuoteLine>()
                .Property(p => p.Quantity)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<SalesReceiptHeader>()
                .Property(p => p.Amount)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<SalesReceiptLine>()
                .Property(p => p.Amount)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<SalesReceiptLine>()
                .Property(p => p.AmountPaid)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<SalesReceiptLine>()
                .Property(p => p.Discount)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<SalesReceiptLine>()
                .Property(p => p.Quantity)
                .HasColumnType("decimal(18, 2)"); 

            builder.Entity<Tax>()
                .Property(p => p.Rate)
                .HasColumnType("decimal(18, 2)");

            builder.Seed();
            /* Medhat END */

            
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
        public virtual DbSet<PurchaseOrderLine> PurchaseOrderLines { get; set; }
        public virtual DbSet<PurchaseOrderHeader> PurchaseOrderHeaders { get; set; }
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

public override int SaveChanges()
{
    // Enable audit logging
    SaveAuditLog();

    var ret = base.SaveChanges();

    // Update record IDs for newly added entities
    UpdateAuditLogRecordId();

    return ret;
}



        #region Audit Logs
        private void SaveAuditLog()
        {
            string username = string.Empty;

            var dbEntityEntries = ChangeTracker.Entries().ToList()
                .Where(p => p.State == EntityState.Modified || p.State == EntityState.Added || p.State == EntityState.Deleted);

            foreach (var dbEntityEntry in dbEntityEntries)
            {
                try
                {
                    username = ((BaseEntity)dbEntityEntry.Entity).ModifiedBy;
                    var auditLogs = AuditLogHelper.GetChangesForAuditLog(dbEntityEntry, username);
                    foreach (var auditlog in auditLogs)
                        if (auditlog != null)
                            AuditLogs.Add(auditlog);
                }
                catch
                {
                    continue;
                }
            }
        }

        private void UpdateAuditLogRecordId()
        {
            foreach (var entity in AuditLogHelper.addedEntities)
            {
                if (ChangeTracker.Entries().ToList().Contains(entity.Value))
                {
                    string keyName = entity.Value.Entity
                        .GetType()
                        .GetProperties()
                        .Single(p => p.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.KeyAttribute), false).Count() > 0).Name;

                    string? recid = entity.Value.Property(keyName).CurrentValue!.ToString();

                    var auditLog = this.AuditLogs.FirstOrDefault(log => log.AuditEventDateUTC == entity.Key);

                    if (auditLog != null)
                    {
                        auditLog.RecordId = recid;
                        base.SaveChanges();
                    }
                }
            }
        }
        #endregion
    }
}
