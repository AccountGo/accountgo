#nullable enable
using System.Collections.Generic;
using Api.Service.Ai;
using Xunit;

namespace AccountGo.BackendTests.Ai
{
    public class AnterRunStreamBodyTests
    {
        [Fact]
        public void Build_PutsUserIdAndSessionIdInsideContextVariables()
        {
            var body = AnterRunStreamBody.Build(
                "Hello",
                "user-42",
                "session_abc",
                new Dictionary<string, string>
                {
                    ["companySlug"] = "acme",
                    ["pageRoute"] = "/org/acme/dashboard",
                });

            Assert.Equal("Hello", body["message"]);
            var cvs = Assert.IsType<Dictionary<string, string>>(body["contextVariables"]);
            Assert.Equal("user-42", cvs["userId"]);
            Assert.Equal("session_abc", cvs["sessionId"]);
            Assert.Equal("acme", cvs["companySlug"]);
            Assert.Equal("/org/acme/dashboard", cvs["pageRoute"]);
            Assert.False(body.ContainsKey("sessionId"));
        }

        [Fact]
        public void Build_OverwritesClientUserIdWithAuthenticatedUser()
        {
            var body = AnterRunStreamBody.Build(
                "Hi",
                "auth-user",
                null,
                new Dictionary<string, string> { ["userId"] = "forged" });

            var cvs = Assert.IsType<Dictionary<string, string>>(body["contextVariables"]);
            Assert.Equal("auth-user", cvs["userId"]);
        }
    }
}
