using System.Text.Json;

namespace FunctionFoundry.Engineering.Tests;

public sealed class SdkPinningTests
{
    [Fact]
    public void GlobalJson_pins_exact_sdk_without_roll_forward()
    {
        string path = FindRepoFile("global.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement sdk = document.RootElement.GetProperty("sdk");
        Assert.Equal("10.0.301", sdk.GetProperty("version").GetString());
        Assert.Equal("disable", sdk.GetProperty("rollForward").GetString());
        Assert.False(sdk.GetProperty("allowPrerelease").GetBoolean());
    }

    [Fact]
    public void DirectoryBuildProps_enables_documentation_and_treats_warnings_as_errors()
    {
        string content = File.ReadAllText(FindRepoFile("Directory.Build.props"));
        Assert.Contains("<TreatWarningsAsErrors>true</TreatWarningsAsErrors>", content, StringComparison.Ordinal);
        Assert.Contains("<GenerateDocumentationFile>true</GenerateDocumentationFile>", content, StringComparison.Ordinal);
        Assert.Contains("<Deterministic>true</Deterministic>", content, StringComparison.Ordinal);
        Assert.Contains("<ManagePackageVersionsCentrally>", File.ReadAllText(FindRepoFile("Directory.Packages.props")), StringComparison.Ordinal);
    }

    [Fact]
    public void Forbidden_aggregate_package_names_are_blocked_by_targets()
    {
        string content = File.ReadAllText(FindRepoFile("Directory.Build.targets"));
        Assert.Contains("ValidateNoForbiddenPackageNames", content, StringComparison.Ordinal);
        Assert.Contains(".All", content, StringComparison.Ordinal);
        Assert.Contains(".Common", content, StringComparison.Ordinal);
        Assert.Contains(".Utils", content, StringComparison.Ordinal);
        Assert.Contains(".Helpers", content, StringComparison.Ordinal);
    }

    private static string FindRepoFile(string relativePath)
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new FileNotFoundException($"Could not locate repository file '{relativePath}'.");
    }
}
