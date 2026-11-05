using Api.Options;
using Xunit;

namespace AccountGo.BackendTests.Security;

[Collection("WorkspaceSetupHttp")]
public sealed class WorkspaceExperienceEnvironmentTests
{
    [Fact]
    public void CanonicalEnvironment_EnablesSetup_DespiteStaleAliasValues()
    {
        var values = new Dictionary<string, string?>
        {
            ["WORKSPACE_EXPERIENCE_SOLO_SETUP_ENABLED"] = "true",
            ["WorkspaceExperience__SoloSetupEnabled"] = "false"
        };
        var previous = values.Keys.ToDictionary(key => key, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var pair in values) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            Assert.True(WorkspaceExperienceOptions.FromEnvironment(new WorkspaceExperienceOptions()).SoloSetupEnabled);
        }
        finally
        {
            foreach (var pair in previous) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }
}
