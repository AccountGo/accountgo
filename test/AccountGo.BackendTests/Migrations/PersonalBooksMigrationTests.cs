#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Api.Data;
using Core.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace AccountGo.BackendTests.Migrations;

public sealed class PersonalBooksMigrationTests
{
    [Fact]
    public async Task SqliteMigration_EnforcesOneOwnerAndCompany_AndHardDeleteReleasesRegistration()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(connection).Options);
        db.SuppressTenantSecurityCheck = true;
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("DROP TABLE WorkspaceLinks; DROP TABLE PersonalWorkspaces;");
        var migration = new Api.Data.Migrations.ApiDb.PersonalBooks { ActiveProvider = db.Database.ProviderName! };
        var commands = db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations);
        foreach (var command in commands) await db.Database.ExecuteSqlRawAsync(command.CommandText);
        var first = new Company { Name = "First", Slug = "first" };
        var second = new Company { Name = "Second", Slug = "second" };
        db.Companies.AddRange(first, second);
        await db.SaveChangesAsync();
        db.PersonalWorkspaces.Add(new PersonalWorkspace { OwnerUserId = "owner", CompanyId = first.Id, CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        db.PersonalWorkspaces.Add(new PersonalWorkspace { OwnerUserId = "owner", CompanyId = second.Id, CreatedAtUtc = DateTime.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.PersonalWorkspaces.Add(new PersonalWorkspace { OwnerUserId = "other", CompanyId = first.Id, CreatedAtUtc = DateTime.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.Companies.Remove(await db.Companies.SingleAsync(c => c.Id == first.Id));
        await db.SaveChangesAsync();
        Assert.Empty(await db.PersonalWorkspaces.ToListAsync());
        db.PersonalWorkspaces.Add(new PersonalWorkspace { OwnerUserId = "owner", CompanyId = second.Id, CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        Assert.Single(await db.PersonalWorkspaces.ToListAsync());
    }
}
