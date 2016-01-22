using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Data;
using Core.Domain;

namespace UnitTests
{
    [TestClass]
    public class UnitTest1
    {
        string connectionString = "data source=localhost;initial catalog=accountgo;integrated security=sspi;multipleactiveresultsets=true;Pooling=false";

        [TestMethod]
        public void TestMethod1()
        {
            string filename = @"C:\Development\accountgo\github\Test\UnitTests\App_Data\coa.csv";

            //using (var context = new ApplicationContext(connectionString))
            //{
            //    DbInitializerHelper._context = context;
            //    DbInitializerHelper._filename = filename;

            //    DbInitializerHelper.Initialize();
            //}
        }
    }
}
