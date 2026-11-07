using System.Collections.Concurrent;
using System.Data.Common;
using System.Text.Json;
using Api.Auditing;
using Core.Domain;
using Core.Domain.Auditing;
using Core.Domain.Financials;
using Core.Domain.Purchases;
using Core.Domain.Recurring;
using Core.Domain.Sales;
using Core.Domain.TaxSystem;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace Api.Data;

/// <summary>Writes allow-listed record changes atomically with their business save.</summary>
public sealed class RecordChangeInterceptor : SaveChangesInterceptor
{
    private sealed record CapturedChange(EntityEntry Entry, int CompanyId, string EntityType,
        string Action, Dictionary<string, object?> Values, string KeyName,
        string? ParentEntityType, int? ParentEntityId);
    private sealed class SaveState
    {
        public List<CapturedChange> Changes { get; } = new();
        public IDbContextTransaction? OwnedTransaction { get; set; }
    }

    public static IReadOnlyDictionary<Type, string> AllowList { get; } = new Dictionary<Type, string>
    {
        [typeof(Account)] = "Account", [typeof(Bank)] = "Bank",
        [typeof(BankTransaction)] = "BankTransaction", [typeof(BankReconciliation)] = "BankReconciliation",
        [typeof(BankStatementDescriptionRule)] = "BankStatementDescriptionRule",
        [typeof(JournalEntryHeader)] = "JournalEntryHeader", [typeof(JournalEntryLine)] = "JournalEntryLine",
        [typeof(SalesInvoiceHeader)] = "SalesInvoiceHeader", [typeof(SalesInvoiceLine)] = "SalesInvoiceLine",
        [typeof(PurchaseInvoiceHeader)] = "PurchaseInvoiceHeader", [typeof(PurchaseInvoiceLine)] = "PurchaseInvoiceLine",
        [typeof(SalesReceiptHeader)] = "SalesReceiptHeader", [typeof(SalesReceiptLine)] = "SalesReceiptLine",
        [typeof(VendorPayment)] = "VendorPayment", [typeof(Customer)] = "Customer", [typeof(Vendor)] = "Vendor",
        [typeof(Party)] = "Party", [typeof(Tax)] = "Tax", [typeof(TaxGroup)] = "TaxGroup",
        [typeof(TaxGroupTax)] = "TaxGroupTax", [typeof(Company)] = "Company", [typeof(CompanySetting)] = "CompanySetting",
        [typeof(CustomerAllocation)] = "CustomerAllocation", [typeof(MainContraAccount)] = "MainContraAccount",
        [typeof(PaymentTerm)] = "PaymentTerm",
        [typeof(GeneralLedgerSetting)] = "GeneralLedgerSetting", [typeof(WorkspaceOperatingSettings)] = "WorkspaceOperatingSettings",
        [typeof(BooksLockChange)] = "BooksLockChange", [typeof(FinancialYear)] = "FinancialYear",
        [typeof(CompanyMember)] = "CompanyMember", [typeof(CompanyMemberSecurityRole)] = "CompanyMemberSecurityRole",
        [typeof(RecurringSchedule)] = "RecurringSchedule", [typeof(RecurringScheduleLine)] = "RecurringScheduleLine"
    };

    /// <summary>Line/child types → (parent allow-list name, FK property). Used to fill ParentEntity* columns.</summary>
    public static IReadOnlyDictionary<Type, (string ParentEntityType, string ForeignKeyProperty)> ParentLinks { get; } =
        new Dictionary<Type, (string, string)>
        {
            [typeof(SalesInvoiceLine)] = ("SalesInvoiceHeader", nameof(SalesInvoiceLine.SalesInvoiceHeaderId)),
            [typeof(PurchaseInvoiceLine)] = ("PurchaseInvoiceHeader", nameof(PurchaseInvoiceLine.PurchaseInvoiceHeaderId)),
            [typeof(JournalEntryLine)] = ("JournalEntryHeader", nameof(JournalEntryLine.JournalEntryHeaderId)),
            [typeof(SalesReceiptLine)] = ("SalesReceiptHeader", nameof(SalesReceiptLine.SalesReceiptHeaderId)),
            [typeof(RecurringScheduleLine)] = ("RecurringSchedule", nameof(RecurringScheduleLine.RecurringScheduleId)),
            [typeof(TaxGroupTax)] = ("TaxGroup", nameof(TaxGroupTax.TaxGroupId)),
            [typeof(CompanyMemberSecurityRole)] = ("CompanyMember", nameof(CompanyMemberSecurityRole.CompanyMemberId))
        };

    private readonly IRequestAuditContext _auditContext;
    private readonly ConcurrentDictionary<DbContext, SaveState> _states = new();
    public RecordChangeInterceptor(IRequestAuditContext auditContext) => _auditContext = auditContext;

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context, false, CancellationToken.None).GetAwaiter().GetResult();
        return result;
    }
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        await Capture(eventData.Context, true, cancellationToken).ConfigureAwait(false);
        return result;
    }
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Persist(eventData.Context, false, CancellationToken.None).GetAwaiter().GetResult();
        return result;
    }
    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        await Persist(eventData.Context, true, cancellationToken).ConfigureAwait(false);
        return result;
    }
    public override void SaveChangesFailed(DbContextErrorEventData eventData) => Rollback(eventData.Context);
    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Rollback(eventData.Context);
        return Task.CompletedTask;
    }

    private async Task Capture(DbContext? context, bool async, CancellationToken cancellationToken)
    {
        if (context is null) return;
        var candidates = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(e => AllowList.ContainsKey(e.Metadata.ClrType)).ToList();
        if (candidates.Count == 0) return;
        var state = new SaveState();
        foreach (var entry in candidates)
        {
            var key = entry.Metadata.FindPrimaryKey();
            if (key is null || key.Properties.Count != 1 || (Nullable.GetUnderlyingType(key.Properties[0].ClrType) ?? key.Properties[0].ClrType) != typeof(int))
                throw new InvalidOperationException($"Audited type {entry.Metadata.ClrType.Name} must have a single integer key.");
            var companyId = await ResolveCompanyId(context, entry, async, cancellationToken).ConfigureAwait(false);
            var (parentType, parentId) = ResolveParent(entry);
            state.Changes.Add(new CapturedChange(entry, companyId, AllowList[entry.Metadata.ClrType],
                entry.State.ToString(), Snapshot(entry, entry.State), key.Properties[0].Name, parentType, parentId));
        }
        if (context.Database.CurrentTransaction is null)
            state.OwnedTransaction = async
                ? await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
                : context.Database.BeginTransaction();
        if (!_states.TryAdd(context, state))
        {
            if (state.OwnedTransaction is not null) await state.OwnedTransaction.DisposeAsync().ConfigureAwait(false);
            throw new InvalidOperationException("A record change audit save is already active for this context.");
        }
    }

    private static async Task<int> ResolveCompanyId(DbContext context, EntityEntry entry, bool async, CancellationToken cancellationToken)
    {
        if (entry.Entity is ICompanyOwned owned) return owned.CompanyId;
        if (entry.Entity is Company company) return company.Id;
        if (entry.Entity is CompanyMember member) return member.CompanyId;
        if (entry.Entity is CompanyMemberSecurityRole grant)
        {
            if (grant.CompanyMember is not null) return grant.CompanyMember.CompanyId;
            var tracked = context.ChangeTracker.Entries<CompanyMember>().FirstOrDefault(x => x.Entity.Id == grant.CompanyMemberId);
            if (tracked is not null) return tracked.Entity.CompanyId;
            var query = context.Set<CompanyMember>().IgnoreQueryFilters().Where(x => x.Id == grant.CompanyMemberId).Select(x => x.CompanyId);
            return async ? await query.SingleAsync(cancellationToken).ConfigureAwait(false) : query.Single();
        }
        throw new InvalidOperationException($"Audited type {entry.Metadata.ClrType.Name} has no workspace owner.");
    }

    /// <summary>Resolves parent document identity for a tracked line entry (Deleted uses original FK values).</summary>
    public static (string? ParentEntityType, int? ParentEntityId) ResolveParent(EntityEntry entry)
    {
        if (!ParentLinks.TryGetValue(entry.Metadata.ClrType, out var link))
            return (null, null);
        var property = entry.Property(link.ForeignKeyProperty);
        var raw = entry.State == EntityState.Deleted ? property.OriginalValue : property.CurrentValue;
        if (raw is null) return (link.ParentEntityType, null);
        return (link.ParentEntityType, Convert.ToInt32(raw));
    }

    private static Dictionary<string, object?> Snapshot(EntityEntry entry, EntityState state)
    {
        var values = new Dictionary<string, object?>();
        foreach (var property in entry.Properties)
        {
            if (property.Metadata.IsShadowProperty() || IsSecret(property.Metadata.Name, property.Metadata.ClrType)) continue;
            if (state == EntityState.Modified && !property.IsModified) continue;
            var oldValue = property.OriginalValue;
            var newValue = property.CurrentValue;
            if (state == EntityState.Modified && Equals(oldValue, newValue)) continue;
            values[property.Metadata.Name] = state == EntityState.Modified
                ? new object?[] { Mask(property.Metadata.Name, oldValue, entry.Entity is Bank), Mask(property.Metadata.Name, newValue, entry.Entity is Bank) }
                : Mask(property.Metadata.Name, state == EntityState.Deleted ? oldValue : newValue, entry.Entity is Bank);
        }
        if (state == EntityState.Modified && entry.Entity is BankReconciliation reconciliation)
        {
            values[nameof(BankReconciliation.BankId)] = reconciliation.BankId;
            values[nameof(BankReconciliation.StatementEndDate)] = reconciliation.StatementEndDate;
        }
        if (state == EntityState.Modified && entry.Entity is BankTransaction transaction)
            values[nameof(BankTransaction.BankId)] = transaction.BankId;
        return values;
    }

    private static bool IsSecret(string name, Type type)
    {
        if (type == typeof(byte[]) || type == typeof(Stream)) return true;
        if (name.EndsWith("Id", StringComparison.OrdinalIgnoreCase)) return false;
        foreach (var part in PrivateNameParts)
            if (name.Contains(part, StringComparison.OrdinalIgnoreCase) || type.Name.Contains(part, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
    private static readonly string[] PrivateNameParts = { "Password", "Hash", "Secret", "Token", "Stamp", "Seed", "Key" };
    private static readonly string[] MaskedNameParts = { "Iban", "RoutingNumber", "AccountNumber", "BankAccountNumber" };
    private static object? Mask(string name, object? value, bool isBank)
    {
        if (value is not string text || !(isBank && name.Equals("Number", StringComparison.OrdinalIgnoreCase)) && !MaskedNameParts
                .Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase))) return value;
        return "****" + text[^Math.Min(4, text.Length)..];
    }

    private async Task Persist(DbContext? context, bool async, CancellationToken cancellationToken)
    {
        if (context is null || !_states.TryGetValue(context, out var state)) return;
        try
        {
            if (context.Database.ProviderName == PostgresProviderName)
                await PersistPostgres(context, state, async, cancellationToken).ConfigureAwait(false);
            else
                await PersistValues(context, state, async, cancellationToken).ConfigureAwait(false);
            if (state.OwnedTransaction is not null)
            {
                if (async) await state.OwnedTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                else state.OwnedTransaction.Commit();
            }
        }
        catch
        {
            // Only roll back a transaction this interceptor opened; leave caller-owned transactions alone.
            if (state.OwnedTransaction is not null)
            {
                try { state.OwnedTransaction.Rollback(); }
                catch { /* caller will observe the original failure */ }
            }
            _states.TryRemove(context, out _);
            throw;
        }
        finally
        {
            _states.TryRemove(context, out _);
            if (state.OwnedTransaction is not null)
            {
                if (async) await state.OwnedTransaction.DisposeAsync().ConfigureAwait(false);
                else state.OwnedTransaction.Dispose();
            }
        }
    }

    /// <summary>
    /// Added rows get database-generated keys during the save, after capture. Their own key, foreign keys and
    /// parent id are read again here so the record holds the saved values instead of temporary ones.
    /// </summary>
    private static int? FinalizeAdded(CapturedChange change)
    {
        if (change.Action != EntityState.Added.ToString())
            return change.ParentEntityId;
        foreach (var property in change.Entry.Properties)
            if ((property.Metadata.IsPrimaryKey() || property.Metadata.IsForeignKey()) && change.Values.ContainsKey(property.Metadata.Name))
                change.Values[property.Metadata.Name] = property.CurrentValue;
        return ResolveParent(change.Entry).ParentEntityId;
    }

    private const string PostgresProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";

    /// <summary>
    /// Postgres: one fixed statement with an array per column, so the server plans it once instead of parsing a
    /// new multi-row VALUES statement on every save. WITH ORDINALITY keeps rows (and their ids) in capture order.
    /// </summary>
    private const string PostgresInsert =
        "INSERT INTO \"RecordChanges\" (\"CompanyId\", \"EntityType\", \"EntityId\", \"ParentEntityType\", \"ParentEntityId\", \"Action\", \"ChangesJson\", \"ActorUserId\", \"ActorKind\", \"CorrelationId\", \"IpAddress\", \"OccurredAt\") " +
        "SELECT r.company_id, r.entity_type, r.entity_id, r.parent_type, r.parent_id, r.action, r.changes, r.actor_user_id, r.actor_kind, r.correlation_id, r.ip_address, r.occurred_at " +
        "FROM unnest(@rc0::integer[], @rc1::text[], @rc2::integer[], @rc3::text[], @rc4::integer[], @rc5::text[], @rc6::text[], @rc7::text[], @rc8::text[], @rc9::text[], @rc10::text[], @rc11::timestamp[]) " +
        "WITH ORDINALITY AS r(company_id, entity_type, entity_id, parent_type, parent_id, action, changes, actor_user_id, actor_kind, correlation_id, ip_address, occurred_at, ordinal) " +
        "ORDER BY r.ordinal";

    private async Task PersistPostgres(DbContext context, SaveState state, bool async, CancellationToken cancellationToken)
    {
        var count = state.Changes.Count;
        var companyIds = new int[count];
        var entityTypes = new string[count];
        var entityIds = new int[count];
        var parentTypes = new string?[count];
        var parentIds = new int?[count];
        var actions = new string[count];
        var changes = new string[count];
        var actorUserIds = new string?[count];
        var actorKinds = new string[count];
        var correlationIds = new string[count];
        var ipAddresses = new string?[count];
        var occurredAt = new DateTime[count];
        for (var i = 0; i < count; i++)
        {
            var change = state.Changes[i];
            var parentId = FinalizeAdded(change);
            companyIds[i] = change.Entry.Entity is Company company ? company.Id : change.CompanyId;
            entityTypes[i] = change.EntityType;
            entityIds[i] = Convert.ToInt32(change.Entry.Property(change.KeyName).CurrentValue);
            parentTypes[i] = change.ParentEntityType;
            parentIds[i] = parentId;
            actions[i] = change.Action;
            changes[i] = JsonSerializer.Serialize(change.Values);
            actorUserIds[i] = _auditContext.ActorUserId;
            actorKinds[i] = _auditContext.ActorKind;
            correlationIds[i] = _auditContext.CorrelationId;
            ipAddresses[i] = _auditContext.IpAddress;
            occurredAt[i] = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        }
        static Npgsql.NpgsqlParameter Column(int index, object values, NpgsqlTypes.NpgsqlDbType type) =>
            new($"@rc{index}", NpgsqlTypes.NpgsqlDbType.Array | type) { Value = values };
        object[] parameters =
        {
            Column(0, companyIds, NpgsqlTypes.NpgsqlDbType.Integer), Column(1, entityTypes, NpgsqlTypes.NpgsqlDbType.Text),
            Column(2, entityIds, NpgsqlTypes.NpgsqlDbType.Integer), Column(3, parentTypes, NpgsqlTypes.NpgsqlDbType.Text),
            Column(4, parentIds, NpgsqlTypes.NpgsqlDbType.Integer), Column(5, actions, NpgsqlTypes.NpgsqlDbType.Text),
            Column(6, changes, NpgsqlTypes.NpgsqlDbType.Text), Column(7, actorUserIds, NpgsqlTypes.NpgsqlDbType.Text),
            Column(8, actorKinds, NpgsqlTypes.NpgsqlDbType.Text), Column(9, correlationIds, NpgsqlTypes.NpgsqlDbType.Text),
            Column(10, ipAddresses, NpgsqlTypes.NpgsqlDbType.Text), Column(11, occurredAt, NpgsqlTypes.NpgsqlDbType.Timestamp)
        };
        if (async) await context.Database.ExecuteSqlRawAsync(PostgresInsert, parameters, cancellationToken).ConfigureAwait(false);
        else context.Database.ExecuteSqlRaw(PostgresInsert, parameters);
    }

    private async Task PersistValues(DbContext context, SaveState state, bool async, CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        const string insert = "INSERT INTO \"RecordChanges\" (\"CompanyId\", \"EntityType\", \"EntityId\", \"ParentEntityType\", \"ParentEntityId\", \"Action\", \"ChangesJson\", \"ActorUserId\", \"ActorKind\", \"CorrelationId\", \"IpAddress\", \"OccurredAt\") VALUES ";
        foreach (var batch in state.Changes.Chunk(80))
        {
            using var parameterCommand = connection.CreateCommand();
            var parameters = new List<object>(batch.Length * 12);
            var rows = new List<string>(batch.Length);
            foreach (var change in batch)
            {
                var parentId = FinalizeAdded(change);
                var offset = parameters.Count;
                var rowValues = new object[]
                {
                    change.Entry.Entity is Company company ? company.Id : change.CompanyId,
                    change.EntityType,
                    Convert.ToInt32(change.Entry.Property(change.KeyName).CurrentValue),
                    (object?)change.ParentEntityType ?? DBNull.Value,
                    (object?)parentId ?? DBNull.Value,
                    change.Action,
                    JsonSerializer.Serialize(change.Values),
                    (object?)_auditContext.ActorUserId ?? DBNull.Value,
                    _auditContext.ActorKind,
                    _auditContext.CorrelationId,
                    (object?)_auditContext.IpAddress ?? DBNull.Value,
                    DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
                };
                var names = new string[rowValues.Length];
                for (var i = 0; i < rowValues.Length; i++)
                {
                    var name = $"@p{offset + i}";
                    names[i] = name;
                    parameters.Add(Parameter(parameterCommand, name, rowValues[i]));
                }
                rows.Add($"({string.Join(", ", names)})");
            }
            var sql = insert + string.Join(", ", rows);
            if (async) await context.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken).ConfigureAwait(false);
            else context.Database.ExecuteSqlRaw(sql, parameters);
        }
    }
    private static DbParameter Parameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        return parameter;
    }
    private void Rollback(DbContext? context)
    {
        if (context is null || !_states.TryRemove(context, out var state)) return;
        if (state.OwnedTransaction is null) return;
        try { state.OwnedTransaction.Rollback(); }
        finally { state.OwnedTransaction.Dispose(); }
    }
}
