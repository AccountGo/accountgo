using System;
using System.Linq;
using Api.Data;
using Core.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AccountGo.ServicesTests
{
    public class CompanyAccountingPolicyRulesTests
    {
        [Theory]
        [InlineData(ReportingFramework.IfrsForSmes, AccountingBasis.Accrual, InventoryCostingMethod.None)]
        [InlineData(ReportingFramework.UsGaapPrivate, AccountingBasis.Accrual, InventoryCostingMethod.Fifo)]
        [InlineData(ReportingFramework.Ifrs, AccountingBasis.Accrual, InventoryCostingMethod.WeightedAverage)]
        [InlineData(ReportingFramework.CashBasis, AccountingBasis.Cash, InventoryCostingMethod.None)]
        public void Validate_AcceptsSupportedPolicyCombinations(
            ReportingFramework framework,
            AccountingBasis basis,
            InventoryCostingMethod inventoryCostingMethod)
        {
            Assert.Empty(CompanyAccountingPolicyRules.Validate(framework, basis, inventoryCostingMethod));
        }

        [Theory]
        [InlineData(ReportingFramework.CashBasis, AccountingBasis.Accrual)]
        [InlineData(ReportingFramework.IfrsForSmes, AccountingBasis.Cash)]
        [InlineData(ReportingFramework.UsGaapPrivate, AccountingBasis.Cash)]
        [InlineData(ReportingFramework.Ifrs, AccountingBasis.Cash)]
        public void Validate_RejectsMismatchedFrameworkAndBasis(ReportingFramework framework, AccountingBasis basis)
        {
            Assert.Contains(CompanyAccountingPolicyRules.Validate(framework, basis, InventoryCostingMethod.None),
                error => error.Contains("accounting basis", StringComparison.OrdinalIgnoreCase));
        }

        [Theory]
        [InlineData(3)]
        [InlineData(100)]
        public void Validate_RejectsUnsupportedInventoryCostingMethods(int method)
        {
            var errors = CompanyAccountingPolicyRules.Validate(
                ReportingFramework.IfrsForSmes,
                AccountingBasis.Accrual,
                (InventoryCostingMethod)method);

            Assert.Contains(errors, error => error.Contains("inventory costing", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void CompanySetting_PolicyFields_RoundTripThroughSqlite()
        {
            using (var connection = new SqliteConnection("Data Source=:memory:"))
            {
                connection.Open();
                var options = new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(connection).Options;
                using (var db = new ApiDbContext(options))
                {
                    db.Database.EnsureCreated();
                    var company = new Company { Name = "Policy test", Slug = "policy-test" };
                    db.Companies.Add(company);
                    db.CompanySettings.Add(new CompanySetting
                    {
                        Company = company,
                        Currency = "EUR",
                        ReportingFramework = ReportingFramework.CashBasis,
                        AccountingBasis = AccountingBasis.Cash,
                        TaxRegime = TaxRegime.VatGst,
                        FiscalYearPattern = FiscalYearPattern.Custom,
                        FiscalYearStartMonth = 10,
                        BooksStartDate = new DateTime(2026, 10, 1),
                        IndustryTemplate = IndustryTemplate.Mixed,
                        InventoryCostingMethod = InventoryCostingMethod.Fifo,
                        CoaTemplateId = "mixed-sme",
                        OnboardingChecklistDismissedAt = new DateTime(2026, 10, 2, 3, 4, 5, DateTimeKind.Utc),
                        OnboardingChecklistJson = "{\"taxes\":true}"
                    });
                    db.SaveChanges();
                    db.ChangeTracker.Clear();

                    var setting = db.CompanySettings.IgnoreQueryFilters().Single();
                    Assert.Equal(ReportingFramework.CashBasis, setting.ReportingFramework);
                    Assert.Equal(AccountingBasis.Cash, setting.AccountingBasis);
                    Assert.Equal(TaxRegime.VatGst, setting.TaxRegime);
                    Assert.Equal(FiscalYearPattern.Custom, setting.FiscalYearPattern);
                    Assert.Equal(10, setting.FiscalYearStartMonth);
                    Assert.Equal(new DateTime(2026, 10, 1), setting.BooksStartDate);
                    Assert.Equal(IndustryTemplate.Mixed, setting.IndustryTemplate);
                    Assert.Equal(InventoryCostingMethod.Fifo, setting.InventoryCostingMethod);
                    Assert.Equal("mixed-sme", setting.CoaTemplateId);
                    Assert.Equal("{\"taxes\":true}", setting.OnboardingChecklistJson);
                    Assert.NotNull(setting.OnboardingChecklistDismissedAt);
                }
            }
        }

        [Fact]
        public void CompanySetting_DefaultsMatchLegacyPolicyBackfill()
        {
            var setting = new CompanySetting();

            Assert.Equal(ReportingFramework.IfrsForSmes, setting.ReportingFramework);
            Assert.Equal(TaxRegime.None, setting.TaxRegime);
            Assert.Equal(FiscalYearPattern.Calendar, setting.FiscalYearPattern);
            Assert.Equal(IndustryTemplate.Services, setting.IndustryTemplate);
            Assert.Equal(InventoryCostingMethod.None, setting.InventoryCostingMethod);
            Assert.Equal("legacy-default", setting.CoaTemplateId);
            Assert.Equal(DateTime.UtcNow.Date, setting.BooksStartDate);
        }
    }
}
