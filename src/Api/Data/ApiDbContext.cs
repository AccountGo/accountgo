using Api.Data.Seed;
using Core.Domain;
using Core.Domain.Auditing;
using Core.Domain.Donations;
using Core.Domain.Financials;
using Core.Domain.Items;
using Core.Domain.Purchases;
using Core.Domain.Recurring;
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
        private readonly ITenantContext? _tenantContext;
        private readonly AuditLogHelper _auditLogHelper = new();

        public int CurrentTenantCompanyId => _tenantContext?.CompanyId ?? -1;
        public bool SuppressTenantSecurityCheck { get; set; }

        public ApiDbContext(DbContextOptions<ApiDbContext> options, ITenantContext? tenantContext = null)
            : this((DbContextOptions)options, tenantContext)
        {
        }

        // Provider subclasses (Postgres) that carry their own migrations.
        protected ApiDbContext(DbContextOptions options, ITenantContext? tenantContext)
            : base(options)
        {
            _tenantContext = tenantContext;
        }

        private void EnforceTenantSecurity()
        {
            if (_tenantContext == null || SuppressTenantSecurityCheck)
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
            CleanupDeletedPersonalBooks();
            EnforceAccessBootstrapLifecycle();
            EnforceAuditLogImmutability();
            EnforceBankAccountIdRequired();
            EnforceReconciledLineImmutability();
            EnforceTenantSecurity();
            EnforceRecurringScheduleConsistency();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            CleanupDeletedPersonalBooks();
            EnforceAccessBootstrapLifecycle();
            EnforceAuditLogImmutability();
            EnforceBankAccountIdRequired();
            EnforceReconciledLineImmutability();
            EnforceTenantSecurity();
            EnforceRecurringScheduleConsistency();
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        private void CleanupDeletedPersonalBooks()
        {
            var ids = ChangeTracker.Entries<Company>()
                .Where(e => e.State == EntityState.Deleted
                    || (e.State == EntityState.Modified && e.Entity.DeletedAt != null))
                .Select(e => e.Entity.Id).ToList();
            if (ids.Count == 0) return;
            WorkspaceLinks.RemoveRange(WorkspaceLinks.Where(l => ids.Contains(l.WorkCompanyId) || ids.Contains(l.PersonalCompanyId)));
            PersonalWorkspaces.RemoveRange(PersonalWorkspaces.Where(p => ids.Contains(p.CompanyId)));
        }

        private static readonly HashSet<string> ImmutableAuditTables = new(StringComparer.OrdinalIgnoreCase)
        {
            "__WorkspaceInitialization",
            "__OwnerAccessSetup",
            "__WorkspacePolicyTransition",
            "__JournalPostingAuthorization",
            "__InvitationTransition",
            "__WorkspaceOrigin",
            "__WorkspaceCollaborationEvidence",
            // Measurement and invariant-detection depend on these records staying intact.
            "__WorkspaceFirstUsefulAction",
            "__WorkspaceInvariantIncident",
            // Durable onboarding cohort anchors (start/completion) for conversion analysis.
            "__WorkspaceOnboardingCohort",
            // Signup-specific pending-continuation marker (slice 2); immutable like the others.
            "__WorkspaceSignupContinuation"
        };

        private void EnforceAuditLogImmutability()
        {
            var auditEntries = ChangeTracker.Entries<Core.Domain.Auditing.AuditLog>()
                .Where(e => e.State is EntityState.Modified or EntityState.Deleted)
                .ToList();

            foreach (var entry in auditEntries)
            {
                var originalTable = entry.Property(nameof(Core.Domain.Auditing.AuditLog.TableName)).OriginalValue as string;
                var currentTable = entry.Entity.TableName;

                var protectedTable = (originalTable != null && ImmutableAuditTables.Contains(originalTable))
                    ? originalTable
                    : (currentTable != null && ImmutableAuditTables.Contains(currentTable) ? currentTable : null);

                if (protectedTable != null)
                {
                    throw new InvalidOperationException(
                        $"Audit log records for '{protectedTable}' are append-only evidence and cannot be modified or deleted.");
                }
            }
        }

        // Close setup in the same write as the first functional grant or independent administrator.
        // Checking pending and persisted membership also covers removal, demotion and suspension.
        private void EnforceAccessBootstrapLifecycle()
        {
            var members = ChangeTracker.Entries<CompanyMember>()
                .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .ToList();
            var grants = ChangeTracker.Entries<CompanyMemberSecurityRole>()
                .Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList();
            if (members.Count == 0 && grants.Count == 0)
                return;

            var grantedMemberIds = grants.Select(g => g.CompanyMemberId).Where(id => id > 0).ToList();
            var grantCompanyIds = CompanyMembers.AsNoTracking()
                .Where(m => grantedMemberIds.Contains(m.Id)).Select(m => m.CompanyId).ToHashSet();
            grantCompanyIds.UnionWith(grants.Where(g => g.CompanyMember != null).Select(g => g.CompanyMember.CompanyId));
            var companyIds = members.Select(e => e.Entity.CompanyId).Concat(grantCompanyIds)
                .Where(id => id > 0).Distinct().ToList();
            foreach (var companyId in companyIds)
            {
                var company = Companies.SingleOrDefault(c => c.Id == companyId);

                var isCollaboratorChange = members.Any(e => e.Entity.CompanyId == companyId
                    && company?.CreatedByUserId != null
                    && e.Entity.UserId != company.CreatedByUserId);
                if (isCollaboratorChange && _tenantContext?.IsResolved == true && _tenantContext.CompanyId == companyId)
                {
                    var settings = WorkspaceOperatingSettings.IgnoreQueryFilters().SingleOrDefault(s => s.CompanyId == companyId);
                    if (settings != null)
                    {
                        settings.EverTeamActivatedAtUtc ??= DateTime.UtcNow;
                        if (settings.CollaborationState == WorkspaceCollaborationState.LegacyUnclassified)
                        {
                            settings.CollaborationState = WorkspaceCollaborationState.TeamActive;
                        }
                    }
                }

                if (company?.AccessBootstrapAvailable != true)
                    continue;

                var independentAdministrator = CompanyMembers.AsNoTracking().Any(m => m.CompanyId == companyId
                    && m.UserId != company.CreatedByUserId && m.Status == CompanyMemberStatus.Active
                    && (m.Role == CompanyMemberRole.Owner || m.Role == CompanyMemberRole.Admin))
                    || members.Any(e => e.Entity.CompanyId == companyId && e.State != EntityState.Deleted
                        && e.Entity.UserId != company.CreatedByUserId && e.Entity.Status == CompanyMemberStatus.Active
                        && (e.Entity.Role == CompanyMemberRole.Owner || e.Entity.Role == CompanyMemberRole.Admin));
                if (grantCompanyIds.Contains(companyId) || independentAdministrator)
                    company.AccessBootstrapAvailable = false;
            }
        }

        /// <summary>
        /// #479: RecurringSchedule rows and the ids they point at must all live in the same company.
        /// Lines and occurrences must match their parent schedule's CompanyId, and every FK
        /// (schedule party, line item/tax/account, occurrence invoice) must resolve to a row in
        /// that same company.
        ///
        /// This check <b>fails closed</b>: when an id is set but no row with that id exists in the
        /// expected company, it throws. A foreign key only proves the id exists somewhere; it does
        /// not prove tenant ownership. Lookups therefore use <see cref="EntityFrameworkExtensions.IgnoreQueryFilters"/>
        /// so a cross-company row is visible to this check (and then rejected) instead of being
        /// silently hidden by the tenant query filter.
        /// </summary>
        private void EnforceRecurringScheduleConsistency()
        {
            // Resolve a row's CompanyId by id, consulting the change tracker first (for rows added
            // in this unit of work that are not yet in the database) then the database with tenant
            // filters ignored. Returns null only when no such id exists anywhere.
            int? ResolveCompanyId<TEntity>(DbSet<TEntity> set, int id)
                where TEntity : BaseEntity, ICompanyOwned
            {
                var local = set.Local.FirstOrDefault(e => e.Id == id);
                if (local != null)
                    return local.CompanyId;

                return set.IgnoreQueryFilters()
                    .Where(e => e.Id == id)
                    .Select(e => (int?)e.CompanyId)
                    .FirstOrDefault();
            }

            void RequireSameCompany<TEntity>(DbSet<TEntity> set, int id, int expectedCompanyId, string label)
                where TEntity : BaseEntity, ICompanyOwned
            {
                var companyId = ResolveCompanyId(set, id);
                if (companyId == null)
                {
                    throw new CrossTenantWriteException(
                        $"{label} {id} does not exist or is not visible; cannot verify it belongs to company {expectedCompanyId}.");
                }
                if (companyId.Value != expectedCompanyId)
                {
                    throw new CrossTenantWriteException(
                        $"{label} {id} belongs to company {companyId.Value}, not {expectedCompanyId}.");
                }
            }

            foreach (var entry in ChangeTracker.Entries<RecurringSchedule>())
            {
                if (entry.State != EntityState.Added && entry.State != EntityState.Modified)
                    continue;

                var schedule = entry.Entity;

                // PartyId points at a Customer (sales) or Vendor (bill), both of which are company-owned.
                if (schedule.Kind == RecurringScheduleKind.SalesInvoice)
                {
                    RequireSameCompany(Customers, schedule.PartyId, schedule.CompanyId,
                        "RecurringSchedule Customer");
                }
                else
                {
                    RequireSameCompany(Vendors, schedule.PartyId, schedule.CompanyId,
                        "RecurringSchedule Vendor");
                }

                if (schedule.PaymentTermId.HasValue)
                {
                    RequireSameCompany(PaymentTerms, schedule.PaymentTermId.Value, schedule.CompanyId,
                        "RecurringSchedule PaymentTerm");
                }
            }

            foreach (var entry in ChangeTracker.Entries<RecurringScheduleLine>())
            {
                if (entry.State != EntityState.Added && entry.State != EntityState.Modified)
                    continue;

                var line = entry.Entity;

                var parentCompanyId = ResolveCompanyId(RecurringSchedules, line.RecurringScheduleId);
                if (parentCompanyId == null)
                {
                    throw new CrossTenantWriteException(
                        $"RecurringScheduleLine references schedule {line.RecurringScheduleId}, which does not exist.");
                }
                if (parentCompanyId.Value != line.CompanyId)
                {
                    throw new CrossTenantWriteException(
                        $"RecurringScheduleLine CompanyId ({line.CompanyId}) must match its RecurringSchedule CompanyId ({parentCompanyId.Value}).");
                }

                if (line.ItemId.HasValue)
                    RequireSameCompany(Items, line.ItemId.Value, line.CompanyId, "RecurringScheduleLine Item");

                if (line.TaxId.HasValue)
                    RequireSameCompany(Taxes, line.TaxId.Value, line.CompanyId, "RecurringScheduleLine Tax");

                if (line.AccountId.HasValue)
                    RequireSameCompany(Accounts, line.AccountId.Value, line.CompanyId, "RecurringScheduleLine Account");
            }

            foreach (var entry in ChangeTracker.Entries<RecurringOccurrence>())
            {
                if (entry.State != EntityState.Added && entry.State != EntityState.Modified)
                    continue;

                var occurrence = entry.Entity;

                var parentCompanyId = ResolveCompanyId(RecurringSchedules, occurrence.RecurringScheduleId);
                if (parentCompanyId == null)
                {
                    throw new CrossTenantWriteException(
                        $"RecurringOccurrence references schedule {occurrence.RecurringScheduleId}, which does not exist.");
                }
                if (parentCompanyId.Value != occurrence.CompanyId)
                {
                    throw new CrossTenantWriteException(
                        $"RecurringOccurrence CompanyId ({occurrence.CompanyId}) must match its RecurringSchedule CompanyId ({parentCompanyId.Value}).");
                }

                if (occurrence.SalesInvoiceId.HasValue)
                    RequireSameCompany(SalesInvoiceHeaders, occurrence.SalesInvoiceId.Value, occurrence.CompanyId,
                        "RecurringOccurrence SalesInvoice");

                if (occurrence.PurchaseInvoiceId.HasValue)
                    RequireSameCompany(PurchaseInvoiceHeaders, occurrence.PurchaseInvoiceId.Value, occurrence.CompanyId,
                        "RecurringOccurrence PurchaseInvoice");
            }
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
            builder.Entity<PersonalWorkspace>(entity =>
            {
                entity.ToTable("PersonalWorkspaces");
                entity.Property(p => p.OwnerUserId).IsRequired().HasMaxLength(450);
                entity.HasIndex(p => p.OwnerUserId).IsUnique();
                entity.HasIndex(p => p.CompanyId).IsUnique();
                entity.HasOne<Company>().WithMany().HasForeignKey(p => p.CompanyId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<WorkspaceLink>(entity =>
            {
                entity.ToTable("WorkspaceLinks");
                entity.Property(l => l.OwnerUserId).IsRequired().HasMaxLength(450);
                entity.HasIndex(l => l.OwnerUserId);
                entity.HasIndex(l => l.WorkCompanyId).IsUnique();
                entity.HasIndex(l => l.PersonalCompanyId);
                entity.HasOne<Company>().WithMany().HasForeignKey(l => l.WorkCompanyId).OnDelete(DeleteBehavior.NoAction);
                entity.HasOne<Company>().WithMany().HasForeignKey(l => l.PersonalCompanyId).OnDelete(DeleteBehavior.NoAction);
            });
            builder.Entity<LinkedEntryPair>(entity =>
            {
                entity.ToTable("LinkedEntryPairs");
                entity.Property(p => p.OwnerUserId).IsRequired().HasMaxLength(450);
                entity.Property(p => p.Kind).IsRequired().HasMaxLength(32);
                entity.Property(p => p.SourceCurrency).IsRequired().HasMaxLength(3);
                entity.Property(p => p.MirrorCurrency).IsRequired().HasMaxLength(3);
                entity.Property(p => p.State).IsRequired().HasMaxLength(32);
                entity.Property(p => p.IdempotencyKey).HasMaxLength(100);
                entity.Property(p => p.SourceAmount).HasColumnType("decimal(18, 2)");
                entity.Property(p => p.MirrorAmount).HasColumnType("decimal(18, 2)");
                entity.Property(p => p.ExchangeRate).HasColumnType("decimal(18, 8)");
                entity.HasIndex(p => new { p.SourceCompanyId, p.SourceJournalId }).IsUnique().HasFilter("[SourceJournalId] IS NOT NULL");
                entity.HasIndex(p => new { p.MirrorCompanyId, p.MirrorJournalId }).IsUnique().HasFilter("[MirrorJournalId] IS NOT NULL");
                entity.HasIndex(p => new { p.SourceCompanyId, p.IdempotencyKey }).IsUnique().HasFilter("[IdempotencyKey] IS NOT NULL");
                entity.HasIndex(p => p.StatementLineId).IsUnique().HasFilter("[StatementLineId] IS NOT NULL");
            });
            builder.Entity<Company>().HasQueryFilter(c => c.DeletedAt == null);
            builder.Entity<Company>().HasIndex(c => c.Slug).IsUnique();
            builder.Entity<Company>().Property(c => c.AccessBootstrapAvailable).HasDefaultValue(false);
            builder.Entity<Company>().Property(c => c.ActivationState)
                .IsRequired()
                .HasMaxLength(32)
                .HasDefaultValue(CompanyActivationStates.Active);
            builder.Entity<CompanySetting>().Property(s => s.Currency).IsRequired().HasMaxLength(3).HasDefaultValue("PHP");
            builder.Entity<CompanySetting>().Property(s => s.AccountingBasis).HasDefaultValue(AccountingBasis.Accrual);
            builder.Entity<CompanySetting>().Property(s => s.ReportingFramework).HasDefaultValue(ReportingFramework.IfrsForSmes);
            builder.Entity<CompanySetting>().Property(s => s.TaxRegime).HasDefaultValue(TaxRegime.None);
            builder.Entity<CompanySetting>().Property(s => s.FiscalYearPattern).HasDefaultValue(FiscalYearPattern.Calendar);
            builder.Entity<CompanySetting>().Property(s => s.BooksStartDate)
                .HasColumnType("date")
                .HasDefaultValue(new System.DateTime(1970, 1, 1));
            builder.Entity<CompanySetting>().Property(s => s.BooksLockedThrough)
                .HasColumnType("date");
            builder.Entity<CompanySetting>().Property(s => s.EntityType).HasDefaultValue(EntityType.Corporation);
            builder.Entity<CompanySetting>().Property(s => s.IndustryTemplate).HasDefaultValue(IndustryTemplate.Services);
            builder.Entity<BooksLockChange>(entity =>
            {
                entity.Property(e => e.PreviousLockedThrough).HasColumnType("date");
                entity.Property(e => e.NewLockedThrough).HasColumnType("date");
                entity.Property(e => e.Reason).HasMaxLength(500);
                entity.Property(e => e.ChangedBy).IsRequired().HasMaxLength(256);
                entity.HasIndex(e => e.CompanyId);
            });
            builder.Entity<CompanySetting>().Property(s => s.InventoryCostingMethod).HasDefaultValue(InventoryCostingMethod.None);
            builder.Entity<CompanySetting>().Property(s => s.CoaTemplateId)
                .IsRequired()
                .HasMaxLength(100)
                .HasDefaultValue("legacy-default");
            builder.Entity<CompanySetting>().Property(s => s.MfaPolicy).HasDefaultValue(MfaPolicy.Optional);
            builder.Entity<CompanySetting>().Property(s => s.RequireMfaEveryLogin).HasDefaultValue(false);
            builder.Entity<CompanySetting>().Property(s => s.Timezone).IsRequired().HasMaxLength(50).HasDefaultValue("UTC");

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
                entity.Property(e => e.GrantOrigin).HasDefaultValue(WorkspaceMemberGrantOrigin.LegacyExplicit);
                entity.Property(e => e.GrantedByUserId).HasMaxLength(450);
                entity.HasOne(e => e.CompanyMember)
                    .WithMany(m => m.SecurityRoles)
                    .HasForeignKey(e => e.CompanyMemberId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.SecurityRole)
                    .WithMany()
                    .HasForeignKey(e => e.SecurityRoleId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<CompanySetting>(entity =>
            {
                entity.Property(e => e.RegistrationStatus).HasDefaultValue(BusinessRegistrationStatus.NotProvided);
                entity.Property(e => e.RegistrationNumber).HasMaxLength(100);
            });

            builder.Entity<WorkspaceOperatingSettings>(entity =>
            {
                entity.ToTable("WorkspaceOperatingSettings");
                entity.HasIndex(e => e.CompanyId).IsUnique();
                entity.Property(e => e.InitializedByUserId).HasMaxLength(450);
                entity.Property(e => e.LastConfirmedByUserId).HasMaxLength(450);
                entity.Property(e => e.PolicyRevision).IsConcurrencyToken();
                entity.Property(e => e.PolicySchemaVersion).HasDefaultValue(WorkspaceOperatingPolicySchema.CurrentVersion);
                entity.HasOne(e => e.Company)
                    .WithMany()
                    .HasForeignKey(e => e.CompanyId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.ConfirmedOwnerMember)
                    .WithMany()
                    .HasForeignKey(e => e.ConfirmedOwnerMemberId)
                    .OnDelete(DeleteBehavior.NoAction);
            });

            builder.Entity<WorkspaceApprovalRule>(entity =>
            {
                entity.ToTable("WorkspaceApprovalRule");
                entity.HasIndex(e => new { e.CompanyId, e.OperationKey }).IsUnique();
                entity.Property(e => e.OperationKey).IsRequired().HasMaxLength(128);
                entity.Property(e => e.ConfiguredByUserId).HasMaxLength(450);
                entity.Property(e => e.PolicySchemaVersion).HasDefaultValue(WorkspaceOperatingPolicySchema.CurrentVersion);
                entity.HasOne(e => e.Company)
                    .WithMany()
                    .HasForeignKey(e => e.CompanyId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.ReviewerSecurityRole)
                    .WithMany()
                    .HasForeignKey(e => e.ReviewerSecurityRoleId)
                    .OnDelete(DeleteBehavior.NoAction);
            });

            builder.Entity<WorkspacePolicyCoordination>(entity =>
            {
                entity.ToTable("WorkspacePolicyCoordination");
                entity.HasIndex(e => e.CompanyId).IsUnique();
                entity.HasOne(e => e.Company)
                    .WithMany()
                    .HasForeignKey(e => e.CompanyId)
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

            builder.Entity<WorkspaceAccessRequest>(entity =>
            {
                entity.ToTable("WorkspaceAccessRequest");
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.CompanyId, e.RequestId }).IsUnique();
                entity.HasIndex(e => new { e.CompanyId, e.Status });
                entity.HasOne(e => e.Company)
                    .WithMany()
                    .HasForeignKey(e => e.CompanyId)
                    .OnDelete(DeleteBehavior.NoAction);
                entity.HasOne(e => e.TargetMember)
                    .WithMany()
                    .HasForeignKey(e => e.TargetMemberId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<SalesInvoiceEmail>(entity =>
            {
                entity.HasIndex(e => e.DispatchId).IsUnique().HasFilter("[DispatchId] IS NOT NULL");
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

            builder.Entity<BankStatementColumnMapping>(entity =>
            {
                entity.Property(e => e.HeaderSignature).HasMaxLength(1000).IsRequired();
                entity.Property(e => e.ColumnMapJson).IsRequired();
                entity.HasIndex(e => e.CompanyId);
                entity.HasIndex(e => e.BankId);
                entity.HasIndex(e => new { e.CompanyId, e.BankId, e.HeaderSignature }, "UX_BankStatementColumnMapping_Company_Bank_Header")
                    .IsUnique();
                entity.HasOne(e => e.Bank).WithMany()
                    .HasForeignKey(e => e.BankId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<GeneralLedgerLine>(entity =>
            {
                entity.HasIndex(e => new { e.CompanyId, e.BankReconciliationId }, "IX_GeneralLedgerLine_Company_BankReconciliationId");
                entity.HasOne(e => e.BankReconciliation).WithMany()
                    .HasForeignKey(e => e.BankReconciliationId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            #region Recurring Schedules (#479 Phase 1)

            builder.Entity<RecurringSchedule>(entity =>
            {
                entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Notes).HasMaxLength(2000);
                entity.Property(e => e.PaymentInstructions).HasMaxLength(2000);
                entity.Property(e => e.VendorInvoiceNumberPattern).HasMaxLength(100);
                entity.Property(e => e.AutomationEnabledBy).HasMaxLength(256);

                // Dates are stored as date-only
                entity.Property(e => e.StartDate).HasColumnType("date");
                entity.Property(e => e.EndDate).HasColumnType("date");
                entity.Property(e => e.NextRunDate).HasColumnType("date");

                // Indexes
                entity.HasIndex(e => e.CompanyId);
                entity.HasIndex(e => new { e.CompanyId, e.Status }, "IX_RecurringSchedule_Company_Status");
                entity.HasIndex(e => new { e.CompanyId, e.Kind, e.PartyId }, "IX_RecurringSchedule_Company_Kind_Party");
                entity.HasIndex(e => new { e.CompanyId, e.NextRunDate }, "IX_RecurringSchedule_Company_NextRunDate")
                    .HasFilter("[Status] = 1 AND [NextRunDate] IS NOT NULL"); // Only active schedules with next run date

                // Relationships
                entity.HasOne(e => e.Company).WithMany()
                    .HasForeignKey(e => e.CompanyId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<RecurringScheduleLine>(entity =>
            {
                entity.Property(e => e.Description).HasMaxLength(500);
                entity.Property(e => e.Quantity).HasColumnType("decimal(18, 4)").HasDefaultValue(1m);
                entity.Property(e => e.Amount).HasColumnType("decimal(18, 2)");
                entity.Property(e => e.Discount).HasColumnType("decimal(18, 4)").HasDefaultValue(0m);
                entity.Property(e => e.DiscountType).HasDefaultValue(RecurringDiscountType.Percentage);

                entity.HasIndex(e => e.CompanyId);
                entity.HasIndex(e => e.RecurringScheduleId);

                entity.HasOne(e => e.Schedule).WithMany(s => s.Lines)
                    .HasForeignKey(e => e.RecurringScheduleId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.Item).WithMany()
                    .HasForeignKey(e => e.ItemId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.Tax).WithMany()
                    .HasForeignKey(e => e.TaxId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.Account).WithMany()
                    .HasForeignKey(e => e.AccountId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<RecurringOccurrence>(entity =>
            {
                entity.Property(e => e.IdempotencyKey).IsRequired().HasMaxLength(100);
                entity.Property(e => e.ErrorCode).HasMaxLength(50);
                entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
                entity.Property(e => e.GuardFailureReason).HasMaxLength(500);
                entity.Property(e => e.ActionBy).HasMaxLength(256);
                entity.Property(e => e.SkipReason).HasMaxLength(500);
                entity.Property(e => e.LeasedBy).HasMaxLength(100);

                // Dates are stored as date-only
                entity.Property(e => e.ScheduledRunDate).HasColumnType("date");
                entity.Property(e => e.PeriodStart).HasColumnType("date");
                entity.Property(e => e.PeriodEnd).HasColumnType("date");
                entity.Property(e => e.DocumentDate).HasColumnType("date");
                entity.Property(e => e.DueDate).HasColumnType("date");
                // LeasedUntil uses timestamp with time zone on Postgres, datetime2 on SQL Server.
                // No explicit type needed here - let the provider choose.

                // Unique index: one occurrence per company + schedule + scheduled run date
                // This is the idempotency key - prevents duplicate runs for the same calendar date
                entity.HasIndex(e => new { e.CompanyId, e.RecurringScheduleId, e.ScheduledRunDate }, "UX_RecurringOccurrence_Company_Schedule_RunDate")
                    .IsUnique();

                // Index for finding due occurrences
                entity.HasIndex(e => new { e.CompanyId, e.Status, e.DocumentDate }, "IX_RecurringOccurrence_Company_Status_DocumentDate");

                // Index for lease expiration (worker uses this to find expired leases)
                entity.HasIndex(e => new { e.CompanyId, e.Status, e.LeasedUntil }, "IX_RecurringOccurrence_Company_Status_Lease");

                // Index for idempotency key lookups
                entity.HasIndex(e => e.IdempotencyKey);

                // Relationships - Restrict delete to preserve occurrence ledger when schedule is deleted
                entity.HasOne(e => e.Schedule).WithMany(s => s.Occurrences)
                    .HasForeignKey(e => e.RecurringScheduleId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.SalesInvoice).WithMany()
                    .HasForeignKey(e => e.SalesInvoiceId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.PurchaseInvoice).WithMany()
                    .HasForeignKey(e => e.PurchaseInvoiceId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            #endregion

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
        public virtual DbSet<BankStatementColumnMapping> BankStatementColumnMappings { get; set; }
        public virtual DbSet<BooksLockChange> BooksLockChanges { get; set; }
        public DbSet<PersonalWorkspace> PersonalWorkspaces { get; set; }
        public DbSet<WorkspaceLink> WorkspaceLinks { get; set; }
        public DbSet<LinkedEntryPair> LinkedEntryPairs { get; set; }
        public virtual DbSet<Company> Companies { get; set; }
        public virtual DbSet<CompanyMember> CompanyMembers { get; set; }
        public virtual DbSet<CompanyMemberSecurityRole> CompanyMemberSecurityRoles { get; set; }
        public virtual DbSet<CompanyInvitation> CompanyInvitations { get; set; }
        public virtual DbSet<WorkspaceAccessRequest> WorkspaceAccessRequests { get; set; }
        public virtual DbSet<WorkspaceOperatingSettings> WorkspaceOperatingSettings { get; set; }
        public virtual DbSet<WorkspaceApprovalRule> WorkspaceApprovalRules { get; set; }
        public virtual DbSet<WorkspacePolicyCoordination> WorkspacePolicyCoordinations { get; set; }
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
        public virtual DbSet<RecurringSchedule> RecurringSchedules { get; set; }
        public virtual DbSet<RecurringScheduleLine> RecurringScheduleLines { get; set; }
        public virtual DbSet<RecurringOccurrence> RecurringOccurrences { get; set; }

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
