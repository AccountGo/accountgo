using System.Xml.Linq;
using Xunit;

namespace AccountGo.BackendTests.DocumentCapture;

public sealed class DocumentCapturePackagePinTests
{
    [Fact]
    public void ApiTestAndBrandingProjects_UseTheSameSkiaSharpPinsWhenAvailable()
    {
        var root = FindRepositoryRoot();
        Assert.NotNull(root);
        var publicVersion = ReadProperty(Path.Combine(root!, "Directory.Build.props"), "AccountGoSkiaSharpVersion");

        AssertProjectVersion(Path.Combine(root!, "src/Api/Api.csproj"), "$(AccountGoSkiaSharpVersion)");
        AssertProjectVersion(Path.Combine(root!, "test/AccountGo.BackendTests/AccountGo.BackendTests.csproj"), "$(AccountGoSkiaSharpVersion)");

        var brandingProject = Path.Combine(root!, "modules/Branding/AccountGo.Branding.csproj");
        if (!File.Exists(brandingProject))
            return;

        AssertProjectVersion(brandingProject, publicVersion);
    }

    private static string? FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Directory.Build.props"))
                && File.Exists(Path.Combine(current.FullName, "src/Api/Api.csproj")))
                return current.FullName;
            current = current.Parent;
        }
        return null;
    }

    private static string ReadProperty(string propsPath, string propertyName)
    {
        var document = XDocument.Load(propsPath);
        return document.Root!.Descendants(propertyName).Single().Value;
    }

    private static void AssertProjectVersion(string projectPath, string expectedVersion)
    {
        var document = XDocument.Load(projectPath);
        foreach (var packageName in new[] { "SkiaSharp", "SkiaSharp.NativeAssets.Linux.NoDependencies" })
        {
            var reference = document.Descendants("PackageReference")
                .Single(element => (string?)element.Attribute("Include") == packageName);
            Assert.Equal(expectedVersion, (string?)reference.Attribute("Version"));
        }
    }
}
