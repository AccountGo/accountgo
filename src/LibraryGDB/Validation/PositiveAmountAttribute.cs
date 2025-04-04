using System;
using System.ComponentModel.DataAnnotations;

namespace LibraryGDB.Validation
{
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class PositiveAmountAttribute : ValidationAttribute
    {
        protected override ValidationResult IsValid(
            object? value,
            ValidationContext validationContext
        )
        {
            if (value is decimal amount && amount <= 0)
            {
                return new ValidationResult("Amount must be greater than zero.");
            }

            return ValidationResult.Success!;
        }
    }
}
