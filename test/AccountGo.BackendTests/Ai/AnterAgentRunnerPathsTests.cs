#nullable enable
using Api.Service.Ai;
using Xunit;

namespace AccountGo.BackendTests.Ai
{
    public class AnterAgentRunnerPathsTests
    {
        [Fact]
        public void RunStream_UsesExternalAgentRunnerBySlugPath()
        {
            var url = AnterAgentRunnerPaths.RunStream("https://agents.anter.ai/", "accountgo-assistant");
            Assert.Equal(
                "https://agents.anter.ai/api/v1/external/agent-runner/agents/by-slug/accountgo-assistant/run-stream",
                url);
        }

        [Fact]
        public void Sessions_RequiresUserIdQuery()
        {
            var url = AnterAgentRunnerPaths.Sessions(
                "https://agents.anter.ai",
                "accountgo-assistant",
                "user-123");
            Assert.Equal(
                "https://agents.anter.ai/api/v1/external/agent-runner/agents/by-slug/accountgo-assistant/sessions?userId=user-123",
                url);
        }

        [Fact]
        public void Session_EncodesSessionIdAndUserId()
        {
            var url = AnterAgentRunnerPaths.Session(
                "https://agents.anter.ai",
                "accountgo-assistant",
                "session/a b",
                "user 1");
            Assert.Contains("sessions/session%2Fa%20b", url);
            Assert.Contains("userId=user%201", url);
        }
    }
}
