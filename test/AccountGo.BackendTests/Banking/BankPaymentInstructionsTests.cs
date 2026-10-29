// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using Core.Domain;
using Core.Domain.Financials;
using Services.Banking;
using Xunit;

namespace AccountGo.BackendTests.Banking;

/// <summary>Phase 3G: invoice payment instructions built from the workspace's payment bank.</summary>
public class BankPaymentInstructionsTests
{
    private const string FreeTextDefault = "Pay by cash at the counter";
    private const string PayeeName = "Acme Trading";
    private const string InvoiceNo = "INV-0042";

    private static Bank Bank(string? number = "001-234-567", string? iban = null) => new()
    {
        Id = 7,
        Name = "Operating",
        BankName = "First National",
        Number = number!,
        RoutingNumber = "021000021",
        Iban = iban!,
        Swift = "FNBAUS33",
    };

    private static SalesInvoiceSettings Settings(int? bankId = 7) => new()
    {
        DefaultPaymentInstructions = FreeTextDefault,
        PaymentBankId = bankId,
    };

    [Fact]
    public void Format_ListsDetailsAndSkipsEmptyFields()
    {
        var bank = Bank();
        bank.Swift = "  ";

        var text = BankPaymentInstructions.Format(bank, PayeeName, InvoiceNo)!;

        Assert.StartsWith(BankPaymentInstructions.Heading, text);
        Assert.Contains("Bank: First National", text);
        Assert.Contains("Account name: Acme Trading", text);
        Assert.Contains("Account number: 001-234-567", text);
        Assert.Contains("Routing number: 021000021", text);
        Assert.Contains("Reference: INV-0042", text);
        Assert.DoesNotContain("SWIFT", text);
        Assert.DoesNotContain("IBAN", text);
    }

    [Fact]
    public void Format_FallsBackToTheAccountNameWhenThereIsNoBankName()
    {
        var bank = Bank();
        bank.BankName = "";

        Assert.Contains("Bank: Operating", BankPaymentInstructions.Format(bank, PayeeName, InvoiceNo));
    }

    [Fact]
    public void Format_IbanAloneCountsAsAccountDetails()
    {
        var bank = Bank(number: null, iban: "DE89370400440532013000");

        Assert.Contains("IBAN: DE89370400440532013000", BankPaymentInstructions.Format(bank, PayeeName, InvoiceNo));
    }

    [Fact]
    public void Format_NoAccountNumberOrIban_ReturnsNullSoFreeTextApplies()
    {
        Assert.Null(BankPaymentInstructions.Format(Bank(number: null), PayeeName, InvoiceNo));
        Assert.Null(BankPaymentInstructions.Format(Bank(number: "   "), PayeeName, InvoiceNo));
    }

    [Fact]
    public void Resolve_BankDetailsBeatTheFreeTextDefault()
    {
        var text = BankPaymentInstructions.Resolve(null, Settings(), Bank(), PayeeName, InvoiceNo);

        Assert.Contains("Account number: 001-234-567", text);
        Assert.DoesNotContain(FreeTextDefault, text);
    }

    [Fact]
    public void Resolve_InvoiceFrozenWithTheDefaultTextStillGetsBankDetails()
    {
        var text = BankPaymentInstructions.Resolve(FreeTextDefault, Settings(), Bank(), PayeeName, InvoiceNo);

        Assert.Contains("Account number: 001-234-567", text);
    }

    [Fact]
    public void Resolve_InvoiceOwnEditedTextWins()
    {
        var text = BankPaymentInstructions.Resolve("Wire to our Singapore account", Settings(), Bank(), PayeeName, InvoiceNo);

        Assert.Equal("Wire to our Singapore account", text);
    }

    [Fact]
    public void Resolve_ArchivedBankFallsBackToFreeText()
    {
        var bank = Bank();
        bank.ArchivedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal(FreeTextDefault, BankPaymentInstructions.Resolve(null, Settings(), bank, PayeeName, InvoiceNo));
    }

    [Fact]
    public void Resolve_NoBankSelected_UsesStoredTextThenDefault()
    {
        Assert.Equal(FreeTextDefault, BankPaymentInstructions.Resolve(null, Settings(bankId: null), null, PayeeName, InvoiceNo));
        Assert.Equal("Own text", BankPaymentInstructions.Resolve(" Own text ", Settings(bankId: null), null, PayeeName, InvoiceNo));
    }

    [Fact]
    public void Resolve_BankWithoutAccountDetailsFallsBackToFreeText()
    {
        Assert.Equal(FreeTextDefault, BankPaymentInstructions.Resolve(null, Settings(), Bank(number: null), PayeeName, InvoiceNo));
    }

    [Fact]
    public void Resolve_NothingConfigured_ReturnsNull()
    {
        Assert.Null(BankPaymentInstructions.Resolve(null, new SalesInvoiceSettings(), null, PayeeName, InvoiceNo));
    }

    [Fact]
    public void PaymentBankId_RoundTripsThroughSettingsJson()
    {
        var copy = SalesInvoiceSettings.FromJson(Settings(bankId: 7).ToJson());

        Assert.Equal(7, copy.PaymentBankId);
        Assert.Null(SalesInvoiceSettings.FromJson("{\"showNotes\":true}").PaymentBankId);
        Assert.Null(SalesInvoiceSettings.FromJson(new SalesInvoiceSettings().ToJson()).PaymentBankId);
    }
}
