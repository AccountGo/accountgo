using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Api.Service
{
    public interface IEmailSender
    {
        /// <summary>Sends the message and returns the provider's id for it, when the provider gives one.</summary>
        Task<string?> SendAsync(EmailMessage message, CancellationToken ct = default);

        Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default) =>
            SendAsync(new EmailMessage { To = to, Subject = subject, Html = htmlBody }, ct);
    }

    public class EmailMessage
    {
        /// <summary>Stable provider key for retries of one immutable dispatch.</summary>
        public string? IdempotencyKey { get; set; }
        /// <summary>Server-derived invoice and request fingerprint, excluding regenerated links and PDF bytes.</summary>
        public string? IntentFingerprint { get; set; }
        /// <summary>Appearance frozen with a custom-email attachment. Retries keep the admitted value.</summary>
        public string? AppearanceStamp { get; set; }
        public string To { get; set; } = string.Empty;
        public List<string> AdditionalTo { get; set; } = new();
        public List<string> Cc { get; set; } = new();
        public string Subject { get; set; } = string.Empty;
        public string Html { get; set; } = string.Empty;
        /// <summary>Plain-text alternative for clients that don't render HTML.</summary>
        public string? Text { get; set; }
        /// <summary>Display name shown with the server-resolved sending address.</summary>
        public string? FromName { get; set; }
        /// <summary>Specific sender email address (e.g. auth@accountgo.dev, invites@accountgo.dev). Falls back to default sending address if not set.</summary>
        public string? FromEmail { get; set; }
        public string? ReplyTo { get; set; }
        public List<string> Bcc { get; set; } = new();
        public List<EmailAttachment> Attachments { get; set; } = new();
    }

    /// <summary>A delivery failure with safe diagnostics and acceptance certainty when the provider establishes it.</summary>
    public class EmailDeliveryException : HttpRequestException
    {
        public EmailDeliveryException(string message, Exception? inner = null, bool acceptanceUnknown = true) : base(message, inner)
        { AcceptanceUnknown = acceptanceUnknown; }
        /// <summary>True when transport failure may have occurred after provider acceptance.</summary>
        public bool AcceptanceUnknown { get; }
    }

    public class EmailAttachment
    {
        public string FileName { get; set; } = string.Empty;
        public byte[] Content { get; set; } = Array.Empty<byte>();
    }
}
