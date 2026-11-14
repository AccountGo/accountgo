#nullable disable
using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace AccountGo.BackendTests;

public partial class MultiTenancyPhase3Tests
{
    [Fact]
    public async Task DashboardPeriodFigures_FollowTheRangeAndLeaveOutYearEndClosing()
    {
        var fyEnd = DateTime.UtcNow.Date.AddDays(-1);
        var fyStart = new DateTime(fyEnd.Year, 1, 1);
        var fyCode = "FY" + fyEnd.Year.ToString(CultureInfo.InvariantCulture);
        var activityDay = fyStart.AddMonths(1);
        if (activityDay > fyEnd) activityDay = fyStart;
        WithDb(db =>
        {
            var fy = db.FinancialYears.Single(y => y.FiscalYearCode == fyCode);
            fy.EndDate = fyEnd;
            // Freelance income (40900) exists once personal books are linked; add it here like the link does.
            if (!db.Accounts.Any(a => a.AccountCode == "40900"))
            {
                var revenue = db.Accounts.Single(a => a.AccountCode == "40100");
                db.Accounts.Add(new Core.Domain.Financials.Account
                {
                    CompanyId = revenue.CompanyId, AccountClassId = revenue.AccountClassId, ParentAccountId = revenue.ParentAccountId,
                    DrOrCrSide = revenue.DrOrCrSide, AccountCode = "40900", AccountName = "Freelance income"
                });
            }
            db.SaveChanges();
        });

        // Freelance income 100 into cash, then owner drawings 30 out of cash, on one day.
        await PostJournal(activityDay, ("10111", 1, 100m), ("40900", 2, 100m));
        await PostJournal(activityDay, ("30800", 1, 30m), ("10111", 2, 30m));
        await CloseYearWithWarnings(fyCode);

        string Day(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        async Task<JsonElement> Summary(DateTime from, DateTime to)
        {
            using var response = await Send(HttpMethod.Get, $"/api/Dashboard/Summary?from={Day(from)}&to={Day(to)}");
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        static decimal Kpi(JsonElement summary, string name) => summary.GetProperty("kpis").GetProperty(name).GetDecimal();

        // The whole closed year: closing journals must not cancel the year's freelance income.
        var year = await Summary(fyStart, fyEnd);
        Assert.Equal(100m, Kpi(year, "moneyFromFreelanceWorkYtd"));
        Assert.Equal(30m, Kpi(year, "drawingsYtd"));
        Assert.Equal(Kpi(year, "totalSalesYtd") - 100m, Kpi(year, "earnedIncomeYtd"));

        // The year-end month's sales trend shows operating revenue only, not the closing transfer.
        var closingMonth = year.GetProperty("monthlySales").EnumerateArray().Last().GetProperty("amount").GetDecimal();
        var operatingInClosingMonth = activityDay.Year == fyEnd.Year && activityDay.Month == fyEnd.Month ? 100m : 0m;
        Assert.Equal(operatingInClosingMonth, closingMonth);

        // A one-day range includes entries dated on its end day.
        var day = await Summary(activityDay, activityDay);
        Assert.Equal(100m, Kpi(day, "moneyFromFreelanceWorkYtd"));
        Assert.Equal(30m, Kpi(day, "drawingsYtd"));
        Assert.Equal(70m, Kpi(day, "cashFlowYtd"));

        // A range after the activity leaves it out.
        if (activityDay.AddDays(1) <= fyEnd)
        {
            var later = await Summary(activityDay.AddDays(1), fyEnd);
            Assert.Equal(0m, Kpi(later, "moneyFromFreelanceWorkYtd"));
            Assert.Equal(0m, Kpi(later, "drawingsYtd"));
        }
    }

    [Fact]
    public async Task DashboardDrawings_InPartnerMode_IncludesPartnerDrawingsAccounts()
    {
        var fyEnd = DateTime.UtcNow.Date;
        var fyStart = new DateTime(fyEnd.Year, 1, 1);
        WithDb(db =>
        {
            var setting = db.CompanySettings.Single();
            setting.EntityType = Core.Domain.EntityType.Partnership;
            db.SaveChanges();
        });

        using var setupResponse = await Send(HttpMethod.Post, "/api/Financials/Partners/Setup", new
        {
            date = fyStart,
            partners = new[]
            {
                new { name = "Avery", percent = 50m, capital = 0m, drawings = 0m },
                new { name = "Blake", percent = 50m, capital = 0m, drawings = 0m },
            }
        });
        Assert.True(setupResponse.IsSuccessStatusCode, await setupResponse.Content.ReadAsStringAsync());

        await PostJournal(fyStart, ("30851", 1, 45m), ("10111", 2, 45m));

        string Day(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        using var response = await Send(HttpMethod.Get, $"/api/Dashboard/Summary?from={Day(fyStart)}&to={Day(fyEnd)}");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var summary = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(45m, summary.GetProperty("kpis").GetProperty("drawingsYtd").GetDecimal());
    }

    private async Task PostJournal(DateTime date, params (string Code, int DrCr, decimal Amount)[] lines)
    {
        var journal = new Dto.Financial.JournalEntry { JournalDate = date, VoucherType = 6, Memo = "Dashboard period check" };
        foreach (var (code, drCr, amount) in lines)
            journal.JournalEntryLines.Add(new Dto.Financial.JournalEntryLine { AccountId = AccountId(code), DrCr = drCr, Amount = amount });
        using var saved = await Send(HttpMethod.Post, "/api/Financials/SaveJournalEntry", journal);
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        journal.Id = (await saved.Content.ReadFromJsonAsync<Dto.Financial.JournalEntry>()).Id;
        var approval = await ApproveJournalPostAsync(journal.Id);
        using var posted = await Send(HttpMethod.Post, "/api/Financials/PostJournalEntry", journal, extraHeaders: approval);
        Assert.True(posted.IsSuccessStatusCode, await posted.Content.ReadAsStringAsync());
    }
}
