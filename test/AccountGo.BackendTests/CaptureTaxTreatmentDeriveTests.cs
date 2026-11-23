// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Api.Service;
using Core.Domain;
using Xunit;

namespace AccountGo.BackendTests;

public sealed class CaptureTaxTreatmentDeriveTests
{
    private const string WithTax =
        """{"sourceTotals":{"subtotal":"407.47","taxTotal":"33.62","grandTotal":"441.09"}}""";
    private const string NoTax =
        """{"sourceTotals":{"subtotal":"100.00","taxTotal":"0.00","grandTotal":"100.00"}}""";

    [Fact]
    [Trait("Rule", "T-10")]
    [Trait("Rule", "T-12")]
    public void Derive_EveryLineWithTheSameTax_IsThatTaxRate()
    {
        Assert.Equal("tax-rate:tax_01a", CaptureTaxTreatment.Derive(WithTax, "bill", ["tax_01a", "tax_01a"], 441.09m, TaxRegime.VatGst));
        Assert.Equal("tax-rate:tax_01a", CaptureTaxTreatment.Derive(WithTax, "invoice", ["tax_01a"], 441.09m, TaxRegime.SalesTax));
    }

    [Fact]
    [Trait("Rule", "T-10")]
    public void Derive_LinesWithDifferentOrPartialTaxes_AreMixedRates()
    {
        Assert.Equal("tax-rate:mixed", CaptureTaxTreatment.Derive(WithTax, "bill", ["tax_01a", "tax_01b"], 441.09m, TaxRegime.VatGst));
        Assert.Equal("tax-rate:mixed", CaptureTaxTreatment.Derive(WithTax, "invoice", ["tax_01a", null], 441.09m, TaxRegime.VatGst));
    }

    [Fact]
    [Trait("Rule", "T-10")]
    public void Derive_BillWithNoLineTaxAndTheDocumentTotal_IsInCost()
    {
        Assert.Equal("in-cost", CaptureTaxTreatment.Derive(WithTax, "bill", [null, null], 441.09m, TaxRegime.SalesTax));
        Assert.Equal("in-cost", CaptureTaxTreatment.Derive(WithTax, "bill", [null], 441.08m, TaxRegime.None));
    }

    [Fact]
    [Trait("Rule", "T-10")]
    public void Derive_BillWithNoLineTaxAndAShortTotal_IsNotDerivedYet()
    {
        Assert.Null(CaptureTaxTreatment.Derive(WithTax, "bill", [null, null], 407.47m, TaxRegime.SalesTax));
    }

    [Fact]
    [Trait("Rule", "T-11")]
    public void Derive_InvoiceInAWorkspaceWithoutTaxShortByTheDocumentTax_IsAcknowledged()
    {
        Assert.Equal("acknowledged", CaptureTaxTreatment.Derive(WithTax, "invoice", [null, null], 407.47m, TaxRegime.None));
        // Under a tax regime the same shortfall is never an accepted treatment, and an invoice is never "in cost".
        Assert.Null(CaptureTaxTreatment.Derive(WithTax, "invoice", [null, null], 407.47m, TaxRegime.VatGst));
        Assert.Null(CaptureTaxTreatment.Derive(WithTax, "invoice", [null, null], 441.09m, TaxRegime.None));
    }

    [Fact]
    [Trait("Rule", "T-10")]
    public void Derive_DocumentWithoutTax_IsNone()
    {
        Assert.Equal("none", CaptureTaxTreatment.Derive(NoTax, "bill", [null], 100m, TaxRegime.None));
        Assert.Equal("none", CaptureTaxTreatment.Derive("""{"sourceTotals":{"grandTotal":"100.00"}}""", "invoice", ["tax_01a"], 107m, TaxRegime.VatGst));
        Assert.Equal("none", CaptureTaxTreatment.Derive(null, "invoice", [null], 100m, TaxRegime.None));
    }
}
