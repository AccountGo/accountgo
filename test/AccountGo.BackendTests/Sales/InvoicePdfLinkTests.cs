using System;
using Api.Service;
using Xunit;

namespace AccountGo.BackendTests.Sales;

public class InvoicePdfLinkTests
{
    private const string Secret = "0123456789abcdef0123456789abcdef"; // 32 bytes
    private const string ApiBase = "https://api.example.com";

    [Fact]
    public void Create_RoundTrips_AndBindsEmailCompanyInvoiceAndExpiry()
    {
        var expires = DateTimeOffset.UtcNow.AddDays(7);
        var url = InvoicePdfLinkSigner.CreateUrl(
            ApiBase, companyId: 3, invoiceId: 42, email: "Marvin.Perez@Letshyke.com", expiresAt: expires, secret: Secret);

        Assert.StartsWith("https://api.example.com/api/public/invoices/pdf?", url, StringComparison.Ordinal);

        Assert.True(InvoicePdfLinkSigner.TryValidate(
            Query(url, "c"), Query(url, "i"), Query(url, "e"), Query(url, "exp"), Query(url, "sig"),
            Secret, DateTimeOffset.UtcNow, out var claims, out var error));

        Assert.Null(error);
        Assert.Equal(3, claims!.CompanyId);
        Assert.Equal(42, claims.InvoiceId);
        Assert.Equal("marvin.perez@letshyke.com", claims.Email);
        Assert.Equal(expires.ToUnixTimeSeconds(), claims.ExpiresAt.ToUnixTimeSeconds());
    }

    [Fact]
    public void TryValidate_RejectsTamperedInvoiceId()
    {
        var url = InvoicePdfLinkSigner.CreateUrl(
            ApiBase, 1, 10, "a@b.com", DateTimeOffset.UtcNow.AddDays(1), Secret);

        Assert.False(InvoicePdfLinkSigner.TryValidate(
            Query(url, "c"), "999", Query(url, "e"), Query(url, "exp"), Query(url, "sig"),
            Secret, DateTimeOffset.UtcNow, out _, out var error));
        Assert.Equal("invalid", error);
    }

    [Fact]
    public void TryValidate_RejectsWrongEmail()
    {
        var url = InvoicePdfLinkSigner.CreateUrl(
            ApiBase, 1, 10, "a@b.com", DateTimeOffset.UtcNow.AddDays(1), Secret);

        Assert.False(InvoicePdfLinkSigner.TryValidate(
            Query(url, "c"), Query(url, "i"), "other@b.com", Query(url, "exp"), Query(url, "sig"),
            Secret, DateTimeOffset.UtcNow, out _, out var error));
        Assert.Equal("invalid", error);
    }

    [Fact]
    public void TryValidate_RejectsExpiredLink()
    {
        var expires = DateTimeOffset.UtcNow.AddMinutes(-1);
        var url = InvoicePdfLinkSigner.CreateUrl(ApiBase, 1, 10, "a@b.com", expires, Secret);

        Assert.False(InvoicePdfLinkSigner.TryValidate(
            Query(url, "c"), Query(url, "i"), Query(url, "e"), Query(url, "exp"), Query(url, "sig"),
            Secret, DateTimeOffset.UtcNow, out _, out var error));
        Assert.Equal("expired", error);
    }

    [Fact]
    public void Lifetime_IsSevenDays()
    {
        Assert.Equal(TimeSpan.FromDays(7), InvoicePdfLinkSigner.Lifetime);
    }

    private static string Query(string url, string key)
    {
        var query = new Uri(url).Query.TrimStart('?');
        foreach (var part in query.Split('&'))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 && pair[0] == key)
                return Uri.UnescapeDataString(pair[1]);
        }
        throw new InvalidOperationException($"Missing {key}");
    }
}
