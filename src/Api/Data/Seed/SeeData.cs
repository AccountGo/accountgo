using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Numerics;
using Core.Domain;
using Api.Data;
using Core.Domain.Financials;

namespace Api.Data.Seed
{
    public static class SeedData
    {
        // this is an extension method to the ModelBuilder class
        public static void Seed(this ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Company>().HasData(
                GetCompanies()
            );
        }

        private static List<Company> GetCompanies()
        {
            List<Company> companiees = new List<Company>() {
                new Company() {    // 1
                    Id = 1,
                    Name = "Financial Solutions Inc.",
                    CompanyCode = "100",
                    ShortName = "FSI",
                    CRA = "012345678"
                }
            };
            return companiees;
        }

        public static IList<AccountClass> GetChartOfAccounts()
        {
            IList<AccountClass> acccountClasses = Initializer.GetAccountClassesFromCsv();

            return acccountClasses;
        }

    }
}
