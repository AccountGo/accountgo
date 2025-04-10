using System;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace LibraryGDB.Validation
{
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class ValidAmountToPayAttribute : ValidationAttribute
    {
        private readonly string _invoiceAmountProperty;
        private readonly string _amountPaidProperty;

        public ValidAmountToPayAttribute(string invoiceAmountProperty, string amountPaidProperty)
        {
            _invoiceAmountProperty = invoiceAmountProperty;
            _amountPaidProperty = amountPaidProperty;
        }

        protected override ValidationResult IsValid(
            object? value,
            ValidationContext validationContext
        )
        {
            var amountToPay = value as decimal? ?? 0;

            var invoiceAmountProperty = validationContext.ObjectType.GetProperty(
                _invoiceAmountProperty
            );
            var amountPaidProperty = validationContext.ObjectType.GetProperty(_amountPaidProperty);

            if (invoiceAmountProperty == null || amountPaidProperty == null)
            {
                return new ValidationResult(
                    $"Invalid property names: {_invoiceAmountProperty} or {_amountPaidProperty}"
                );
            }

            var invoiceAmount = (decimal)(
                invoiceAmountProperty.GetValue(validationContext.ObjectInstance) ?? 0
            );
            var amountPaid = (decimal)(
                amountPaidProperty.GetValue(validationContext.ObjectInstance) ?? 0
            );
            var balance = invoiceAmount - amountPaid;

            if (amountToPay <= 0)
            {
                return new ValidationResult("Amount to pay must be greater than zero.");
            }

            if (amountToPay > balance)
            {
                return new ValidationResult(
                    "Amount to pay cannot be greater than the remaining amount to pay."
                );
            }

            return ValidationResult.Success!;
        }
    }
}
