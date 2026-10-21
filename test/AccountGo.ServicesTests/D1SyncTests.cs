using System;
using System.Linq;
using System.Threading.Tasks;
using Api.Data;
using Api.Service.D1Sync;
using Core.Domain;
using Core.Domain.TaxSystem;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AccountGo.ServicesTests;

/// <summary>
/// Live write sync: every successful SaveChanges must emit SQL that reproduces the change in D1.
/// </summary>
public class D1SyncTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly D1SyncContext _sync = new();

    public D1SyncTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using var setup = NewApiContext(withSync: false);
        setup.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private ApiDbContext NewApiContext(bool withSync = true, SqliteConnection? connection = null)
    {
        var options = new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(connection ?? _connection).Options;
        return new ApiDbContext(options, withSync ? _sync : null);
    }

    [Fact]
    public async Task Insert_Emits_Upsert_With_Generated_Id_And_Enum_As_Integer()
    {
        using var ctx = NewApiContext();
        var member = new CompanyMember
        {
            CompanyId = 1,
            UserId = "user-1",
            Role = CompanyMemberRole.Owner,
            Status = CompanyMemberStatus.Active,
            JoinedAt = new DateTime(2026, 10, 21, 11, 43, 5, 123, DateTimeKind.Utc).AddTicks(4567)
        };
        ctx.CompanyMembers.Add(member);
        await ctx.SaveChangesAsync();

        Assert.True(member.Id > 0);
        var sql = Assert.Single(_sync.GetStatements());
        Assert.StartsWith("INSERT INTO \"CompanyMember\"", sql);
        Assert.Contains("ON CONFLICT (\"Id\") DO UPDATE SET", sql);
        Assert.Contains($"{member.Id}, ", sql);
        Assert.DoesNotContain("'Owner'", sql);
        Assert.DoesNotContain("'Active'", sql);
    }

    [Fact]
    public void Update_Emits_Upsert_With_New_Values_Sync_Overload()
    {
        using (var seed = NewApiContext(withSync: false))
        {
            seed.CompanyMembers.Add(new CompanyMember { CompanyId = 1, UserId = "user-2", Role = CompanyMemberRole.Member, JoinedAt = DateTime.UtcNow });
            seed.SaveChanges();
        }

        using var ctx = NewApiContext();
        var member = ctx.CompanyMembers.Single(m => m.UserId == "user-2");
        member.Role = CompanyMemberRole.Admin;
        ctx.SaveChanges();

        var sql = Assert.Single(_sync.GetStatements());
        Assert.StartsWith("INSERT INTO \"CompanyMember\"", sql);
        Assert.Contains("ON CONFLICT (\"Id\") DO UPDATE SET", sql);
        Assert.Contains("'user-2'", sql);
    }

    [Fact]
    public async Task Delete_Emits_Delete_By_Primary_Key()
    {
        int id;
        using (var seed = NewApiContext(withSync: false))
        {
            var m = new CompanyMember { CompanyId = 1, UserId = "user-3", Role = CompanyMemberRole.Member, JoinedAt = DateTime.UtcNow };
            seed.CompanyMembers.Add(m);
            seed.SaveChanges();
            id = m.Id;
        }

        using var ctx = NewApiContext();
        ctx.CompanyMembers.Remove(ctx.CompanyMembers.Single(m => m.Id == id));
        await ctx.SaveChangesAsync();

        var sql = Assert.Single(_sync.GetStatements());
        Assert.Equal($"DELETE FROM \"CompanyMember\" WHERE \"Id\" = {id};", sql);
    }

    [Fact]
    public async Task Failed_Save_Emits_Nothing()
    {
        using var ctx = NewApiContext();
        // Duplicate (CompanyId, UserId) violates the unique index.
        ctx.CompanyMembers.Add(new CompanyMember { CompanyId = 1, UserId = "dup", Role = CompanyMemberRole.Member, JoinedAt = DateTime.UtcNow });
        ctx.CompanyMembers.Add(new CompanyMember { CompanyId = 1, UserId = "dup", Role = CompanyMemberRole.Member, JoinedAt = DateTime.UtcNow });

        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        Assert.Empty(_sync.GetStatements());
    }

    [Fact]
    public async Task Emitted_Sql_Round_Trips_Exact_Values_Into_A_Replica()
    {
        var joinedAt = new DateTime(2026, 10, 21, 11, 43, 5, 123, DateTimeKind.Utc).AddTicks(4567);
        int memberId, taxId;
        using (var ctx = NewApiContext())
        {
            var member = new CompanyMember { CompanyId = 1, UserId = "rt-user", Role = CompanyMemberRole.Admin, JoinedAt = joinedAt, InvitedByUserId = "O'Brien {0}" };
            var tax = new Tax { CompanyId = 1, TaxName = "VAT reduced", TaxCode = "VR", Rate = 0.125m, IsActive = true };
            ctx.CompanyMembers.Add(member);
            ctx.Add(tax);
            await ctx.SaveChangesAsync();
            memberId = member.Id;
            taxId = tax.Id;
        }

        // Replica = what D1 holds: same schema, statements applied as the worker would.
        using var replicaConn = new SqliteConnection("DataSource=:memory:");
        replicaConn.Open();
        using (var replica = NewApiContext(withSync: false, connection: replicaConn))
        {
            replica.Database.EnsureCreated();
        }
        ApplyLikeD1(replicaConn, _sync.GetStatements());

        using var check = NewApiContext(withSync: false, connection: replicaConn);
        var m = check.CompanyMembers.Single(x => x.Id == memberId);
        Assert.Equal(CompanyMemberRole.Admin, m.Role);
        Assert.Equal(joinedAt.Ticks, m.JoinedAt.Ticks);
        Assert.Equal("O'Brien {0}", m.InvitedByUserId);
        var t = check.Set<Tax>().Single(x => x.Id == taxId);
        Assert.Equal(0.125m, t.Rate);
        Assert.True(t.IsActive);
    }

    /// <summary>Applies statements the way worker.ts does on D1: FKs enforced, deferred, one transaction.</summary>
    private static void ApplyLikeD1(SqliteConnection conn, System.Collections.Generic.IEnumerable<string> statements)
    {
        Exec(conn, "PRAGMA foreign_keys = ON;");
        using var tx = conn.BeginTransaction();
        Exec(conn, "PRAGMA defer_foreign_keys = ON;", tx);
        foreach (var stmt in statements) Exec(conn, stmt, tx);
        tx.Commit();
    }

    private static void Exec(SqliteConnection conn, string sql, SqliteTransaction? tx = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public async Task Updating_A_Parent_Does_Not_Cascade_Delete_Its_Children_In_Replica()
    {
        // Replica starts as a copy of the source (company 1 + one member).
        using var replicaConn = new SqliteConnection("DataSource=:memory:");
        replicaConn.Open();
        using (var replica = NewApiContext(withSync: false, connection: replicaConn))
        {
            replica.Database.EnsureCreated();
            replica.CompanyMembers.Add(new CompanyMember { CompanyId = 1, UserId = "child", Role = CompanyMemberRole.Member, JoinedAt = DateTime.UtcNow });
            replica.SaveChanges();
        }
        using (var seed = NewApiContext(withSync: false))
        {
            seed.CompanyMembers.Add(new CompanyMember { CompanyId = 1, UserId = "child", Role = CompanyMemberRole.Member, JoinedAt = DateTime.UtcNow });
            seed.SaveChanges();
        }

        // Update the parent (Company, which CompanyMember references with ON DELETE CASCADE).
        using (var ctx = NewApiContext())
        {
            var company = ctx.Companies.Single(c => c.Id == 1);
            company.Name = "Renamed Co";
            await ctx.SaveChangesAsync();
        }
        ApplyLikeD1(replicaConn, _sync.GetStatements());

        using var check = NewApiContext(withSync: false, connection: replicaConn);
        Assert.Equal("Renamed Co", check.Companies.Single(c => c.Id == 1).Name);
        Assert.Equal(1, check.CompanyMembers.Count(m => m.UserId == "child"));
    }

    [Fact]
    public void Child_Statement_Before_Parent_Statement_Applies_With_Deferred_Foreign_Keys()
    {
        using var replicaConn = new SqliteConnection("DataSource=:memory:");
        replicaConn.Open();
        using (var replica = NewApiContext(withSync: false, connection: replicaConn))
        {
            replica.Database.EnsureCreated();
        }

        Company company;
        CompanyMember member;
        using (var ctx = NewApiContext())
        {
            company = new Company { Name = "New Co", ShortName = "NEW", Slug = "new-co" };
            member = new CompanyMember { Company = company, UserId = "owner", Role = CompanyMemberRole.Owner, JoinedAt = DateTime.UtcNow };
            ctx.CompanyMembers.Add(member);
            ctx.SaveChanges();
        }

        // Force child-first order, as EF's change tracker may produce.
        var ordered = _sync.GetStatements().OrderBy(s => s.Contains("\"CompanyMember\"") ? 0 : 1).ToList();
        Assert.Contains("\"CompanyMember\"", ordered[0]);
        ApplyLikeD1(replicaConn, ordered);

        using var check = NewApiContext(withSync: false, connection: replicaConn);
        Assert.Equal(company.Id, check.CompanyMembers.Single(m => m.UserId == "owner").CompanyId);
    }

    [Fact]
    public async Task Identity_Context_Emits_Sync_Sql_For_User_Tokens()
    {
        var options = new DbContextOptionsBuilder<ApplicationIdentityDbContext>().UseSqlite(_connection).Options;
        using (var setup = new ApplicationIdentityDbContext(options))
        {
            setup.Database.ExecuteSqlRaw(@"CREATE TABLE IF NOT EXISTS ""AspNetUserTokens"" (""UserId"" TEXT NOT NULL, ""LoginProvider"" TEXT NOT NULL, ""Name"" TEXT NOT NULL, ""Value"" TEXT NULL, PRIMARY KEY (""UserId"", ""LoginProvider"", ""Name""));");
        }

        using var ctx = new ApplicationIdentityDbContext(options, _sync);
        ctx.Set<IdentityUserToken<string>>().Add(new IdentityUserToken<string> { UserId = "u1", LoginProvider = "EmailOtp", Name = "Default", Value = "{\"CodeHash\":\"x\"}" });
        await ctx.SaveChangesAsync();

        var sql = Assert.Single(_sync.GetStatements());
        Assert.StartsWith("INSERT INTO \"AspNetUserTokens\"", sql);
        Assert.Contains("'{\"CodeHash\":\"x\"}'", sql);
    }
}
