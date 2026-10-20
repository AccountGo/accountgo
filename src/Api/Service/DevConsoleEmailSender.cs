using System;
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

        public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
        {
            var border = new string('=', 65);
            var logMessage = $"\n{border}\n[EMAIL DISPATCH - DEV CONSOLE]\nTo:      {to}\nSubject: {subject}\nTime:    {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC\n\n{htmlBody}\n{border}\n";

            Console.WriteLine(logMessage);
            _logger.LogInformation("Email dispatched to {To} with subject '{Subject}'", to, subject);

            return Task.CompletedTask;
        }
    }
}
