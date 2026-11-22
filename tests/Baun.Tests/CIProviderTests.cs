using Baun.CI;

namespace Baun.Tests;

/// <summary>
/// Sets environment variables, which are shared by the whole process, so it runs in the collection of
/// <see cref="GitHubActionsTests"/>, which doesn't run in parallel with other tests.
/// </summary>
[Collection(nameof(GitHubActionsTests))]
public sealed class CIProviderTests : IDisposable
{
    private static readonly string[] Variables = ["GITHUB_ACTIONS", "TF_BUILD", "GITLAB_CI", "TEAMCITY_VERSION"];

    private readonly Dictionary<string, string?> original = Variables.ToDictionary(name => name, System.Environment.GetEnvironmentVariable);

    public CIProviderTests()
    {
        foreach (string name in Variables)
        {
            System.Environment.SetEnvironmentVariable(name, null);
        }
    }

    public void Dispose()
    {
        foreach ((string name, string? value) in original)
        {
            System.Environment.SetEnvironmentVariable(name, value);
        }
    }

    [Theory]
    [InlineData("GITHUB_ACTIONS", "true", nameof(CIProvider.GitHubActions))]
    [InlineData("TF_BUILD", "True", nameof(CIProvider.AzurePipelines))]
    [InlineData("GITLAB_CI", "true", nameof(CIProvider.GitLab))]
    [InlineData("TEAMCITY_VERSION", "2025.07", nameof(CIProvider.TeamCity))]
    [InlineData("GITHUB_ACTIONS", "false", nameof(CIProvider.None))]
    public void Detects_the_CI_server(string variable, string value, string expected)
    {
        System.Environment.SetEnvironmentVariable(variable, value);

        Assert.Equal(Enum.Parse<CIProvider>(expected), CIProviders.Detect());
    }

    [Fact]
    public void Detects_no_CI_server_without_its_variables()
    {
        Assert.Equal(CIProvider.None, CIProviders.Detect());
    }
}
