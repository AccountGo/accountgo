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
            modelBuilder.Entity<Core.Domain.Donations.Donor>().HasData(
                new Core.Domain.Donations.Donor
                {
                    Id = 1,
                    DonorType = "Individual",
                    FirstName = "Olivia",
                    LastName = "Bennett",
                    CompanyName = string.Empty,
                    Street = "125 Maple Street",
                    Province = "ON",
                    PostalCode = "K1A 0B1",
                    Telephone = "613-555-0101",
                    Email = "olivia.bennett@example.com"
                },
                new Core.Domain.Donations.Donor
                {
                    Id = 2,
                    DonorType = "Company",
                    FirstName = string.Empty,
                    LastName = string.Empty,
                    CompanyName = "Cedar Grove Community Services",
                    Street = "480 Cedar Avenue",
                    Province = "BC",
                    PostalCode = "V6B 1A1",
                    Telephone = "604-555-0102",
                    Email = "giving@cedargrove.example.com"
                }
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
