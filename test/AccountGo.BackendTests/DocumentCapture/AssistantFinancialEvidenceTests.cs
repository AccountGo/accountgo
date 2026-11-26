// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Text.Json;
using Api.DocumentCaptures.Parsing;
using Xunit;

namespace AccountGo.BackendTests.DocumentCapture;

/// <summary>Rule A-02: assistant attachments need positive financial or bank evidence before fields are read. Invented data.</summary>
public sealed class AssistantFinancialEvidenceTests
{
    private static string[] ParseAssistantLabels(params string[] lines)
    {
        var textLines = lines.Select((text, index) => new TextLine(1, text, 0, index * 20, 400, index * 20 + 16, TextLineSource.PdfTextLayer)).ToList();
        var parsed = DocumentTextFieldParser.Parse("assistant-attachment", textLines);
        using var doc = JsonDocument.Parse(parsed.FieldsJson);
        return doc.RootElement.GetProperty("fields").EnumerateArray()
            .Select(field => field.GetProperty("label").GetString()!)
            .ToArray();
    }

    [Fact]
    [Trait("Rule", "A-02")]
    public void ApiSchemaWithFinancialWordsIsNotFinancial()
    {
        string[] lines = ["Response schema", "total: integer", "tax_rate: number", "invoice_id: string", "created_date: string", "Send a request."];

        var evidence = AssistantFinancialEvidence.Assess(lines);

        Assert.False(evidence.Financial);
        Assert.False(evidence.Bank);
        Assert.Equal(0, evidence.FinancialSignals);
        Assert.Empty(ParseAssistantLabels(lines));
    }

    [Fact]
    [Trait("Rule", "A-02")]
    public void ProseWithAColonGetsNoFields()
    {
        Assert.Empty(ParseAssistantLabels("Meeting notes", "Agenda: review the roadmap", "Note: bring laptops", "Owner: Sam"));
    }

    [Theory]
    [Trait("Rule", "A-02")]
    [InlineData("INVOICE", "Invoice Number LPW-2048", "Total $93.50")]
    [InlineData("Receipt #: S02-6665", "Date: 21/11/2026", "TOTAL: $42.50")]
    [InlineData("Invoice Date March 4, 2026", "Amount Due 1,250.00")]
    [InlineData("Tax Invoice", "Sub Total 85.00")]
    public void InvoicesAndReceiptsAreFinancial(params string[] lines)
    {
        var evidence = AssistantFinancialEvidence.Assess(lines);

        Assert.True(evidence.Financial, string.Join(" | ", lines));
        Assert.NotEmpty(ParseAssistantLabels(lines));
    }

    [Theory]
    [Trait("Rule", "A-02")]
    [InlineData("Total $93.50")]
    [InlineData("INVOICE")]
    [InlineData("Order #: 7741", "Shipped in two boxes")]
    [InlineData("Date: 2026-03-04", "Release notes")]
    public void OneSignalAloneIsNotEnough(params string[] lines)
    {
        var evidence = AssistantFinancialEvidence.Assess(lines);

        Assert.False(evidence.Financial);
        Assert.Empty(ParseAssistantLabels(lines));
    }

    [Theory]
    [Trait("Rule", "A-02")]
    [InlineData("Account Number 12345678", "Sort code 40-12-76")]
    [InlineData("IBAN: DE89 3704 0044 0532 0130 00")]
    [InlineData("Routing number 021000021", "Opening balance 1,000.00")]
    public void BankDetailsAreBankEvidence(params string[] lines)
    {
        var evidence = AssistantFinancialEvidence.Assess(lines);

        Assert.True(evidence.Bank, string.Join(" | ", lines));
    }

    [Theory]
    [Trait("Rule", "A-02")]
    [InlineData("iban: string", "account_number: string")]
    [InlineData("Account settings", "Update your account number in Settings.")]
    public void BankWordsWithoutNumbersAreNotBankEvidence(params string[] lines)
    {
        Assert.False(AssistantFinancialEvidence.Assess(lines).Bank);
    }

    [Fact]
    [Trait("Rule", "A-02")]
    public void StatementWithBankDetailsStaysBankEvenWithAFinancialTitle()
    {
        var evidence = AssistantFinancialEvidence.Assess(["Statement", "Account Number 12345678", "Date: 2026-03-01", "Closing balance 950.00"]);

        Assert.True(evidence.Bank);
        Assert.False(evidence.Financial);
    }
}
