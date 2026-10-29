using System;
using System.Linq;
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

        private static readonly JsonSerializerOptions PayloadOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

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

        public async Task<string?> SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            var apiKey = _configuration["RESEND_API_KEY"]
                         ?? _configuration["Email:Resend:ApiKey"]
                         ?? Environment.GetEnvironmentVariable("RESEND_API_KEY");

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogWarning("Resend API key is missing. Email to {To} will not be dispatched.", message.To);
                throw new InvalidOperationException("Resend API key is not configured.");
            }

            var fromEmail = message.FromEmail
                            ?? _configuration["RESEND_EMAIL_FROM"]
                            ?? _configuration["Email:FromAddress"]
                            ?? "billing@accountgo.dev";

            var fromName = SafeDisplayName(message.FromName)
                           ?? (string.IsNullOrWhiteSpace(message.FromEmail)
                                ? (_configuration["RESEND_FROM_NAME"] ?? _configuration["Email:FromName"] ?? "AccountGo")
                                : null);

            var formattedFrom = string.IsNullOrWhiteSpace(fromName) ? fromEmail : $"{fromName} <{fromEmail}>";

            var defaultReplyTo = _configuration["SUPPORT_EMAIL"]
                                 ?? _configuration["Email:SupportAddress"]
                                 ?? "support@accountgo.dev";
            if (string.IsNullOrWhiteSpace(message.ReplyTo) && !string.IsNullOrWhiteSpace(defaultReplyTo))
            {
                message.ReplyTo = defaultReplyTo;
            }

            return await DispatchEmailAsync(apiKey, formattedFrom, message, ct);
        }

        /// <summary>
        /// A display name is user-supplied (a business name), so drop anything that would let it break out of the
        /// From header: quotes, angle brackets, commas, and line breaks.
        /// </summary>
        public static string? SafeDisplayName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            var cleaned = new string(name.Where(c => !char.IsControl(c) && c is not ('"' or '<' or '>' or ',' or ';')).ToArray()).Trim();
            if (cleaned.Length > 70)
                cleaned = cleaned[..70].TrimEnd();
            return cleaned.Length == 0 ? null : $"\"{cleaned}\"";
        }

        public static string BuildPayload(string from, EmailMessage message)
        {
            var recipients = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(message.To))
                recipients.Add(message.To);
            if (message.AdditionalTo != null)
            {
                foreach (var extra in message.AdditionalTo)
                {
                    if (!string.IsNullOrWhiteSpace(extra) && !recipients.Contains(extra, StringComparer.OrdinalIgnoreCase))
                        recipients.Add(extra);
                }
            }

            return JsonSerializer.Serialize(new ResendEmailRequest
            {
                From = from,
                To = recipients.ToArray(),
                Bcc = message.Bcc.Count > 0 ? message.Bcc.ToArray() : null,
                ReplyTo = string.IsNullOrWhiteSpace(message.ReplyTo) ? null : message.ReplyTo,
                Subject = message.Subject,
                Html = message.Html,
                Text = message.Text,
                Attachments = message.Attachments.Count > 0
                    ? message.Attachments.Select(a => new ResendAttachment { Filename = a.FileName, Content = Convert.ToBase64String(a.Content) }).ToArray()
                    : null
            }, PayloadOptions);
        }

        private async Task<string> DispatchEmailAsync(
            string apiKey,
            string from,
            EmailMessage message,
            CancellationToken ct)
        {
            HttpResponseMessage response;
            string responseContent;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, ResendApiEndpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                request.Content = new StringContent(BuildPayload(from, message), Encoding.UTF8, "application/json");

                response = await _httpClient.SendAsync(request, ct);
                responseContent = await response.Content.ReadAsStringAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception encountered while dispatching email via Resend to {To}", message.To);
                throw new EmailDeliveryException($"The email service could not be reached ({ex.GetType().Name}).", ex);
            }

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<ResendSuccessResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                _logger.LogInformation("✓ Email dispatched successfully via Resend. ID: {EmailId}, To: {To}, Subject: '{Subject}', Attachments: {AttachmentCount}",
                    result?.Id, message.To, message.Subject, message.Attachments.Count);
                return result?.Id ?? string.Empty;
            }

            _logger.LogWarning("Resend API rejected email dispatch. StatusCode: {StatusCode}, Response: {Response}",
                response.StatusCode, responseContent);

            if (responseContent.Contains("domain is not verified", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("To send from '{From}', add and verify your domain at https://resend.com/domains.", from);
            }

            throw new EmailDeliveryException($"The email service refused it: {ProviderReason(responseContent, (int)response.StatusCode)}");
        }

        /// <summary>Resend's own explanation ({"message": "..."}), or the status code when the body isn't one.</summary>
        public static string ProviderReason(string responseContent, int statusCode)
        {
            try
            {
                using var json = JsonDocument.Parse(responseContent);
                if (json.RootElement.ValueKind == JsonValueKind.Object
                    && json.RootElement.TryGetProperty("message", out var message)
                    && message.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(message.GetString()))
                {
                    var text = message.GetString()!.Trim();
                    return text.Length > 300 ? text[..300] : text;
                }
            }
            catch (JsonException)
            {
            }
            return $"HTTP {statusCode}";
        }

        private class ResendEmailRequest
        {
            public string From { get; set; } = string.Empty;
            public string[] To { get; set; } = Array.Empty<string>();
            public string[]? Bcc { get; set; }
            public string? ReplyTo { get; set; }
            public string Subject { get; set; } = string.Empty;
            public string Html { get; set; } = string.Empty;
            public string? Text { get; set; }
            public ResendAttachment[]? Attachments { get; set; }
        }

        private class ResendAttachment
        {
            public string Filename { get; set; } = string.Empty;
            public string Content { get; set; } = string.Empty;
        }

        private class ResendSuccessResponse
        {
            public string? Id { get; set; }
        }
    }
}
