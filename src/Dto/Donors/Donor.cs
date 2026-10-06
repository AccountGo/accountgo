using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Dto.Donors
{
    public class Donor : IValidatableObject
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Select a donor type: Individual or Company."),
         RegularExpression("^(Individual|Company)$", ErrorMessage = "Donor type must be Individual or Company.")]
        public string DonorType { get; set; } = "Individual";

        [StringLength(100, ErrorMessage = "First name cannot exceed 100 characters. Shorten the first name and try again.")]
        public string FirstName { get; set; } = string.Empty;

        [StringLength(100, ErrorMessage = "Last name cannot exceed 100 characters. Shorten the last name and try again.")]
        public string LastName { get; set; } = string.Empty;

        [StringLength(200, ErrorMessage = "Company name cannot exceed 200 characters. Shorten the company name and try again.")]
        public string CompanyName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Street is required. Enter the donor's street address."),
         StringLength(200, ErrorMessage = "Street address cannot exceed 200 characters.")]
        public string Street { get; set; } = string.Empty;

        [Required(ErrorMessage = "Province is required. Select the donor's Canadian province or territory."),
         RegularExpression("^(AB|BC|MB|NB|NL|NS|NT|NU|ON|PE|QC|SK|YT)$", ErrorMessage = "Select a Canadian province or territory from the Province dropdown.")]
        public string Province { get; set; } = string.Empty;

        [Required(ErrorMessage = "Postal code is required. Enter the donor's Canadian postal code, for example K1A 0B1."),
         RegularExpression(@"(?i)^[ABCEGHJ-NPRSTVXY]\d[ABCEGHJ-NPRSTV-Z] ?\d[ABCEGHJ-NPRSTV-Z]\d$",
             ErrorMessage = "Postal code must use letter-number-letter, number-letter-number, for example K1A 0B1 or K1A0B1. Letters D, F, I, O, Q and U are not allowed, and the first letter cannot be W or Z. Check the donor's assigned postal code.")]
        public string PostalCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Telephone is required. Enter the donor's telephone number."),
         Phone(ErrorMessage = "Telephone number is invalid. Enter a number such as 613-555-0101 or +1 613-555-0101."),
         StringLength(30, ErrorMessage = "Telephone number cannot exceed 30 characters, including its country code and extension.")]
        public string Telephone { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email is required. Enter the donor's email address."),
         EmailAddress(ErrorMessage = "Email address is invalid. Use a name followed by @ and a domain, for example donor@example.com."),
         StringLength(254, ErrorMessage = "Email address cannot exceed 254 characters.")]
        public string Email { get; set; } = string.Empty;

        public string Name
        {
            get { return DonorType == "Company" ? CompanyName : (FirstName + " " + LastName).Trim(); }
        }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (DonorType == "Individual")
            {
                if (string.IsNullOrWhiteSpace(FirstName))
                    yield return new ValidationResult("First name is required for an Individual donor. Enter the donor's given name.", new[] { nameof(FirstName) });
                if (string.IsNullOrWhiteSpace(LastName))
                    yield return new ValidationResult("Last name is required for an Individual donor. Enter the donor's family name.", new[] { nameof(LastName) });
            }
            else if (DonorType == "Company" && string.IsNullOrWhiteSpace(CompanyName))
            {
                yield return new ValidationResult("Company name is required for a Company donor. Enter the organization's name.", new[] { nameof(CompanyName) });
            }
        }
    }
}