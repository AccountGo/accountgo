using Api.Service;
using Dto.Sales;
using QuestPDF.Infrastructure;
using UglyToad.PdfPig;
using Xunit;

namespace AccountGo.BackendTests.Sales;

public class InvoicePdfAppearanceTests
{
    static InvoicePdfAppearanceTests() => global::QuestPDF.Settings.License = LicenseType.Community;

    [Fact]
    public void DefaultPalette_IsReadable()
    {
        Assert.True(InvoicePdfPalette.TryNormalize(InvoicePdfPalette.Default, out var palette, out var error), error);
        Assert.Equal(InvoicePdfPalette.Default, palette);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#FFF")]
    [InlineData("#05966980")]
    [InlineData("expression(alert(1))")]
    [InlineData("#GGGGGG")]
    public void Palette_RejectsAnythingOtherThanRgbHex(string color)
    {
        var candidate = InvoicePdfPalette.Default with { Text = color };
        Assert.False(InvoicePdfPalette.TryNormalize(candidate, out _, out var error));
        Assert.Contains("#RRGGBB", error);
    }

    [Fact]
    public void Palette_RejectsAccentThatDisappearsOnTheTotalsSurface()
    {
        var candidate = InvoicePdfPalette.Default with { Surface = "#D7D7D7", Accent = "#929292" };
        Assert.False(InvoicePdfPalette.TryNormalize(candidate, out _, out var error));
        Assert.Contains("shaded totals", error);
    }

    [Fact]
    public void Palette_RejectsSuccessThatDisappearsOnTheTotalsSurface()
    {
        var candidate = InvoicePdfPalette.Default with { Success = "#929292" };
        Assert.False(InvoicePdfPalette.TryNormalize(candidate, out _, out var error));
        Assert.Contains("shaded totals", error);
    }

    [Fact]
    public void Palette_RejectsWhiteTextOnWhitePage()
    {
        var candidate = InvoicePdfPalette.Default with { Text = "#FFFFFF" };
        Assert.False(InvoicePdfPalette.TryNormalize(candidate, out _, out var error));
        Assert.Contains("readable", error);
    }

    [Fact]
    public void DefaultPdf_ShowsCenteredAttributionOnEveryPage_AndKeepsSellerAndTotals()
    {
        var pdf = Render(Sample(lines: 48, seller: "Northwind Trading Company"), InvoicePdfAppearance.Default, "USD");
        using var document = PdfDocument.Open(pdf);
        Assert.True(document.NumberOfPages >= 2);
        Assert.Equal("AccountGo", document.Information.Creator);
        var all = string.Join("\n", document.GetPages().Select(page => page.Text));
        Assert.Contains("$120.00", all);
        foreach (var page in document.GetPages())
        {
            Assert.Contains("Powered by AccountGo", page.Text);
            Assert.Contains("Northwind Trading Company", page.Text);
            Assert.Contains("Page", page.Text);
            AssertCentered(page);
        }
    }

    [Fact]
    public void HiddenAttribution_RemovesOnlyTheBrandingLine()
    {
        var appearance = InvoicePdfAppearance.Default with { ShowAccountGoBranding = false };
        var pdf = Render(Sample(lines: 4, seller: "Northwind Trading Company"), appearance, "EUR");
        using var document = PdfDocument.Open(pdf);
        var text = string.Join("\n", document.GetPages().Select(page => page.Text));
        Assert.DoesNotContain("Powered by", text);
        Assert.Contains("Northwind Trading Company", text);
        Assert.Contains("Page", text);
        Assert.Contains("€120.00", text);
        Assert.Contains("NOTES", text);
        Assert.Equal("AccountGo", document.Information.Creator);
        Assert.Equal("Northwind Trading Company", document.Information.Author);
    }

    [Fact]
    public void LongSellerName_StillCentersAttributionOnItsOwnLine()
    {
        var seller = new string('A', 80) + " Supplies";
        var pdf = Render(Sample(lines: 2, seller: seller), InvoicePdfAppearance.Default, "USD");
        using var document = PdfDocument.Open(pdf);
        var page = document.GetPage(1);
        Assert.Contains(seller, page.Text);
        AssertCentered(page);
    }

    [Fact]
    public async Task Renderer_UsesProviderAppearance()
    {
        var provider = new FixedAppearance(InvoicePdfAppearance.Default with { ShowAccountGoBranding = false });
        var renderer = new InvoicePdfRenderer(provider);
        var rendered = await renderer.RenderAsync(Sample(2, "Acme"), "USD");
        using var document = PdfDocument.Open(rendered.Pdf);
        Assert.DoesNotContain("Powered by", document.GetPage(1).Text);
        Assert.True(InvoicePdfAppearanceStamp.TryParse(rendered.AppearanceStamp)!.HideAccountGoBranding);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public void Fingerprint_WithoutLogo_PreservesExpectedBehavior()
    {
        var appearance1 = InvoicePdfAppearance.Default;
        var appearance2 = InvoicePdfAppearance.Default with { Logo = null };
        Assert.Equal(appearance1.Fingerprint(), appearance2.Fingerprint());
        Assert.False(appearance1.RequiresEntitlement);
    }

    [Fact]
    public void Fingerprint_WithLogo_IncludesLogoHash_AndDoesNotRequireEntitlement()
    {
        var logo = new InvoicePdfLogo(
            Bytes: Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="),
            Hash: "abc123hash",
            Width: 1,
            Height: 1);

        var appearance = InvoicePdfAppearance.Default with { Logo = logo };
        Assert.NotEqual(InvoicePdfAppearance.Default.Fingerprint(), appearance.Fingerprint());
        // Universal logo: logo alone does not require entitlement
        Assert.False(appearance.RequiresEntitlement);

        var stamp = InvoicePdfAppearanceStamp.From(appearance);
        Assert.Equal("abc123hash", stamp.LogoHash);
        Assert.False(stamp.RequiresEntitlement);

        var json = InvoicePdfAppearanceStamp.Serialize(appearance);
        var parsed = InvoicePdfAppearanceStamp.TryParse(json);
        Assert.NotNull(parsed);
        Assert.Equal(stamp.Hash, parsed.Hash);
        Assert.Equal("abc123hash", parsed.LogoHash);
    }

    [Fact]
    public void Stamp_WithoutLogoInJson_ParsesWithNullLogoHash()
    {
        var legacyJson = "{\"Hash\":\"somehash\",\"HideAccountGoBranding\":false,\"RequiresEntitlement\":false,\"SettingsRevision\":2}";
        var parsed = InvoicePdfAppearanceStamp.TryParse(legacyJson);
        Assert.NotNull(parsed);
        Assert.Equal("somehash", parsed.Hash);
        Assert.Null(parsed.LogoHash);
        Assert.False(parsed.RequiresEntitlement);
    }

    [Fact]
    public void InvoicePdfDocument_WithLogo_RendersPdfSuccessfully()
    {
        var logoBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        var logo = new InvoicePdfLogo(logoBytes, "test-hash", 1, 1);
        var appearance = InvoicePdfAppearance.Default with { Logo = logo };

        var pdf = Render(Sample(lines: 3, seller: "Acme Corp"), appearance, "USD");
        Assert.NotNull(pdf);
        Assert.True(pdf.Length > 0);

        using var document = PdfDocument.Open(pdf);
        Assert.Equal(1, document.NumberOfPages);
        var pageText = document.GetPage(1).Text;
        Assert.Contains("Acme Corp", pageText);
        Assert.Contains("Powered by AccountGo", pageText);
    }

    private static void AssertCentered(UglyToad.PdfPig.Content.Page page)
    {
        var words = page.GetWords().ToList();
        var powered = words.First(word => word.Text == "Powered");
        var account = words.First(word => word.Text == "AccountGo");
        var midpoint = (powered.BoundingBox.Left + account.BoundingBox.Right) / 2;
        Assert.InRange(midpoint, page.Width * 0.35, page.Width * 0.65);
        var seller = words.First(word => word.Text.Contains("Northwind") || word.Text.StartsWith("AAAA"));
        Assert.True(powered.BoundingBox.Top < seller.BoundingBox.Top || powered.BoundingBox.Bottom < seller.BoundingBox.Bottom);
    }

    private static byte[] Render(SalesInvoice invoice, InvoicePdfAppearance appearance, string currency) =>
        InvoicePdfRenderer.Render(invoice, currency, appearance).Pdf;

    private static SalesInvoice Sample(int lines, string seller) => new()
    {
        No = "INV-0042",
        CustomerName = "Customer",
        InvoiceDate = new DateTime(2026, 3, 1),
        DueDate = new DateTime(2026, 3, 31),
        Status = "Overdue",
        TotalAmountAfterTax = 120m,
        BalanceDue = 120m,
        Notes = "Please pay from the invoice notice.",
        Display = new DisplayFlags { ShowNotes = true },
        Seller = new SellerInfo { Name = seller, AddressLines = ["1 Market Street"] },
        SalesInvoiceLines = Enumerable.Range(1, lines).Select(index => new SalesInvoiceLine
        {
            ItemDescription = "Consulting engagement line " + index,
            Quantity = 1,
            Amount = 120m,
            LineTotal = 120m
        }).ToList()
    };

    private sealed class FixedAppearance(InvoicePdfAppearance appearance) : IInvoicePdfAppearanceProvider
    {
        public int Calls { get; private set; }
        public Task<InvoicePdfAppearance> GetEffectiveAsync(CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(appearance);
        }
    }
}
