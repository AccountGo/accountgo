// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Linq;
using Api.Auditing;
using Api.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Purchases;
using Core.Domain.Recurring;
using Core.Domain.Sales;
using Core.Domain.TaxSystem;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AccountGo.BackendTests.Auditing;

/// <summary>PR4: line changes store ParentEntityType / ParentEntityId for header History.</summary>
public sealed class RecordChangeParentTests
{
    [Fact]
    public void ParentLinks_CoverEveryDocumentLineType()
    {
        var expected = new[]
        {
            typeof(SalesInvoiceLine),
            typeof(PurchaseInvoiceLine),
            typeof(JournalEntryLine),
            typeof(SalesReceiptLine),
            typeof(RecurringScheduleLine),
            typeof(TaxGroupTax),
            typeof(CompanyMemberSecurityRole)
        };
        Assert.Equal(expected.OrderBy(t => t.Name), RecordChangeInterceptor.ParentLinks.Keys.OrderBy(t => t.Name));
        Assert.All(RecordChangeInterceptor.ParentLinks.Keys, type =>
            Assert.True(RecordChangeInterceptor.AllowList.ContainsKey(type), type.Name));
    }

    [Theory]
    [InlineData(typeof(SalesInvoiceLine), "SalesInvoiceHeader", nameof(SalesInvoiceLine.SalesInvoiceHeaderId), 42)]
    [InlineData(typeof(PurchaseInvoiceLine), "PurchaseInvoiceHeader", nameof(PurchaseInvoiceLine.PurchaseInvoiceHeaderId), 11)]
    [InlineData(typeof(JournalEntryLine), "JournalEntryHeader", nameof(JournalEntryLine.JournalEntryHeaderId), 12)]
    [InlineData(typeof(SalesReceiptLine), "SalesReceiptHeader", nameof(SalesReceiptLine.SalesReceiptHeaderId), 13)]
    [InlineData(typeof(RecurringScheduleLine), "RecurringSchedule", nameof(RecurringScheduleLine.RecurringScheduleId), 14)]
    [InlineData(typeof(TaxGroupTax), "TaxGroup", nameof(TaxGroupTax.TaxGroupId), 15)]
    [InlineData(typeof(CompanyMemberSecurityRole), "CompanyMember", nameof(CompanyMemberSecurityRole.CompanyMemberId), 16)]
    public void ResolveParent_AddedModifiedDeleted_UsesForeignKey(Type lineType, string parentType, string fkProperty, int parentId)
    {
        using var connection = Open();
        using var db = CreateDb(connection);
        db.Database.EnsureCreated();

        var entity = Activator.CreateInstance(lineType)!;
        var entry = db.Entry(entity);
        entry.Property("Id").CurrentValue = 1;
        entry.Property(fkProperty).CurrentValue = parentId;

        entry.State = EntityState.Added;
        Assert.Equal((parentType, (int?)parentId), RecordChangeInterceptor.ResolveParent(entry));

        entry.State = EntityState.Modified;
        Assert.Equal((parentType, (int?)parentId), RecordChangeInterceptor.ResolveParent(entry));

        // Deleted reads the original FK so History still links after the line is removed.
        entry.State = EntityState.Unchanged;
        entry.Property(fkProperty).OriginalValue = parentId;
        entry.Property(fkProperty).CurrentValue = parentId;
        entry.State = EntityState.Deleted;
        Assert.Equal((parentType, (int?)parentId), RecordChangeInterceptor.ResolveParent(entry));
    }

    [Fact]
    public void ResolveParent_TopLevelEntity_ReturnsNull()
    {
        using var connection = Open();
        using var db = CreateDb(connection);
        db.Database.EnsureCreated();

        var entry = db.Entry(new Company { Name = "Solo", ShortName = "solo" });
        entry.State = EntityState.Added;
        Assert.Equal((null, null), RecordChangeInterceptor.ResolveParent(entry));
    }

    [Fact]
    public void Persist_WritesParentColumns_ForSalesInvoiceLine()
    {
        using var connection = Open();
        using var db = CreateDb(connection);
        db.Database.EnsureCreated();
        // Keep FK enforcement off so the line can persist without a full invoice graph.
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys=OFF";
            pragma.ExecuteNonQuery();
        }
        Api.Data.Schema.SqliteSchemaUpgrader.Upgrade(connection);
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys=OFF";
            pragma.ExecuteNonQuery();
        }

        var line = new SalesInvoiceLine
        {
            CompanyId = 1,
            SalesInvoiceHeaderId = 42,
            ItemId = 1,
            MeasurementId = 1,
            Quantity = 1,
            Amount = 10m
        };
        db.SalesInvoiceLines.Add(line);
        db.SaveChanges();

        var added = Assert.Single(db.RecordChanges.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.EntityType == "SalesInvoiceLine"));
        Assert.Equal("SalesInvoiceHeader", added.ParentEntityType);
        Assert.Equal(42, added.ParentEntityId);

        line.Amount = 20m;
        db.SaveChanges();
        var modified = db.RecordChanges.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.EntityType == "SalesInvoiceLine").OrderBy(r => r.Id).Last();
        Assert.Equal("Modified", modified.Action);
        Assert.Equal("SalesInvoiceHeader", modified.ParentEntityType);
        Assert.Equal(42, modified.ParentEntityId);

        db.SalesInvoiceLines.Remove(line);
        db.SaveChanges();
        var deleted = db.RecordChanges.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.EntityType == "SalesInvoiceLine").OrderBy(r => r.Id).Last();
        Assert.Equal("Deleted", deleted.Action);
        Assert.Equal("SalesInvoiceHeader", deleted.ParentEntityType);
        Assert.Equal(42, deleted.ParentEntityId);
    }

    private static SqliteConnection Open()
    {
        var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
        connection.Open();
        return connection;
    }

    private static ApiDbContext CreateDb(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<ApiDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new RecordChangeInterceptor(new FixedAuditContext()))
            .Options;
        return new ApiDbContext(options);
    }

    private sealed class FixedAuditContext : IRequestAuditContext
    {
        public string? ActorUserId => "actor-1";
        public string ActorKind => "user";
        public string CorrelationId => "trace-1";
        public string? IpAddress => "127.0.0.1";
    }
}
