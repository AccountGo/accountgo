using System;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Api.Data;
using Core.Domain;
using Microsoft.Extensions.Configuration;

namespace Api.Service
{
    /// <summary>Invitation tokens are generated only for delivery, never persisted in plaintext.</summary>
    public class WorkspaceInvitationService
    {
        private readonly IEmailSender _email;
        private readonly IConfiguration _configuration;

        public WorkspaceInvitationService(IEmailSender email, IConfiguration configuration)
        {
            _email = email;
            _configuration = configuration;
        }

        public static string HashToken(string token)
        {
            using (var sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(token)));
        }

        public async Task SendAsync(CompanyInvitation invitation, Company company, string inviter)
        {
            var webUrl = _configuration["PUBLIC_WEB_URL"];
            if (!Uri.TryCreate(webUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
                throw new InvalidOperationException("PUBLIC_WEB_URL must be an absolute HTTP(S) URL to send workspace invitations.");
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            var rawToken = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            invitation.TokenHash = HashToken(rawToken);
            invitation.ExpiresAt = DateTime.UtcNow.AddDays(7);
            var link = webUrl.TrimEnd('/') + "/invite/" + rawToken;
            var html = $"<p>{WebUtility.HtmlEncode(inviter)} invited you to {WebUtility.HtmlEncode(company.Name)} on AccountGo.</p>" +
                $"<p>Workspace role: {WebUtility.HtmlEncode(invitation.Role.ToString())}</p>" +
                $"<p><a href=\"{WebUtility.HtmlEncode(link)}\">Accept invitation</a></p><p>This link expires in 7 days.</p>";
            await _email.SendAsync(invitation.Email, $"{inviter} invited you to {company.Name} on AccountGo", html);
        }
    }
}
