using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dto.Sales;

namespace Api.Service;

/// <summary>Fingerprints invoice and recipient intent without delivery history, generated links or PDF serialization noise.</summary>
public static class InvoiceEmailIntent
{
    public static string Fingerprint(SalesInvoice invoice, bool followUp, bool attachPdf, EmailMessage message)
    {
        var content = JsonSerializer.SerializeToNode(invoice)!.AsObject();
        content.Remove(nameof(SalesInvoice.Emails));
        var json = JsonSerializer.Serialize(new
        {
            Invoice = content, FollowUp = followUp, AttachPdf = attachPdf,
            message.To, message.AdditionalTo, message.Cc, message.Bcc, message.ReplyTo
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }
}
