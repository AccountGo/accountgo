// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.IO;
using System.Linq;
using Api.Data;
using Api.Tenancy;
using Core.Domain;
using Core.Domain.Financials;
using Dto.Banking;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Services.Banking;
using Xunit;

namespace AccountGo.BackendTests.Banking;

/// <summary>Statement import on real SQLite: LINQ translation, tenant filter, and the filtered unique index.</summary>
public class BankStatementImportServiceEfTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TenantContext _tenant;
    private readonly ApiDbContext _db;
    private readonly BankStatementImportService _service;
    private readonly int _companyId;
    private readonly int _bankId;
    private readonly int _bankAccountId;

    public BankStatementImportServiceEfTests()
    {
        _connection = new SqliteConnection($"Data Source=file:stmt-ef-{Guid.NewGuid():N}?mode=memory&cache=shared");
        _connection.Open();
        _tenant = new TenantContext();
        _db = new ApiDbContext(
            new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(_connection).Options,
            tenantContext: _tenant);
        _db.Database.EnsureCreated();

        _service = new BankStatementImportService(
            new EfRepository<BankStatementImport>(NullLogger<BankStatementImport>.Instance, _db),
            new EfRepository<BankStatementLine>(NullLogger<BankStatementLine>.Instance, _db),
            new EfRepository<BankStatementColumnMapping>(NullLogger<BankStatementColumnMapping>.Instance, _db),
            new EfRepository<Bank>(NullLogger<Bank>.Instance, _db),
            new EfRepository<GeneralLedgerLine>(NullLogger<GeneralLedgerLine>.Instance, _db),
            new EfRepository<GeneralLedgerHeader>(NullLogger<GeneralLedgerHeader>.Instance, _db),
            new EfRepository<CompanySetting>(NullLogger<CompanySetting>.Instance, _db),
            new Mock<IBankTransactionService>().Object,
            _tenant);

        _companyId = _db.Companies.Select(c => c.Id).Single();
        (_bankId, _bankAccountId) = SeedBank();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public void Commit_ListAndMatch_TranslateAgainstSqlite()
    {
        using (_tenant.Impersonate(_companyId))
        {
            var ledgerLineId = PostLedger(_bankAccountId, DrOrCrSide.Dr, 150m, new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc));

            var result = _service.Commit(_bankId, Csv("2026-10-06,150.00,Customer,,\n2026-10-07,-20.00,Shop,,\n"), "tester");

            Assert.Equal(2, result.Imported);
            Assert.Equal(1, result.AutoMatched);

            var all = _service.ListLines(_bankId, "all");
            Assert.Equal(2, all.Lines.Count);
            Assert.Equal(ledgerLineId, all.Lines.Single(l => l.Amount == 150m).MatchedGeneralLedgerLineId);
            Assert.Single(_service.ListLines(_bankId, "unmatched").Lines);

            var again = _service.Commit(_bankId, Csv("2026-10-06,150.00,Customer,,\n"), "tester");
            Assert.Equal(0, again.Imported);
            Assert.Equal(1, again.Duplicates);
        }
    }

    [Fact]
    public void TenantFilter_HidesOtherCompanyBank()
    {
        var otherCompanyId = _companyId + 100;
        using (_tenant.Impersonate(otherCompanyId))
        {
            Assert.Throws<Core.Tenancy.TenantEntityNotFoundException>(() => _service.ListLines(_bankId, "all"));
        }
    }

    [Fact]
    public void FilteredUniqueIndex_RejectsDuplicateExternalId_ButAllowsNulls()
    {
        using (_tenant.Impersonate(_companyId))
        {
            var import = new BankStatementImport
            {
                CompanyId = _companyId,
                BankId = _bankId,
                Source = BankStatementSources.Csv,
                ImportedAt = DateTime.UtcNow
            };
            _db.BankStatementImports.Add(import);
            _db.SaveChanges();

            BankStatementLine NewLine(string? externalId) => new()
            {
                CompanyId = _companyId,
                BankId = _bankId,
                BankStatementImportId = import.Id,
                Source = BankStatementSources.Csv,
                ExternalId = externalId,
                PostedDate = DateTime.UtcNow.Date,
                Amount = 1m,
                CreatedAt = DateTime.UtcNow
            };

            _db.BankStatementLines.AddRange(NewLine(null), NewLine(null));
            _db.SaveChanges();

            _db.BankStatementLines.Add(NewLine("X1"));
            _db.SaveChanges();
            _db.BankStatementLines.Add(NewLine("X1"));
            Assert.Throws<DbUpdateException>(() => _db.SaveChanges());
        }
    }

    [Fact]
    public void D1_0039_RunsUnderCompoundSelectLimit_AndEnforcesFilteredUniqueIndex()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        SQLitePCL.raw.sqlite3_limit(connection.Handle, SQLitePCL.raw.SQLITE_LIMIT_COMPOUND_SELECT, 1);
        Execute(connection, @"CREATE TABLE ""Bank"" (""Id"" INTEGER PRIMARY KEY, ""CompanyId"" INTEGER NOT NULL);");
        Execute(connection, @"CREATE TABLE ""GeneralLedgerLine"" (""Id"" INTEGER PRIMARY KEY, ""CompanyId"" INTEGER NOT NULL);");

        var script = File.ReadAllText(Path.Combine(RepoRoot(), "src/Api/d1/migrations/0039_bank_statement_lines.sql"));
        foreach (var line in script.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("--", StringComparison.Ordinal))
                continue;
            Assert.EndsWith(";", trimmed);
            Execute(connection, trimmed);
        }

        Execute(connection, @"INSERT INTO ""Bank"" (""Id"", ""CompanyId"") VALUES (1, 1);");
        Execute(connection, @"INSERT INTO ""BankStatementImport"" (""CompanyId"", ""BankId"", ""Source"", ""ImportedAt"") VALUES (1, 1, 1, '2026-10-30');");
        const string insert = @"INSERT INTO ""BankStatementLine"" (""CompanyId"", ""BankId"", ""BankStatementImportId"", ""Source"", ""ExternalId"", ""PostedDate"", ""Amount"", ""CreatedAt"") VALUES (1, 1, 1, 1, {0}, '2026-10-01', 5, '2026-10-30');";
        Execute(connection, string.Format(insert, "NULL"));
        Execute(connection, string.Format(insert, "NULL"));
        Execute(connection, string.Format(insert, "'A'"));
        Assert.Throws<SqliteException>(() => Execute(connection, string.Format(insert, "'A'")));
    }

    private static BankStatementImportRequest Csv(string body) => new()
    {
        Source = (int)BankStatementSources.Csv,
        Content = "Date,Amount,Payee,Memo,Ref\n" + body,
        ColumnMap = new BankStatementColumnMap
        {
            DateColumn = 0,
            AmountColumn = 1,
            PayeeColumn = 2,
            MemoColumn = 3,
            ReferenceColumn = 4
        }
    };

    private (int BankId, int AccountId) SeedBank()
    {
        using (_tenant.Impersonate(_companyId))
        {
            var assets = _db.AccountClasses.FirstOrDefault(c => c.NormalBalance == "Dr")
                ?? _db.AccountClasses.Add(new AccountClass { Name = "Assets", NormalBalance = "Dr" }).Entity;
            _db.SaveChanges();

            var cash = new Account
            {
                CompanyId = _companyId,
                AccountClassId = assets.Id,
                AccountCode = "10111",
                AccountName = "Checking",
                IsCash = true,
                DrOrCrSide = DrOrCrSide.Dr
            };
            _db.Accounts.Add(cash);
            _db.SaveChanges();

            var bank = new Bank
            {
                CompanyId = _companyId,
                Name = "Checking",
                Type = BankTypes.CheckingAccount,
                AccountId = cash.Id,
                IsActive = true
            };
            _db.Banks.Add(bank);
            _db.SaveChanges();
            return (bank.Id, cash.Id);
        }
    }

    private int PostLedger(int accountId, DrOrCrSide side, decimal amount, DateTime date)
    {
        var header = new GeneralLedgerHeader
        {
            CompanyId = _companyId,
            DocumentType = DocumentTypes.JournalEntry,
            Date = date,
            Description = "seed"
        };
        header.GeneralLedgerLines.Add(new GeneralLedgerLine
        {
            CompanyId = _companyId,
            AccountId = accountId,
            DrCr = side,
            Amount = amount
        });
        _db.GeneralLedgerHeaders.Add(header);
        _db.SaveChanges();
        return header.GeneralLedgerLines.Single().Id;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AccountGo.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root.");
    }
}
