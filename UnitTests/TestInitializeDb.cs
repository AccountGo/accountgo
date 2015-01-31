using Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Configuration;
using System.Data.Entity;
using System.Linq;

namespace UnitTests
{
    [TestClass]
    public class TestInitializeDb
    {
        ApplicationContext _context;
        [TestInitialize]
        public void InitializeDb()
        {
            _context = new ApplicationContext(ConfigurationManager.ConnectionStrings["ApplicationDbContext"].ConnectionString);
            var dbInitializer = new DbInitializer<ApplicationContext>();
            Database.SetInitializer<ApplicationContext>(dbInitializer);
            dbInitializer.InitializeDatabase(_context);
        }

        [TestCleanup]
        public void CleanUp()
        {
            _context.Dispose();
        }

        [TestMethod]
        [Priority(1)]
        public void GetAccountIsNotNull()
        {
            var account = _context.Accounts.Where(a => a.AccountCode == "10000").FirstOrDefault();
            Assert.IsNotNull(account);
        }
    }
}
