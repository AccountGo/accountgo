using Api.DocumentCaptures;
using Core.Domain.Security;
using Core.Entitlements;
using Moq;
using Xunit;

namespace AccountGo.BackendTests.DocumentCapture;

public sealed class DocumentCaptureAccessPolicyTests
{
    [Fact]
    public void NewCapture_RequiresFeatureModuleAndKindPermission()
    {
        var service = Entitlements("invoicing", hasFeature: true, moduleAllowed: false);
        var permissions = new HashSet<string>(StringComparer.Ordinal) { SystemPermissions.PurchasesBillsCreate };

        var result = DocumentCaptureAccessPolicy.AuthorizeNew(service, 1, permissions, "purchase-invoice");

        Assert.Equal(DocumentCaptureAccessFailure.ModuleRequired, result);
    }

    [Fact]
    public void NewCapture_CommunityBypassesFeatureButStillRequiresModulePermission()
    {
        var service = Entitlements(PlanKeys.Community, hasFeature: false, moduleAllowed: true);

        Assert.Equal(DocumentCaptureAccessFailure.PermissionRequired,
            DocumentCaptureAccessPolicy.AuthorizeNew(service, 1, new HashSet<string>(), "purchase-invoice"));
        Assert.Equal(DocumentCaptureAccessFailure.None,
            DocumentCaptureAccessPolicy.AuthorizeNew(service, 1,
                new HashSet<string> { SystemPermissions.PurchasesBillsCreate }, "purchase-invoice"));
    }

    [Fact]
    public void NewCapture_HostedWorkspaceWithoutGrantIsDenied()
    {
        var service = Entitlements("invoicing", hasFeature: false, moduleAllowed: true);

        Assert.Equal(DocumentCaptureAccessFailure.FeatureRequired,
            DocumentCaptureAccessPolicy.AuthorizeNew(service, 1,
                new HashSet<string> { SystemPermissions.PurchasesBillsCreate }, "purchase-invoice"));
    }

    private static IEntitlementService Entitlements(string plan, bool hasFeature, bool moduleAllowed)
    {
        var features = hasFeature
            ? new HashSet<string>(StringComparer.Ordinal) { FeatureKeys.DocumentCapture }
            : new HashSet<string>(StringComparer.Ordinal);
        var snapshot = new EntitlementSnapshot(true, plan, plan, false, [], [], [], null, features, plan);
        var mock = new Mock<IEntitlementService>();
        mock.Setup(service => service.Get(1)).Returns(snapshot);
        mock.Setup(service => service.Evaluate(1, It.IsAny<EntitlementAction>()))
            .Returns(new EntitlementDecision(moduleAllowed, null, null));
        return mock.Object;
    }
}
