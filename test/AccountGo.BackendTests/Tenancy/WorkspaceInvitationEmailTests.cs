using System.Net;
using Api.Service;
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
}
