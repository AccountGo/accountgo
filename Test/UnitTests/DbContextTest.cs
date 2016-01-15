using Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace UnitTests
{
    [TestClass]
    public class DbContextTest
    {
        string connectionString = "data source=localhost;initial catalog=apphbDB;integrated security=sspi;multipleactiveresultsets=true;Pooling=false";
        [TestMethod]
        public void TestMethod1()
        {
            using (var context = new ApplicationContext(connectionString))
            {
                //var account = context.Accounts.Where(a => a.AccountCode == "10111").FirstOrDefault();
                //var balance = account.GetBalance();

                //var gl = context.GeneralLedgerHeaders.Where(g => g.Id == 86).FirstOrDefault();
                //bool isEqual = gl.ValidateAccountingEquation();
                //isEqual = gl.ValidateAssetsEqualsEquities();
                //isEqual = gl.ValidateLiabilitiesEqualsExpenses();
                //isEqual = gl.ValidateAssetsEqualsRevenues();
            }
        }
    }
}
