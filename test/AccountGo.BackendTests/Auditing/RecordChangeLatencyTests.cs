using System.Diagnostics;
using Api.Auditing;
using Api.Data;
using Core.Domain;
using Core.Domain.Financials;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace AccountGo.BackendTests.Auditing;

public sealed class RecordChangeLatencyTests
{
    private readonly ITestOutputHelper _output;
    public RecordChangeLatencyTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Reports_50_line_journal_save_time_with_and_without_record_audit()
    {
        Measure(false); // Warm model and SQLite setup before sampling.
        Measure(true);
        var withoutAudit = new List<double>();
        var withAudit = new List<double>();
        for (var sample = 0; sample < 9; sample++)
        {
            withAudit.Add(Measure(true));
            withoutAudit.Add(Measure(false));
        }
        var withoutMedian = Percentile(withoutAudit, 0.50);
        var withMedian = Percentile(withAudit, 0.50);
        var withoutP95 = Percentile(withoutAudit, 0.95);
        var withP95 = Percentile(withAudit, 0.95);
        var medianDifference = withMedian - withoutMedian;
        var medianPercent = withoutMedian == 0 ? 0 : medianDifference / withoutMedian * 100;
        var p95Percent = withoutP95 == 0 ? 0 : (withP95 - withoutP95) / withoutP95 * 100;
        _output.WriteLine($"50-line journal SaveChanges, 9 samples: median without={withoutMedian:F2} ms, with={withMedian:F2} ms, delta={medianDifference:F2} ms ({medianPercent:F1}%); p95 without={withoutP95:F2} ms, with={withP95:F2} ms, delta={p95Percent:F1}%.");
    }

    private static double Percentile(List<double> values, double percentile)
    {
        values.Sort();
        return values[Math.Clamp((int)Math.Ceiling(percentile * values.Count) - 1, 0, values.Count - 1)];
    }

    private static double Measure(bool includeAudit)
    {
        using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
        connection.Open();
        var builder = new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(connection);
        if (includeAudit)
            builder.AddInterceptors(new RecordChangeInterceptor(new FixedAuditContext()));
        using var db = new ApiDbContext(builder.Options);
        db.Database.EnsureCreated();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA foreign_keys=OFF";
            command.ExecuteNonQuery();
        }
        var header = new JournalEntryHeader
        {
            CompanyId = 1,
            Date = DateTime.UtcNow.Date,
            Memo = "Latency sample",
            ReferenceNo = "latency-sample",
            JournalEntryLines = Enumerable.Range(1, 50).Select(index => new JournalEntryLine
            {
                CompanyId = 1,
                AccountId = index,
                DrCr = index % 2 == 0 ? DrOrCrSide.Dr : DrOrCrSide.Cr,
                Amount = index,
                Memo = $"Line {index}"
            }).ToList()
        };
        db.JournalEntryHeaders.Add(header);
        var stopwatch = Stopwatch.StartNew();
        db.SaveChanges();
        stopwatch.Stop();
        return stopwatch.Elapsed.TotalMilliseconds;
    }

    private sealed class FixedAuditContext : IRequestAuditContext
    {
        public string? ActorUserId => "latency-test";
        public string ActorKind => "user";
        public string CorrelationId => "latency-test-trace";
        public string? IpAddress => null;
    }
}
