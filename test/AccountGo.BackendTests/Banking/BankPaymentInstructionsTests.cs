// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Sales;
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

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_ClearedField_PrintsNothingEvenWithABankAndDefaultText(string cleared)
    {
        Assert.Null(BankPaymentInstructions.Resolve(cleared, Settings(), Bank(), PayeeName, InvoiceNo));
        Assert.Null(BankPaymentInstructions.Resolve(cleared, Settings(bankId: null), null, PayeeName, InvoiceNo));
    }

    [Fact]
    public void Resolve_NeverSetField_StillUsesTheWorkspaceDefaults()
    {
        Assert.Contains("Account number", BankPaymentInstructions.Resolve(null, Settings(), Bank(), PayeeName, InvoiceNo));
        Assert.Equal(FreeTextDefault, BankPaymentInstructions.Resolve(null, Settings(bankId: null), null, PayeeName, InvoiceNo));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("  Wire us  ", "Wire us")]
    public void ForSave_KeepsAbsentAsNull_AndClearedAsEmpty(string? requested, string? expected)
    {
        Assert.Equal(expected, BankPaymentInstructions.ForSave(requested));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ForPresentation_IssuedInvoiceNeverFallsBackToTheBankOrDefault(string? stored)
    {
        var text = BankPaymentInstructions.ForPresentation(true, stored, Settings(), Bank(), PayeeName, InvoiceNo);

        Assert.Null(text);
    }

    [Fact]
    public void ForPresentation_IssuedInvoicePrintsItsStoredTextEvenAfterTheWorkspaceChanges()
    {
        const string frozen = "Pay by bank transfer to:\nBank: Old Bank\nReference: INV-0042";

        var text = BankPaymentInstructions.ForPresentation(true, frozen, Settings(), Bank(), PayeeName, InvoiceNo);

        Assert.Equal(frozen, text);
    }

    [Fact]
    public void ForPresentation_DraftResolvesLive()
    {
        var text = BankPaymentInstructions.ForPresentation(false, null, Settings(), Bank(), PayeeName, InvoiceNo);

        Assert.Contains("Account number: 001-234-567", text);
        Assert.Null(BankPaymentInstructions.ForPresentation(false, "", Settings(), Bank(), PayeeName, InvoiceNo));
    }

    private static SalesInvoiceHeader Invoice(SalesInvoiceStatus? status, string? instructions) => new()
    {
        Status = status,
        PaymentInstructions = instructions,
        No = InvoiceNo,
    };

    [Fact]
    public void Capture_AtIssue_WritesTheResolvedTextWhenUnset()
    {
        var invoice = Invoice(SalesInvoiceStatus.Open, null);
        var resolved = BankPaymentInstructions.Resolve(invoice.PaymentInstructions, Settings(), Bank(), PayeeName, InvoiceNo);

        invoice.CapturePaymentInstructionsIfIssued(resolved);

        Assert.Equal(resolved, invoice.PaymentInstructions);
        Assert.Contains("Reference: INV-0042", invoice.PaymentInstructions);
    }

    [Fact]
    public void Capture_AtIssue_KeepsAnExplicitlyClearedField()
    {
        var invoice = Invoice(SalesInvoiceStatus.Open, "");

        invoice.CapturePaymentInstructionsIfIssued("Pay by bank transfer to:\nBank: First National");

        Assert.Equal("", invoice.PaymentInstructions);
    }

    [Fact]
    public void Capture_AtIssue_KeepsTheInvoicesOwnText()
    {
        var invoice = Invoice(SalesInvoiceStatus.Open, "Wire to our Singapore account");
        var resolved = BankPaymentInstructions.Resolve(invoice.PaymentInstructions, Settings(), Bank(), PayeeName, InvoiceNo);

        invoice.CapturePaymentInstructionsIfIssued(resolved);

        Assert.Equal("Wire to our Singapore account", invoice.PaymentInstructions);
    }

    [Fact]
    public void Capture_AtIssue_ReplacesACopyOfTheDefaultTextWithTheBankDetailsTheDraftShowed()
    {
        var invoice = Invoice(SalesInvoiceStatus.Open, FreeTextDefault);
        var resolved = BankPaymentInstructions.Resolve(invoice.PaymentInstructions, Settings(), Bank(), PayeeName, InvoiceNo);

        invoice.CapturePaymentInstructionsIfIssued(resolved);

        Assert.Contains("Account number: 001-234-567", invoice.PaymentInstructions);
    }

    [Fact]
    public void Capture_NothingToResolve_LeavesItNone()
    {
        var invoice = Invoice(SalesInvoiceStatus.Open, null);

        invoice.CapturePaymentInstructionsIfIssued(null);

        Assert.Null(invoice.PaymentInstructions);
        Assert.Null(BankPaymentInstructions.ForPresentation(true, invoice.PaymentInstructions, Settings(), Bank(), PayeeName, InvoiceNo));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(SalesInvoiceStatus.Draft)]
    [InlineData(SalesInvoiceStatus.Void)]
    public void Capture_DoesNothingWhileDraftOrVoid(SalesInvoiceStatus? status)
    {
        var invoice = Invoice(status, null);

        invoice.CapturePaymentInstructionsIfIssued("Pay by bank transfer to:");

        Assert.Null(invoice.PaymentInstructions);
    }

    [Fact]
    public void IssuedInvoice_IsUnaffectedWhenThePaymentBankChangesAfterIssue()
    {
        var invoice = Invoice(SalesInvoiceStatus.Open, null);
        invoice.CapturePaymentInstructionsIfIssued(
            BankPaymentInstructions.Resolve(invoice.PaymentInstructions, Settings(), Bank(), PayeeName, InvoiceNo));
        var printedAtIssue = invoice.PaymentInstructions;

        var laterBank = Bank();
        laterBank.Number = "999-999-999";
        var printedLater = BankPaymentInstructions.ForPresentation(
            true, invoice.PaymentInstructions, Settings(), laterBank, PayeeName, InvoiceNo);

        Assert.Equal(printedAtIssue, printedLater);
        Assert.DoesNotContain("999-999-999", printedLater);
    }
}
