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
            await _email.SendAsync(
                invitation.Email,
                $"{inviter} invited you to {company.Name} on AccountGo",
                BuildHtml(inviter, company.Name, invitation.Role.ToString(), link));
        }

        /// <summary>Same card layout as the OTP emails: AccountGo header, body, centered action, expiry note.</summary>
        public static string BuildHtml(string inviter, string workspaceName, string role, string acceptUrl)
        {
            var safeInviter = WebUtility.HtmlEncode(inviter);
            var safeWorkspace = WebUtility.HtmlEncode(workspaceName);
            var safeRole = WebUtility.HtmlEncode(role);
            var safeUrl = WebUtility.HtmlEncode(acceptUrl);

            return $@"
<div style=""font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; max-width: 500px; margin: 0 auto; padding: 24px; border: 1px solid #e2e8f0; border-radius: 8px; background-color: #ffffff;"">
  <div style=""text-align: center; margin-bottom: 24px;"">
    <h2 style=""color: #0f172a; margin: 0; font-size: 24px; font-weight: 700;"">AccountGo</h2>
    <p style=""color: #64748b; font-size: 14px; margin-top: 4px;"">Workspace invitation</p>
  </div>
  <p style=""color: #334155; font-size: 15px; line-height: 1.5;"">Hello,</p>
  <p style=""color: #334155; font-size: 15px; line-height: 1.5;""><strong>{safeInviter}</strong> invited you to <strong>{safeWorkspace}</strong> on AccountGo.</p>
  <p style=""color: #334155; font-size: 15px; line-height: 1.5;"">Workspace role: <strong>{safeRole}</strong></p>
  <div style=""text-align: center; margin: 32px 0;"">
    <a href=""{safeUrl}"" style=""display: inline-block; font-size: 15px; font-weight: 700; color: #ffffff; background-color: #1e3a8a; padding: 12px 28px; border-radius: 8px; text-decoration: none;"">Accept invitation</a>
  </div>
  <p style=""color: #64748b; font-size: 13px; line-height: 1.4;"">This link will expire in <strong>7 days</strong>. If you were not expecting this invitation, you can safely ignore this email.</p>
</div>";
        }
    }
}
