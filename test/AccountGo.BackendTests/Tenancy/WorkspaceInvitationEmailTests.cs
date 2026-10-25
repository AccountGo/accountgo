using System.Net;
using Api.Service;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AccountGo.BackendTests.Tenancy;

public class WorkspaceInvitationEmailTests
{
    [Fact]
    public void BuildHtml_MatchesOtpCardLayout_AndEncodesUserInput()
    {
        var html = WorkspaceInvitationService.BuildHtml(
            "Ada <script>",
            "Acme & Co",
            "Member",
            "https://app.example/invite/abc");

        Assert.Contains("AccountGo", html);
        Assert.Contains("Workspace invitation", html);
        Assert.Contains("Accept invitation", html);
        Assert.Contains("7 days", html);
        Assert.Contains("max-width: 500px", html);
        Assert.Contains(WebUtility.HtmlEncode("Ada <script>"), html);
        Assert.Contains(WebUtility.HtmlEncode("Acme & Co"), html);
        Assert.Contains("https://app.example/invite/abc", html);
        Assert.DoesNotContain("<script>", html);
    }

    [Fact]
    public async Task SendAsync_ConfiguresInvitesFromAndInviterReplyTo()
    {
        EmailMessage? captured = null;
        var mockEmail = new MockEmailSender(msg => captured = msg);
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
            {
                ["PUBLIC_WEB_URL"] = "https://app.accountgo.dev"
            })
            .Build();

        var service = new WorkspaceInvitationService(mockEmail, config);
        var invitation = new Core.Domain.CompanyInvitation
        {
            Email = "invitee@example.com",
            Role = Core.Domain.CompanyMemberRole.Member
        };
        var company = new Core.Domain.Company { Id = 1, Name = "Pacific Rim" };

        await service.SendAsync(invitation, company, "Marvin Perez", "marvin@example.com");

        Assert.NotNull(captured);
        Assert.Equal("invitee@example.com", captured!.To);
        Assert.Equal("Marvin Perez via AccountGo", captured.FromName);
        Assert.Equal("invites@accountgo.dev", captured.FromEmail);
        Assert.Equal("marvin@example.com", captured.ReplyTo);
        Assert.Contains("Marvin Perez invited you to Pacific Rim on AccountGo", captured.Subject);
        Assert.StartsWith("https://app.accountgo.dev/invite/", captured.Html.Substring(captured.Html.IndexOf("https://app.accountgo.dev/invite/")));
    }

    private sealed class MockEmailSender : IEmailSender
    {
        private readonly System.Action<EmailMessage> _callback;
        public MockEmailSender(System.Action<EmailMessage> callback) => _callback = callback;
        public System.Threading.Tasks.Task<string?> SendAsync(EmailMessage message, System.Threading.CancellationToken ct = default)
        {
            _callback(message);
            return System.Threading.Tasks.Task.FromResult<string?>("mock_id");
        }
    }
}
