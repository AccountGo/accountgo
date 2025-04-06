using System;
using System.ComponentModel.DataAnnotations;

namespace LibraryGDB.Validation
{
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class GreaterThanZeroAttribute : ValidationAttribute
    {
        protected override ValidationResult IsValid(
            object? value,
            ValidationContext validationContext
        )
        {
            if (value is decimal decimalValue && decimalValue > 0)
            {
                return ValidationResult.Success!;
            }
            return new ValidationResult(ErrorMessage ?? "Value must be greater than zero.");
        }
    }
}
