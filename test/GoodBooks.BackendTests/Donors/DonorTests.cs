using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Dto.Donors;
using Xunit;

namespace GoodBooks.BackendTests.Donors
{
    public class DonorTests
    {
        [Theory]
        [InlineData("Individual", "Olivia", "Bennett", "", "Olivia Bennett")]
        [InlineData("Company", "", "", "Cedar Grove Community Services", "Cedar Grove Community Services")]
        public void Validate_ValidDonorType_AcceptsRelevantNameFields(string donorType, string firstName, string lastName, string companyName, string expectedName)
        {
            var donor = CreateValidDonor();
            donor.DonorType = donorType;
            donor.FirstName = firstName;
            donor.LastName = lastName;
            donor.CompanyName = companyName;

            var errors = Validate(donor);

            Assert.Empty(errors);
            Assert.Equal(expectedName, donor.Name);
        }

        [Theory]
        [InlineData("Individual", "", "Bennett", "Company", "FirstName")]
        [InlineData("Individual", "Olivia", "", "Company", "LastName")]
        [InlineData("Individual", "   ", "Bennett", "Company", "FirstName")]
        [InlineData("Company", "Olivia", "Bennett", "", "CompanyName")]
        [InlineData("Company", "Olivia", "Bennett", "   ", "CompanyName")]
        public void Validate_MissingRelevantName_RejectsDonor(string donorType, string firstName, string lastName, string companyName, string expectedMember)
        {
            var donor = CreateValidDonor();
            donor.DonorType = donorType;
            donor.FirstName = firstName;
            donor.LastName = lastName;
            donor.CompanyName = companyName;

            var errors = Validate(donor);

            Assert.Contains(errors, error => error.MemberNames.Contains(expectedMember));
        }

        [Theory]
        [InlineData("DonorType", "Other")]
        [InlineData("DonorType", "")]
        [InlineData("Street", "")]
        [InlineData("Province", "")]
        [InlineData("Province", "ZZ")]
        [InlineData("PostalCode", "")]
        [InlineData("PostalCode", "12345")]
        [InlineData("PostalCode", "D1A 0B1")]
        [InlineData("Telephone", "")]
        [InlineData("Telephone", "not-a-phone")]
        [InlineData("Email", "")]
        [InlineData("Email", "not-an-email")]
        public void Validate_InvalidContactOrType_RejectsField(string propertyName, string value)
        {
            var donor = CreateValidDonor();
            var property = typeof(Donor).GetProperty(propertyName)
                ?? throw new System.ArgumentException("Unknown donor property.", nameof(propertyName));
            property.SetValue(donor, value);

            var errors = Validate(donor);

            Assert.Contains(errors, error => error.MemberNames.Contains(propertyName));
        }

        [Theory]
        [InlineData("AB")]
        [InlineData("BC")]
        [InlineData("MB")]
        [InlineData("NB")]
        [InlineData("NL")]
        [InlineData("NS")]
        [InlineData("NT")]
        [InlineData("NU")]
        [InlineData("ON")]
        [InlineData("PE")]
        [InlineData("QC")]
        [InlineData("SK")]
        [InlineData("YT")]
        public void Validate_CanadianProvinceOrTerritory_AcceptsCode(string province)
        {
            var donor = CreateValidDonor();
            donor.Province = province;

            Assert.Empty(Validate(donor));
        }

        [Theory]
        [InlineData("K1A 0B1")]
        [InlineData("k1a0b1")]
        [InlineData("V6B 1A1")]
        public void Validate_CanadianPostalCode_AcceptsValidFormats(string postalCode)
        {
            var donor = CreateValidDonor();
            donor.PostalCode = postalCode;

            Assert.Empty(Validate(donor));
        }

        [Fact]
        public void Validate_PostalCodeWithForbiddenLetter_ExplainsFormatAndExcludedLetters()
        {
            var donor = CreateValidDonor();
            donor.PostalCode = "V4F6G7";

            var error = Assert.Single(Validate(donor));

            Assert.Contains(nameof(Donor.PostalCode), error.MemberNames);
            Assert.Contains("K1A 0B1 or K1A0B1", error.ErrorMessage);
            Assert.Contains("Letters D, F, I, O, Q and U are not allowed", error.ErrorMessage);
            Assert.Contains("Check the donor's assigned postal code", error.ErrorMessage);
        }

        [Fact]
        public void Validate_NameExceedsDatabaseLimit_RejectsName()
        {
            var donor = CreateValidDonor();
            donor.FirstName = new string('A', 101);

            Assert.Contains(Validate(donor), error => error.MemberNames.Contains(nameof(Donor.FirstName)));
        }

        private static Donor CreateValidDonor()
        {
            return new Donor
            {
                FirstName = "Olivia",
                LastName = "Bennett",
                Street = "125 Maple Street",
                Province = "ON",
                PostalCode = "K1A 0B1",
                Telephone = "613-555-0101",
                Email = "olivia.bennett@example.com"
            };
        }

        private static List<ValidationResult> Validate(Donor donor)
        {
            var errors = new List<ValidationResult>();
            Validator.TryValidateObject(donor, new ValidationContext(donor), errors, true);
            return errors;
        }
    }
}