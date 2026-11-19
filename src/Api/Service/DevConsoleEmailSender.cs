using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Api.Observability;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Api.Service
{
    public class DevConsoleEmailSender : IEmailSender
    {
        private readonly ILogger<DevConsoleEmailSender> _logger;
        private readonly IHostEnvironment _environment;

        public DevConsoleEmailSender(ILogger<DevConsoleEmailSender> logger, IHostEnvironment environment)
        {
            _logger = logger;
            _environment = environment;
        }

        public Task<string?> SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            // Attachments are written to a temp folder so they can be opened while developing.
            var saved = message.Attachments.Select(attachment =>
            {
                var folder = Path.Combine(Path.GetTempPath(), "accountgo-mail");
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, $"{DateTime.UtcNow:yyyyMMddHHmmss}-{Path.GetFileName(attachment.FileName)}");
                File.WriteAllBytes(path, attachment.Content);
                return path;
            }).ToList();

            var redactedTo = LogRedaction.RedactEmail(message.To);
            _logger.LogInformation(
                "Email dispatched to {To} with {AttachmentCount} attachment(s)",
                redactedTo,
                message.Attachments.Count);

            // Full body (OTP codes, invite URLs) only on the developer console — never in Production.
            if (_environment.IsDevelopment())
            {
                var allTo = new System.Collections.Generic.List<string> { message.To };
                if (message.AdditionalTo != null && message.AdditionalTo.Count > 0)
                    allTo.AddRange(message.AdditionalTo);

                var body = LogRedaction.RedactInviteTokens(message.Text ?? message.Html);
                var border = new string('=', 65);
                var logMessage = $"\n{border}\n[EMAIL DISPATCH - DEV CONSOLE]\nFrom:    {message.FromName ?? "AccountGo"}\nTo:      {string.Join(", ", allTo)}\n" +
                    (message.Cc.Count > 0 ? $"Cc:      {string.Join(", ", message.Cc)}\n" : "") +
                    (message.Bcc.Count > 0 ? $"Bcc:     {string.Join(", ", message.Bcc)}\n" : "") +
                    (string.IsNullOrWhiteSpace(message.ReplyTo) ? "" : $"ReplyTo: {message.ReplyTo}\n") +
                    $"Subject: {message.Subject}\nTime:    {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC\n" +
                    string.Concat(saved.Select(path => $"Attach:  {path}\n")) +
                    $"\n{body}\n{border}\n";

                Console.WriteLine(logMessage);
            }

            return Task.FromResult<string?>($"dev-{Guid.NewGuid():N}");
        }
    }
}
