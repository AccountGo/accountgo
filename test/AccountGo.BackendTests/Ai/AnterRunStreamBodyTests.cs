#nullable enable
using System.Collections.Generic;
using Api.Service.Ai;
using Xunit;

namespace AccountGo.BackendTests.Ai
{
    public class AnterRunStreamBodyTests
    {
        [Fact]
        public void BuildScopedUserId_FormatsWorkspaceAndUserWithoutColons()
        {
            var scoped = AnterRunStreamBody.BuildScopedUserId(7, "user:abc-123");

            Assert.Equal("ag_c7_u_user_abc-123", scoped);
            Assert.DoesNotContain(":", scoped);
        }

        [Fact]
        public void Build_PutsUserIdAndSessionIdInsideContextVariables_AndAttachesCallerAssertion()
        {
            var body = AnterRunStreamBody.Build(
                "Hello",
                "ag_c7_u_user-42",
                "session_abc",
                new Dictionary<string, string>
                {
                    ["pageRoute"] = "/org/acme/dashboard",
                },
                companyId: "7",
                companySlug: "acme",
                callerAssertion: "v1.payload.mac",
                accountGoUserId: "user-42");

            Assert.Equal("Hello", body["message"]);
            var cvs = Assert.IsType<Dictionary<string, string>>(body["contextVariables"]);
            Assert.Equal("ag_c7_u_user-42", cvs["userId"]);
            Assert.Equal("user-42", cvs["accountGoUserId"]);
            Assert.Equal("session_abc", cvs["sessionId"]);
            Assert.Equal("7", cvs["companyId"]);
            Assert.Equal("7", cvs["organizationId"]);
            Assert.Equal("acme", cvs["companySlug"]);
            Assert.Equal("/org/acme/dashboard", cvs["pageRoute"]);
            Assert.False(body.ContainsKey("sessionId"));

            var passThrough = Assert.IsType<Dictionary<string, string>>(body["mcpPassThroughHeaders"]);
            Assert.Equal("v1.payload.mac", passThrough[McpCallerAssertionService.HeaderName]);
        }

        [Fact]
        public void Build_OverwritesClientUserIdAndTenantFields()
        {
            var body = AnterRunStreamBody.Build(
                "Hi",
                "ag_c7_u_auth-user",
                null,
                new Dictionary<string, string>
                {
                    ["userId"] = "forged",
                    ["accountGoUserId"] = "forged-raw",
                    ["companyId"] = "999",
                    ["companySlug"] = "victim",
                    ["organizationId"] = "999",
                    ["pageTitle"] = "Dashboard",
                },
                companyId: "7",
                companySlug: "acme",
                accountGoUserId: "auth-user");

            var cvs = Assert.IsType<Dictionary<string, string>>(body["contextVariables"]);
            Assert.Equal("ag_c7_u_auth-user", cvs["userId"]);
            Assert.Equal("auth-user", cvs["accountGoUserId"]);
            Assert.Equal("7", cvs["companyId"]);
            Assert.Equal("7", cvs["organizationId"]);
            Assert.Equal("acme", cvs["companySlug"]);
            Assert.Equal("Dashboard", cvs["pageTitle"]);
            Assert.False(body.ContainsKey("mcpPassThroughHeaders"));
        }

        [Fact]
        public void BuildResumeBody_IncludesMcpPassThroughHeadersWhenAssertionPresent()
        {
            var body = AnterRunStreamBody.BuildResumeBody("v1.resume.mac");

            var passThrough = Assert.IsType<Dictionary<string, string>>(body["mcpPassThroughHeaders"]);
            Assert.Equal("v1.resume.mac", passThrough[McpCallerAssertionService.HeaderName]);
        }
    }
}
