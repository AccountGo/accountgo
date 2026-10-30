using Api.Data.Seed;
using Api.Service.D1Sync;
using Core.Domain;
using Core.Domain.Auditing;
using Core.Domain.Donations;
using Core.Domain.Financials;
using Core.Domain.Items;
using Core.Domain.Purchases;
using Core.Domain.Sales;
using Core.Domain.Security;
using Core.Domain.TaxSystem;
using Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Api.Data
{
    public class ApiDbContext : DbContext
    {
        private readonly ID1SyncContext? _d1SyncContext;
        private readonly ITenantContext? _tenantContext;
        private readonly AuditLogHelper _auditLogHelper = new();

        public int CurrentTenantCompanyId => _tenantContext?.CompanyId ?? -1;

        public ApiDbContext(
            DbContextOptions<ApiDbContext> options,
            ID1SyncContext? d1SyncContext = null,
            ITenantContext? tenantContext = null)
            : base(options)
        {
            _d1SyncContext = d1SyncContext;
            _tenantContext = tenantContext;
        }

        private void EnforceTenantSecurity()
        {
            if (_tenantContext == null)
                return;

            // Only pending writes need a tenant; unchanged tracked rows (e.g. lookups) must not force one.
            var entries = ChangeTracker.Entries<ICompanyOwned>()
                .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified || e.State == EntityState.Deleted)
                .ToList();
            if (entries.Count == 0)
                return;

            int currentCompanyId = _tenantContext.RequireCompanyId();

            foreach (var entry in entries)
            {
                if (entry.State == EntityState.Added)
                {
                    if (entry.Entity.CompanyId == 0)
                    {
                        entry.Entity.CompanyId = currentCompanyId;
                    }
                    else if (entry.Entity.CompanyId != currentCompanyId)
                    {
                        throw new CrossTenantWriteException(
                            $"Cannot add {entry.Metadata.ClrType.Name} with CompanyId {entry.Entity.CompanyId} while active tenant is {currentCompanyId}.");
                    }
                }
                else if (entry.State == EntityState.Modified)
                {
                    var prop = entry.Property(nameof(ICompanyOwned.CompanyId));
                    if (prop.IsModified && !Equals(prop.CurrentValue, prop.OriginalValue))
                    {
                        throw new CrossTenantWriteException(
                            $"Cannot modify CompanyId of existing {entry.Metadata.ClrType.Name}.");
                    }
                    if (entry.Entity.CompanyId != currentCompanyId)
                    {
                        throw new CrossTenantWriteException(
                            $"Cannot modify {entry.Metadata.ClrType.Name} with CompanyId {entry.Entity.CompanyId} while active tenant is {currentCompanyId}.");
                    }
                }
                else if (entry.State == EntityState.Deleted)
                {
                    if (entry.Entity.CompanyId != currentCompanyId)
                    {
                        throw new CrossTenantWriteException(
                            $"Cannot delete {entry.Metadata.ClrType.Name} with CompanyId {entry.Entity.CompanyId} while active tenant is {currentCompanyId}.");
                    }
                }
            }
        }

        /// <summary>
        /// D11: Bank.AccountId stays nullable in storage, but every write must set it.
        /// SQLite/D1 also enforce this with BEFORE INSERT/UPDATE triggers.
        /// </summary>
        private void EnforceBankAccountIdRequired()
        {
            foreach (var entry in ChangeTracker.Entries<Bank>())
            {
                if (entry.State != EntityState.Added && entry.State != EntityState.Modified)
                    continue;

                if (!entry.Entity.AccountId.HasValue)
                {
                    throw new System.InvalidOperationException(
                        "Bank.AccountId is required. Every bank account must be backed by a postable cash account.");
                }
            }
        }

        // Every SaveChanges/SaveChangesAsync overload funnels into these two.
        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            EnforceBankAccountIdRequired();
            EnforceReconciledLineImmutability();
            EnforceTenantSecurity();

            if (_d1SyncContext == null)
                return base.SaveChanges(acceptAllChangesOnSuccess);

            // Capture states before saving: EF resets them to Unchanged/Detached afterwards.
            var pending = D1SqlGenerator.Capture(ChangeTracker);
            var result = base.SaveChanges(acceptAllChangesOnSuccess);
            pending.FlushTo(_d1SyncContext);
            return result;
        }

        public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            EnforceBankAccountIdRequired();
            EnforceReconciledLineImmutability();
            EnforceTenantSecurity();

            if (_d1SyncContext == null)
                return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

            // Capture states before saving: EF resets them to Unchanged/Detached afterwards.
            var pending = D1SqlGenerator.Capture(ChangeTracker);
            var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            pending.FlushTo(_d1SyncContext);
            return result;
        }

        /// <summary>
        /// §5 backstop: reconciled GL lines cannot be deleted or edited (except clearing/setting BankReconciliationId).
        /// </summary>
        private void EnforceReconciledLineImmutability()
        {
            foreach (var entry in ChangeTracker.Entries<GeneralLedgerLine>())
            {
                if (entry.State == EntityState.Deleted)
                {
                    var originalReconId = entry.Property(e => e.BankReconciliationId).OriginalValue;
                    if (originalReconId.HasValue)
                        throw new System.InvalidOperationException(
                            "This ledger line is reconciled and cannot be deleted. Undo the reconciliation first.");
                    continue;
                }

                if (entry.State != EntityState.Modified)
                    continue;

                var stampedId = entry.Property(e => e.BankReconciliationId).OriginalValue;
                if (!stampedId.HasValue)
                    continue;

                foreach (var property in entry.Properties)
                {
                    if (!property.IsModified)
                        continue;
                    if (property.Metadata.Name == nameof(GeneralLedgerLine.BankReconciliationId))
                        continue;
                    throw new System.InvalidOperationException(
                        "This ledger line is reconciled and cannot be changed. Undo the reconciliation first.");
                }
            }

            foreach (var entry in ChangeTracker.Entries<GeneralLedgerHeader>())
            {
                if (entry.State != EntityState.Deleted)
                    continue;

                var headerId = entry.Entity.Id;
                var hasReconciled = GeneralLedgerLines.Local.Any(l =>
                        l.GeneralLedgerHeaderId == headerId && l.BankReconciliationId != null)
                    || GeneralLedgerLines.Any(l =>
                        l.GeneralLedgerHeaderId == headerId && l.BankReconciliationId != null);
                if (hasReconciled)
                    throw new System.InvalidOperationException(
                        "This ledger entry has reconciled lines and cannot be deleted. Undo the reconciliation first.");
            }
        }
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<Company>().HasQueryFilter(c => c.DeletedAt == null);
            builder.Entity<Company>().HasIndex(c => c.Slug).IsUnique();
            builder.Entity<CompanySetting>().Property(s => s.Currency).IsRequired().HasMaxLength(3).HasDefaultValue("PHP");
            builder.Entity<CompanySetting>().Property(s => s.AccountingBasis).HasDefaultValue(AccountingBasis.Accrual);
            builder.Entity<CompanySetting>().Property(s => s.ReportingFramework).HasDefaultValue(ReportingFramework.IfrsForSmes);
            builder.Entity<CompanySetting>().Property(s => s.TaxRegime).HasDefaultValue(TaxRegime.None);
            builder.Entity<CompanySetting>().Property(s => s.FiscalYearPattern).HasDefaultValue(FiscalYearPattern.Calendar);
            builder.Entity<CompanySetting>().Property(s => s.BooksStartDate)
                .HasColumnType("date")
                .HasDefaultValue(new System.DateTime(1970, 1, 1));
            builder.Entity<CompanySetting>().Property(s => s.IndustryTemplate).HasDefaultValue(IndustryTemplate.Services);
            builder.Entity<CompanySetting>().Property(s => s.InventoryCostingMethod).HasDefaultValue(InventoryCostingMethod.None);
            builder.Entity<CompanySetting>().Property(s => s.CoaTemplateId)
                .IsRequired()
                .HasMaxLength(100)
                .HasDefaultValue("legacy-default");
            builder.Entity<CompanySetting>().Property(s => s.MfaPolicy).HasDefaultValue(MfaPolicy.Optional);
            builder.Entity<CompanySetting>().Property(s => s.RequireMfaEveryLogin).HasDefaultValue(false);

            // Stored as BusinessAddress_* columns on CompanySetting; the SQLite upgrader and D1 0004 use the same names.
            builder.Entity<CompanySetting>().OwnsOne(s => s.BusinessAddress);
            builder.Entity<EntitlementUsage>(entity =>
            {
                entity.Property(e => e.Resource).IsRequired().HasMaxLength(50);
                entity.HasIndex(e => new { e.CompanyId, e.Resource, e.OccurredAt });
            });

            foreach (var entityType in builder.Model.GetEntityTypes())
            {
                if (typeof(ICompanyOwned).IsAssignableFrom(entityType.ClrType))
                {
                    var method = typeof(ApiDbContext)
                        .GetMethod(nameof(ConfigureTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)?
                        .MakeGenericMethod(entityType.ClrType);
                    method?.Invoke(this, new object[] { builder });
                }
            }

            builder.Entity<DonationInvoiceHeader>()
                .HasOne(donation => donation.Donor)
                .WithMany()
                .HasForeignKey(donation => donation.DonorId)
                .OnDelete(DeleteBehavior.NoAction);

            builder.Entity<DonationInvoiceHeader>()
                .HasOne(donation => donation.GlAccount)
                .WithMany()
                .HasForeignKey(donation => donation.GlAccountId)
                .OnDelete(DeleteBehavior.NoAction);

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
                .HasColumnType("decimal(18, 4)");

            builder.Entity<InventoryControlJournal>()
                .Property(p => p.OUTQty)
                .HasColumnType("decimal(18, 4)");

            builder.Entity<InventoryControlJournal>()
                .Property(p => p.UnitCost)
                .HasColumnType("decimal(18, 4)");

            builder.Entity<InventoryControlJournal>()
                .Property(p => p.TotalAmount)
                .HasColumnType("decimal(18, 2)");

            builder.Entity<InventoryControlJournal>()
                .Property(p => p.TotalCost)
                .HasColumnType("decimal(18, 2)");

            builder.Entity<Item>()
                .Property(p => p.QuantityOnHand)
                .HasColumnType("decimal(18, 4)")
                .HasDefaultValue(0m);

            builder.Entity<Item>()
                .Property(p => p.AverageCost)
                .HasColumnType("decimal(18, 4)")
                .HasDefaultValue(0m);

            builder.Entity<Item>()
                .Property(p => p.StockValue)
                .HasColumnType("decimal(18, 2)")
                .HasDefaultValue(0m);

            builder.Entity<InventoryAdjustment>()
                .Property(p => p.QuantityChange)
                .HasColumnType("decimal(18, 4)");

            builder.Entity<InventoryAdjustment>()
                .Property(p => p.UnitCost)
                .HasColumnType("decimal(18, 4)");

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

            builder.Entity<PurchaseInvoiceLine>()
                .Property(p => p.TaxAmount)
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

            builder.Entity<Vendor>()
                .Property(p => p.DiscountPercentage)
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

            // Invoice numbers are unique per workspace; drafts without a number (NULL) don't clash.
            builder.Entity<SalesInvoiceHeader>()
                .HasIndex(e => new { e.CompanyId, e.No })
                .IsUnique()
                .HasDatabaseName("IX_SalesInvoiceHeader_CompanyId_No");

            builder.Entity<SalesInvoiceHeader>()
                .HasIndex(e => new { e.CompanyId, e.Origin }, "IX_SalesInvoiceHeader_Company_Origin");

            builder.Entity<SalesInvoiceLine>()
                .Property(p => p.Amount)
                .HasColumnType("decimal(18, 2)");

            builder.Entity<SalesInvoiceLine>()
                .Property(p => p.Discount)
                .HasColumnType("decimal(18, 2)");

            builder.Entity<SalesInvoiceLine>()
                .Property(p => p.Quantity)
                .HasColumnType("decimal(18, 2)");

            builder.Entity<SalesInvoiceLine>()
                .Property(p => p.TaxAmount)
                .HasColumnType("decimal(18, 2)")
                .HasDefaultValue(0m);

            builder.Entity<SalesInvoiceLine>()
                .HasOne(p => p.Tax)
                .WithMany()
                .HasForeignKey(p => p.TaxId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<SalesInvoiceLine>()
                .HasOne(p => p.SalesAccount)
                .WithMany()
                .HasForeignKey(p => p.SalesAccountId)
                .OnDelete(DeleteBehavior.Restrict);

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

            builder.Entity<CompanyMember>(entity =>
            {
                entity.HasIndex(e => new { e.CompanyId, e.UserId }).IsUnique();
                entity.HasIndex(e => e.UserId);
                entity.HasOne(e => e.Company)
                    .WithMany()
                    .HasForeignKey(e => e.CompanyId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<CompanyMemberSecurityRole>(entity =>
            {
                entity.HasIndex(e => new { e.CompanyMemberId, e.SecurityRoleId }).IsUnique();
                entity.HasIndex(e => e.SecurityRoleId);
                entity.HasOne(e => e.CompanyMember)
                    .WithMany(m => m.SecurityRoles)
                    .HasForeignKey(e => e.CompanyMemberId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.SecurityRole)
                    .WithMany()
                    .HasForeignKey(e => e.SecurityRoleId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<CompanyInvitation>(entity =>
            {
                entity.HasIndex(e => e.TokenHash).IsUnique();
                entity.HasIndex(e => new { e.CompanyId, e.Email });
                entity.HasOne(e => e.Company)
                    .WithMany()
                    .HasForeignKey(e => e.CompanyId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<SalesInvoiceEmail>(entity =>
            {
                entity.HasIndex(e => new { e.CompanyId, e.SentAt });
                entity.HasIndex(e => e.SalesInvoiceHeaderId);
                entity.HasOne<SalesInvoiceHeader>()
                    .WithMany()
                    .HasForeignKey(e => e.SalesInvoiceHeaderId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<Account>()
                .HasIndex(e => new { e.CompanyId, e.AccountCode })
                .IsUnique();

            builder.Entity<SequenceNumber>()
                .HasIndex(e => new { e.CompanyId, e.SequenceNumberType })
                .IsUnique();

            builder.Entity<Customer>()
                .HasIndex(e => new { e.CompanyId, e.No })
                .IsUnique();

            builder.Entity<Customer>()
                .HasOne(e => e.BillToContact)
                .WithMany()
                .HasForeignKey(e => e.BillToContactId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<Vendor>()
                .HasIndex(e => new { e.CompanyId, e.No })
                .IsUnique();

            builder.Entity<Vendor>()
                .HasOne(e => e.BillToContact)
                .WithMany()
                .HasForeignKey(e => e.BillToContactId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<Item>()
                .HasIndex(e => new { e.CompanyId, e.No })
                .IsUnique();

            builder.Entity<Bank>(entity =>
            {
                entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
                entity.Property(e => e.CurrencyCode).HasMaxLength(3);
                entity.Property(e => e.OpeningBalance).HasColumnType("decimal(18, 2)");

                // Storage stays nullable (D11); domain + SaveChanges + triggers require a value.
                entity.Property(e => e.AccountId).IsRequired(false);

                // Named indexes: two filters on CompanyId must not collapse into one metadata entry.
                // SQL Server unique indexes on nullable columns get an IS NOT NULL filter from EF.
                entity.HasIndex(e => new { e.CompanyId, e.AccountId }, "UX_Bank_Company_Account")
                    .IsUnique()
                    .HasFilter("[AccountId] IS NOT NULL");

                entity.HasIndex(e => new { e.CompanyId, e.Name }, "UX_Bank_Company_Name")
                    .IsUnique();

                entity.HasIndex(e => e.CompanyId, "UX_Bank_DefaultReceipts")
                    .IsUnique()
                    .HasFilter("[IsDefaultForReceipts] = 1");

                entity.HasIndex(e => e.CompanyId, "UX_Bank_DefaultPayments")
                    .IsUnique()
                    .HasFilter("[IsDefaultForPayments] = 1");
            });

            builder.Entity<BankTransaction>(entity =>
            {
                entity.Property(e => e.No).IsRequired().HasMaxLength(50);
                entity.Property(e => e.ReferenceNo).HasMaxLength(100);
                entity.Property(e => e.Memo).HasMaxLength(500);
                entity.Property(e => e.Amount).HasColumnType("decimal(18, 2)");

                entity.HasIndex(e => e.CompanyId);
                entity.HasIndex(e => new { e.CompanyId, e.BankId, e.Date }, "IX_BankTransaction_Company_Bank_Date");
                entity.HasIndex(e => e.GeneralLedgerHeaderId);
                entity.HasIndex(e => new { e.CompanyId, e.No }, "UX_BankTransaction_Company_No").IsUnique();

                entity.HasOne(e => e.Bank).WithMany().HasForeignKey(e => e.BankId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.ToBank).WithMany().HasForeignKey(e => e.ToBankId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.ContraAccount).WithMany().HasForeignKey(e => e.ContraAccountId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.Party).WithMany().HasForeignKey(e => e.PartyId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.GeneralLedgerHeader).WithMany().HasForeignKey(e => e.GeneralLedgerHeaderId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.VoidGeneralLedgerHeader).WithMany().HasForeignKey(e => e.VoidGeneralLedgerHeaderId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<BankReconciliation>(entity =>
            {
                entity.Property(e => e.StatementEndingBalance).HasColumnType("decimal(18, 2)");
                entity.Property(e => e.BeginningBalance).HasColumnType("decimal(18, 2)");
                entity.Property(e => e.CreatedBy).HasMaxLength(100);
                entity.Property(e => e.FinishedBy).HasMaxLength(100);
                entity.Property(e => e.ReviewedBy).HasMaxLength(100);
                entity.Property(e => e.UndoneBy).HasMaxLength(100);
                entity.Property(e => e.UndoReason).HasMaxLength(500);

                entity.HasIndex(e => e.CompanyId);
                entity.HasIndex(e => new { e.CompanyId, e.BankId, e.StatementEndDate }, "IX_BankReconciliation_Company_Bank_EndDate");
                entity.HasIndex(e => new { e.CompanyId, e.BankId }, "UX_BankReconciliation_OneDraft")
                    .IsUnique()
                    .HasFilter("[Status] = 1");

                entity.HasOne(e => e.Bank).WithMany().HasForeignKey(e => e.BankId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<BankReconciliationItem>(entity =>
            {
                entity.Property(e => e.Amount).HasColumnType("decimal(18, 2)");

                entity.HasIndex(e => e.CompanyId);
                entity.HasIndex(e => e.BankReconciliationId);
                entity.HasIndex(e => new { e.BankReconciliationId, e.GeneralLedgerLineId }, "UX_BankReconciliationItem_Recon_Line")
                    .IsUnique();

                entity.HasOne(e => e.BankReconciliation).WithMany(r => r.Items)
                    .HasForeignKey(e => e.BankReconciliationId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.GeneralLedgerLine).WithMany()
                    .HasForeignKey(e => e.GeneralLedgerLineId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<BankStatementImport>(entity =>
            {
                entity.Property(e => e.FileName).HasMaxLength(260);
                entity.Property(e => e.ImportedBy).HasMaxLength(100);

                entity.HasIndex(e => e.CompanyId);
                entity.HasIndex(e => new { e.CompanyId, e.BankId }, "IX_BankStatementImport_Company_Bank");

                entity.HasOne(e => e.Bank).WithMany().HasForeignKey(e => e.BankId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<BankStatementLine>(entity =>
            {
                entity.Property(e => e.Amount).HasColumnType("decimal(18, 2)");
                entity.Property(e => e.ExternalId).HasMaxLength(200);
                entity.Property(e => e.Payee).HasMaxLength(200);
                entity.Property(e => e.Memo).HasMaxLength(500);
                entity.Property(e => e.Reference).HasMaxLength(100);
                entity.Property(e => e.ExcludedBy).HasMaxLength(100);
                entity.Property(e => e.ExclusionNote).HasMaxLength(500);

                entity.HasIndex(e => e.CompanyId);
                entity.HasIndex(e => e.BankStatementImportId);
                entity.HasIndex(e => e.MatchedGeneralLedgerLineId);
                entity.HasIndex(e => new { e.CompanyId, e.BankId, e.PostedDate }, "IX_BankStatementLine_Company_Bank_PostedDate");
                entity.HasIndex(e => new { e.CompanyId, e.BankId, e.ExcludedAt }, "IX_BankStatementLine_Company_Bank_ExcludedAt");
                entity.HasIndex(e => new { e.CompanyId, e.BankId, e.Source, e.ExternalId }, "UX_BankStatementLine_Company_Bank_Source_External")
                    .IsUnique()
                    .HasFilter("[ExternalId] IS NOT NULL");

                entity.HasOne(e => e.Bank).WithMany().HasForeignKey(e => e.BankId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.BankStatementImport).WithMany(i => i.Lines)
                    .HasForeignKey(e => e.BankStatementImportId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.MatchedGeneralLedgerLine).WithMany()
                    .HasForeignKey(e => e.MatchedGeneralLedgerLineId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<BankStatementDescriptionRule>(entity =>
            {
                entity.Property(e => e.DescriptionKey).HasMaxLength(200).IsRequired();
                entity.HasIndex(e => e.CompanyId);
                entity.HasIndex(e => new { e.CompanyId, e.DescriptionKey }, "UX_BankStatementDescriptionRule_Company_DescriptionKey")
                    .IsUnique();
                entity.HasOne(e => e.Vendor).WithMany()
                    .HasForeignKey(e => e.VendorId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.ExpenseAccount).WithMany()
                    .HasForeignKey(e => e.ExpenseAccountId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<GeneralLedgerLine>(entity =>
            {
                entity.HasIndex(e => new { e.CompanyId, e.BankReconciliationId }, "IX_GeneralLedgerLine_Company_BankReconciliationId");
                entity.HasOne(e => e.BankReconciliation).WithMany()
                    .HasForeignKey(e => e.BankReconciliationId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Seed();
            /* Medhat END */
        }

        private void ConfigureTenantFilter<TEntity>(ModelBuilder builder) where TEntity : class, ICompanyOwned
        {
            builder.Entity<TEntity>().HasQueryFilter(e => e.CompanyId == CurrentTenantCompanyId);
        }
        public virtual DbSet<Account> Accounts { get; set; }
        public virtual DbSet<AccountClass> AccountClasses { get; set; }
        public virtual DbSet<Address> Addresses { get; set; }
        public virtual DbSet<AuditLog> AuditLogs { get; set; }
        public virtual DbSet<AuditableAttribute> AuditableAttributes { get; set; }
        public virtual DbSet<AuditableEntity> AuditableEntities { get; set; }
        public virtual DbSet<Bank> Banks { get; set; }
        public virtual DbSet<BankTransaction> BankTransactions { get; set; }
        public virtual DbSet<BankReconciliation> BankReconciliations { get; set; }
        public virtual DbSet<BankReconciliationItem> BankReconciliationItems { get; set; }
        public virtual DbSet<BankStatementImport> BankStatementImports { get; set; }
        public virtual DbSet<BankStatementLine> BankStatementLines { get; set; }
        public virtual DbSet<BankStatementDescriptionRule> BankStatementDescriptionRules { get; set; }
        public virtual DbSet<Company> Companies { get; set; }
        public virtual DbSet<CompanyMember> CompanyMembers { get; set; }
        public virtual DbSet<CompanyMemberSecurityRole> CompanyMemberSecurityRoles { get; set; }
        public virtual DbSet<CompanyInvitation> CompanyInvitations { get; set; }
        public virtual DbSet<CompanySetting> CompanySettings { get; set; }
        public virtual DbSet<Contact> Contacts { get; set; }
        public virtual DbSet<Customer> Customers { get; set; }
        public virtual DbSet<CustomerAllocation> CustomerAllocations { get; set; }
        public virtual DbSet<Donor> Donors { get; set; }
        public virtual DbSet<EntitlementUsage> EntitlementUsages { get; set; }
        public virtual DbSet<FinancialYear> FinancialYears { get; set; }
        public virtual DbSet<GeneralLedgerHeader> GeneralLedgerHeaders { get; set; }
        public virtual DbSet<GeneralLedgerLine> GeneralLedgerLines { get; set; }
        public virtual DbSet<GeneralLedgerSetting> GeneralLedgerSettings { get; set; }
        public virtual DbSet<InventoryControlJournal> InventoryControlJournals { get; set; }
        public virtual DbSet<InventoryAdjustment> InventoryAdjustments { get; set; }
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
        public virtual DbSet<SalesInvoiceEmail> SalesInvoiceEmails { get; set; }
        public virtual DbSet<SalesInvoiceLine> SalesInvoiceLines { get; set; }
        public virtual DbSet<SalesOrderHeader> SalesOrderHeaders { get; set; }
        public virtual DbSet<SalesOrderLine> SalesOrderLines { get; set; }
        public virtual DbSet<SalesQuoteHeader> SalesQuoteHeaders { get; set; }
        public virtual DbSet<SalesQuoteLine> SalesQuoteLines { get; set; }
        public virtual DbSet<SalesReceiptHeader> SalesReceiptHeaders { get; set; }
        public virtual DbSet<SalesReceiptLine> SalesReceiptLines { get; set; }
        public virtual DbSet<DonationInvoiceHeader> DonationInvoiceHeaders { get; set; }
        public virtual DbSet<DonationInvoiceLine> DonationInvoiceLines { get; set; }
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

        #region Audit Logs
        private void SaveAuditLog()
        {
            string username = string.Empty;
            _auditLogHelper.ClearAddedEntities();

            var dbEntityEntries = ChangeTracker.Entries().ToList()
                .Where(p => p.State == EntityState.Modified || p.State == EntityState.Added || p.State == EntityState.Deleted);

            foreach (var dbEntityEntry in dbEntityEntries)
            {
                try
                {
                    username = ((BaseEntity)dbEntityEntry.Entity).ModifiedBy;
                    var auditLogs = _auditLogHelper.GetChangesForAuditLog(dbEntityEntry, username);
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
            foreach (var entity in _auditLogHelper.AddedEntities)
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

            _auditLogHelper.ClearAddedEntities();
        }
        #endregion
    }
}
