using Api.Options;
using Api.Service;
using Microsoft.Extensions.Options;
using Xunit;

namespace AccountGo.BackendTests.Security;

[Collection("CanaryEnrollmentHttp")]
public sealed class WorkspaceExperienceEnvironmentTests
{
    [Fact]
    public void CanonicalEnvironment_AdmitsEnrolledOwner_DespiteStaleAliasValues()
    {
        var values = new Dictionary<string, string?>
        {
            ["WORKSPACE_EXPERIENCE_SOLO_SETUP_ENABLED"] = "true",
            ["WORKSPACE_EXPERIENCE_CANARY_USER_IDS"] = "verified-owner",
            ["WorkspaceExperience__SoloSetupEnabled"] = "false",
            ["WorkspaceExperience__Canary__UserIds"] = "stale-owner"
        };
        var previous = values.Keys.ToDictionary(key => key, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var pair in values) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            var enrollment = new WorkspaceCanaryEnrollment(Options.Create(new WorkspaceExperienceOptions()));
            Assert.True(enrollment.IsNewBusinessSoloAudienceAdmitted(null, "verified-owner"));
            Assert.False(enrollment.IsNewBusinessSoloAudienceAdmitted(null, "stale-owner"));
        }
        finally
        {
            foreach (var pair in previous) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }
}
