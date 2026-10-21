using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Api.Service
{
    public class ResendEmailSender : IEmailSender
    {
        private const string ResendApiEndpoint = "https://api.resend.com/emails";
        private const string SandboxSender = "AccountGo <onboarding@resend.dev>";

        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ResendEmailSender> _logger;

        public ResendEmailSender(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<ResendEmailSender> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
        {
            var apiKey = _configuration["RESEND_API_KEY"] 
                         ?? _configuration["Email:Resend:ApiKey"] 
                         ?? Environment.GetEnvironmentVariable("RESEND_API_KEY");

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogWarning("Resend API key is missing. Email to {To} will not be dispatched.", to);
                return;
            }

            var fromEmail = _configuration["RESEND_EMAIL_FROM"] 
                            ?? _configuration["Email:FromAddress"] 
                            ?? "noreply@marvinperez.com";

            var fromName = _configuration["RESEND_FROM_NAME"] 
                           ?? _configuration["Email:FromName"] 
                           ?? "AccountGo";

            var formattedFrom = string.IsNullOrWhiteSpace(fromName) ? fromEmail : $"{fromName} <{fromEmail}>";

            var success = await DispatchEmailAsync(apiKey, formattedFrom, to, subject, htmlBody, ct);

            // If the primary send failed because the custom domain is not yet verified on Resend,
            // check if the recipient is the verified account owner and retry via the sandbox sender.
            if (!success && (to.Equals("marvinperez@gmail.com", StringComparison.OrdinalIgnoreCase) || 
                             to.Equals("hello@marvinperez.com", StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogInformation("Attempting fallback dispatch to verified account owner via Resend sandbox '{SandboxSender}'...", SandboxSender);
                await DispatchEmailAsync(apiKey, SandboxSender, "marvinperez@gmail.com", subject, htmlBody, ct);
            }
        }

        private async Task<bool> DispatchEmailAsync(
            string apiKey, 
            string from, 
            string to, 
            string subject, 
            string htmlBody, 
            CancellationToken ct)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, ResendApiEndpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                var payload = new ResendEmailRequest
                {
                    From = from,
                    To = new[] { to },
                    Subject = subject,
                    Html = htmlBody
                };

                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                });

                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request, ct);
                var responseContent = await response.Content.ReadAsStringAsync(ct);

                if (response.IsSuccessStatusCode)
                {
                    var result = JsonSerializer.Deserialize<ResendSuccessResponse>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    _logger.LogInformation("✓ Email dispatched successfully via Resend. ID: {EmailId}, To: {To}, Subject: '{Subject}'", 
                        result?.Id, to, subject);
                    return true;
                }

                _logger.LogWarning("Resend API rejected email dispatch. StatusCode: {StatusCode}, Response: {Response}", 
                    response.StatusCode, responseContent);

                if (responseContent.Contains("domain is not verified", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("To send from '{From}', add and verify your domain at https://resend.com/domains.", from);
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception encountered while dispatching email via Resend to {To}", to);
                return false;
            }
        }

        private class ResendEmailRequest
        {
            public string From { get; set; } = string.Empty;
            public string[] To { get; set; } = Array.Empty<string>();
            public string Subject { get; set; } = string.Empty;
            public string Html { get; set; } = string.Empty;
        }

        private class ResendSuccessResponse
        {
            public string? Id { get; set; }
        }
    }
}
