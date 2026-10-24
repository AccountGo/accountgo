using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Core.Domain;
using Core.Domain.Financials;
using Xunit;

namespace AccountGo.BackendTests
{
    public partial class MultiTenancyPhase3Tests
    {
        [Fact]
        public async Task Reports_FilterLedgerDates_KeepOpeningBalances_AndBalanceWithContraAccounts()
        {
            var start = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
            var end = start.AddMonths(1).AddTicks(-1);
            WithDb(db =>
            {
                db.Companies.Single(c => c.Id == _companyId).ActiveModulesJson = "[\"reports\"]";
                var asset = db.Accounts.First(a => a.AccountClassId == 1 && !a.ChildAccounts.Any());
                var equity = db.Accounts.First(a => a.AccountClassId == 3 && !a.ChildAccounts.Any());
                var revenue = db.Accounts.First(a => a.AccountClassId == 4 && !a.ChildAccounts.Any());
                var expense = db.Accounts.First(a => a.AccountClassId == 5 && !a.ChildAccounts.Any());
                var contra = new Account
                {
                    CompanyId = _companyId, AccountCode = "TEST-CONTRA", AccountName = "Accumulated depreciation",
                    AccountClassId = 1, IsContraAccount = true
                };
                db.Accounts.Add(contra);
                db.GeneralLedgerHeaders.AddRange(
                    ReportEntry(start.AddTicks(-1), asset, equity, 100m),
                    ReportEntry(start, asset, revenue, 200m),
                    ReportEntry(end, expense, contra, 30m),
                    ReportEntry(end.AddTicks(1), asset, revenue, 900m));
                db.SaveChanges();
            });
            var query = "?from=" + start.ToString("O", CultureInfo.InvariantCulture) +
                "&to=" + end.ToString("O", CultureInfo.InvariantCulture);

            var trialResponse = await Send(HttpMethod.Get, "/api/Financials/trialbalance" + query);
            Assert.Equal(HttpStatusCode.OK, trialResponse.StatusCode);
            var trial = (await Json(trialResponse)).EnumerateArray().ToList();
            Assert.Equal(230m, trial.Sum(a => a.GetProperty("debit").GetDecimal()));
            Assert.Equal(230m, trial.Sum(a => a.GetProperty("credit").GetDecimal()));
            Assert.Equal(trial.Select(a => a.GetProperty("accountCode").GetString()).OrderBy(a => a),
                trial.Select(a => a.GetProperty("accountCode").GetString()));

            var incomeResponse = await Send(HttpMethod.Get, "/api/Financials/IncomeStatement" + query);
            Assert.Equal(HttpStatusCode.OK, incomeResponse.StatusCode);
            var income = (await Json(incomeResponse)).EnumerateArray().ToList();
            Assert.Equal(200m, income.Where(a => !a.GetProperty("isExpense").GetBoolean()).Sum(a => a.GetProperty("amount").GetDecimal()));
            Assert.Equal(30m, income.Where(a => a.GetProperty("isExpense").GetBoolean()).Sum(a => a.GetProperty("amount").GetDecimal()));

            var balanceResponse = await Send(HttpMethod.Get, "/api/Financials/BalanceSheet" + query);
            Assert.Equal(HttpStatusCode.OK, balanceResponse.StatusCode);
            var balance = (await Json(balanceResponse)).EnumerateArray().ToList();
            var assets = balance.Where(a => a.GetProperty("accountClassId").GetInt32() == 1).Sum(a => a.GetProperty("amount").GetDecimal());
            var liabilitiesAndEquity = balance.Where(a => a.GetProperty("accountClassId").GetInt32() != 1).Sum(a => a.GetProperty("amount").GetDecimal());
            Assert.Equal(270m, assets);
            Assert.Equal(assets, liabilitiesAndEquity);
            Assert.Contains(balance, a => a.GetProperty("accountCode").GetString() == "TEST-CONTRA" && a.GetProperty("amount").GetDecimal() == -30m);
            Assert.Contains(balance, a => a.GetProperty("accountId").GetInt32() == 0 && a.GetProperty("amount").GetDecimal() == 170m);

            Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, "/api/Financials/Accounts")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, "/api/Administration/financialyears")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, "/api/Financials/AddAccount", new { accountCode = "forbidden" })).StatusCode);
        }

        [Fact]
        public async Task Reports_UnclosedLoss_ReducesEquity_AndClosingDoesNotDoubleCountIncome()
        {
            WithDb(db =>
            {
                db.Companies.Single(c => c.Id == _companyId).ActiveModulesJson = "[\"reports\"]";
                var asset = db.Accounts.First(a => a.AccountClassId == 1 && !a.ChildAccounts.Any());
                var expense = db.Accounts.First(a => a.AccountClassId == 5 && !a.ChildAccounts.Any());
                var equity = db.Accounts.First(a => a.AccountClassId == 3 && !a.ChildAccounts.Any());
                var day = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                db.GeneralLedgerHeaders.AddRange(
                    ReportEntry(day, asset, equity, 100m),
                    ReportEntry(day.AddDays(1), expense, asset, 40m),
                    ReportEntry(day.AddDays(2), equity, expense, 40m));
                db.SaveChanges();
            });
            foreach (var day in new[] { "2026-01-02", "2026-01-03" })
            {
                var response = await Send(HttpMethod.Get, "/api/Financials/BalanceSheet?to=" + day + "T23:59:59");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var rows = (await Json(response)).EnumerateArray().ToList();
                Assert.Equal(60m, rows.Where(a => a.GetProperty("accountClassId").GetInt32() == 1).Sum(a => a.GetProperty("amount").GetDecimal()));
                Assert.Equal(60m, rows.Where(a => a.GetProperty("accountClassId").GetInt32() == 3).Sum(a => a.GetProperty("amount").GetDecimal()));
                if (day == "2026-01-02")
                    Assert.Contains(rows, a => a.GetProperty("accountId").GetInt32() == 0 && a.GetProperty("amount").GetDecimal() == -40m);
                else
                    Assert.DoesNotContain(rows, a => a.GetProperty("accountId").GetInt32() == 0);
            }
        }

        [Theory]
        [InlineData("trialbalance")]
        [InlineData("IncomeStatement")]
        [InlineData("BalanceSheet")]
        public async Task Reports_RequireReportsView_AndRejectInvalidDates(string action)
        {
            WithDb(db =>
            {
                db.Companies.Single(c => c.Id == _companyId).ActiveModulesJson = "[\"reports\"]";
                db.SaveChanges();
            });
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Get, "/api/Financials/" + action + "?from=2026-05-01&to=2026-04-01")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Get, "/api/Financials/" + action + "?from=invalid")).StatusCode);
            await JoinOther();
            Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Patch, Current + "/members/" + MemberId(_otherId),
                new { securityRoleIds = new[] { RoleId("Sales Representative") } })).StatusCode);
            var denied = await Send(HttpMethod.Get, "/api/Financials/" + action, token: _otherToken);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Contains("reports.view", await denied.Content.ReadAsStringAsync());
        }

        private GeneralLedgerHeader ReportEntry(DateTime date, Account debit, Account credit, decimal amount)
        {
            return new GeneralLedgerHeader
            {
                CompanyId = _companyId, Date = date, Description = "Report test", DocumentType = DocumentTypes.JournalEntry,
                GeneralLedgerLines =
                {
                    new GeneralLedgerLine { CompanyId = _companyId, Account = debit, DrCr = DrOrCrSide.Dr, Amount = amount },
                    new GeneralLedgerLine { CompanyId = _companyId, Account = credit, DrCr = DrOrCrSide.Cr, Amount = amount }
                }
            };
        }
    }
}
