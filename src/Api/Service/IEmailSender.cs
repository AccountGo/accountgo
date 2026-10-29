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
        public string To { get; set; } = string.Empty;
        public List<string> AdditionalTo { get; set; } = new();
        public List<string> Cc { get; set; } = new();
        public string Subject { get; set; } = string.Empty;
        public string Html { get; set; } = string.Empty;
        /// <summary>Plain-text alternative for clients that don't render HTML.</summary>
        public string? Text { get; set; }
        /// <summary>Display name shown before the configured sending address; the address itself never changes.</summary>
        public string? FromName { get; set; }
        /// <summary>Specific sender email address (e.g. auth@accountgo.dev, invites@accountgo.dev). Falls back to default sending address if not set.</summary>
        public string? FromEmail { get; set; }
        public string? ReplyTo { get; set; }
        public List<string> Bcc { get; set; } = new();
        public List<EmailAttachment> Attachments { get; set; } = new();
    }

    /// <summary>The provider did not take the message; the message says why, in words safe to show the sender.</summary>
    public class EmailDeliveryException : HttpRequestException
    {
        public EmailDeliveryException(string message, Exception? inner = null) : base(message, inner) { }
    }

    public class EmailAttachment
    {
        public string FileName { get; set; } = string.Empty;
        public byte[] Content { get; set; } = Array.Empty<byte>();
    }
}
