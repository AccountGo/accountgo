using Api.Data;
using Core.Domain;
using Xunit;

namespace AccountGo.BackendTests.Financial;

public class PaymentAccountTemplateTests
{
    [Theory]
    [InlineData("10170", "Assets", "10100", DrOrCrSide.Dr)]
    [InlineData("10180", "Assets", "10100", DrOrCrSide.Dr)]
    [InlineData("20130", "Liabilities", "20100", DrOrCrSide.Cr)]
    [InlineData("40800", "Revenue", "40000", DrOrCrSide.Cr)]
    [InlineData("41300", "Revenue", "40000", DrOrCrSide.Cr)]
    [InlineData("50122", "Expense", "50100", DrOrCrSide.Dr)]
    [InlineData("50123", "Expense", "50100", DrOrCrSide.Dr)]
    [InlineData("50124", "Expense", "50100", DrOrCrSide.Dr)]
    [InlineData("50125", "Expense", "50100", DrOrCrSide.Dr)]
    [InlineData("50126", "Expense", "50100", DrOrCrSide.Dr)]
    public void BaseChart_LoadsPostablePaymentAccounts_WithCorrectClassification(
        string code, string className, string parentCode, DrOrCrSide normalSide)
    {
        var classes = Initializer.GetAccountClassesFromCsv();
        var account = Assert.Single(classes.Single(c => c.Name == className).Accounts, a => a.AccountCode == code);
        Assert.Equal(parentCode, account.ParentAccount?.AccountCode);
        Assert.Equal(normalSide, account.DrOrCrSide);
        Assert.True(account.CanPost());
        Assert.False(account.IsCash);
        foreach (var template in new[] { CoaTemplates.ServicesSme, CoaTemplates.TradingFifo, CoaTemplates.TradingWeightedAverage, CoaTemplates.MixedSme })
            Assert.DoesNotContain(code, CoaTemplates.OmittedAccountCodes(template));
    }
}
