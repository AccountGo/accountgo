using System;
using System.ComponentModel.DataAnnotations;

public class AmountToPayValidationAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        // Access the containing object so we can read other properties
        var instance = validationContext.ObjectInstance;
        var balanceProperty = validationContext.ObjectType.GetProperty("Balance");
        if (balanceProperty == null)
            return new ValidationResult("Balance property not found.");

        var balanceValue = (decimal)balanceProperty.GetValue(instance)!;
        var amountToPay = value as decimal? ?? 0;

        if (amountToPay <= 0)
            return new ValidationResult("Amount to pay cannot be zero.");

        if (amountToPay > balanceValue)
            return new ValidationResult("Amount to pay cannot be greater than remaining amount to pay.");

        return ValidationResult.Success;
    }
}