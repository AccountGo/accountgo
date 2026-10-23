using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Api.Service
{
    public class DevConsoleEmailSender : IEmailSender
    {
        private readonly ILogger<DevConsoleEmailSender> _logger;

        public DevConsoleEmailSender(ILogger<DevConsoleEmailSender> logger)
        {
            _logger = logger;
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

            var border = new string('=', 65);
            var logMessage = $"\n{border}\n[EMAIL DISPATCH - DEV CONSOLE]\nFrom:    {message.FromName ?? "AccountGo"}\nTo:      {message.To}\n" +
                (message.Bcc.Count > 0 ? $"Bcc:     {string.Join(", ", message.Bcc)}\n" : "") +
                (string.IsNullOrWhiteSpace(message.ReplyTo) ? "" : $"ReplyTo: {message.ReplyTo}\n") +
                $"Subject: {message.Subject}\nTime:    {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC\n" +
                string.Concat(saved.Select(path => $"Attach:  {path}\n")) +
                $"\n{message.Text ?? message.Html}\n{border}\n";

            Console.WriteLine(logMessage);
            _logger.LogInformation("Email dispatched to {To} with subject '{Subject}'", message.To, message.Subject);

            return Task.FromResult<string?>($"dev-{Guid.NewGuid():N}");
        }
    }
}
