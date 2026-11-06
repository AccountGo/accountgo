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
        string Action, Dictionary<string, object?> Values, string KeyName);
    private sealed class SaveState
    {
        public List<CapturedChange> Changes { get; } = new();
        public IDbContextTransaction? OwnedTransaction { get; set; }
    }

    public static IReadOnlyDictionary<Type, string> AllowList { get; } = new Dictionary<Type, string>
    {
        [typeof(Account)] = "Account", [typeof(Bank)] = "Bank",
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
            state.Changes.Add(new CapturedChange(entry, companyId, AllowList[entry.Metadata.ClrType],
                entry.State.ToString(), Snapshot(entry, entry.State), key.Properties[0].Name));
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
        return values;
    }

    private static bool IsSecret(string name, Type type)
    {
        if (type == typeof(byte[]) || type == typeof(Stream)) return true;
        if (name.EndsWith("Id", StringComparison.OrdinalIgnoreCase)) return false;
        var privateNameParts = new[] { "Password", "Hash", "Secret", "Token", "Stamp", "Seed", "Key" };
        return privateNameParts.Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase))
            || privateNameParts.Any(part => type.Name.Contains(part, StringComparison.OrdinalIgnoreCase));
    }
    private static object? Mask(string name, object? value, bool isBank)
    {
        if (value is not string text || !(isBank && name.Equals("Number", StringComparison.OrdinalIgnoreCase)) && !new[] { "Iban", "RoutingNumber", "AccountNumber", "BankAccountNumber" }
                .Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase))) return value;
        return "****" + text[^Math.Min(4, text.Length)..];
    }

    private async Task Persist(DbContext? context, bool async, CancellationToken cancellationToken)
    {
        if (context is null || !_states.TryGetValue(context, out var state)) return;
        try
        {
            var connection = context.Database.GetDbConnection();
            const string insert = "INSERT INTO \"RecordChanges\" (\"CompanyId\", \"EntityType\", \"EntityId\", \"Action\", \"ChangesJson\", \"ActorUserId\", \"ActorKind\", \"CorrelationId\", \"IpAddress\", \"OccurredAt\") VALUES ";
            foreach (var batch in state.Changes.Chunk(80))
            {
                using var parameterCommand = connection.CreateCommand();
                var parameters = new List<object>(batch.Length * 10);
                var rows = new List<string>(batch.Length);
                foreach (var change in batch)
                {
                    if (change.Action == EntityState.Added.ToString())
                        change.Values[change.KeyName] = change.Entry.Property(change.KeyName).CurrentValue;
                    var offset = parameters.Count;
                    var rowValues = new object[]
                    {
                        change.Entry.Entity is Company company ? company.Id : change.CompanyId,
                        change.EntityType,
                        Convert.ToInt32(change.Entry.Property(change.KeyName).CurrentValue),
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
