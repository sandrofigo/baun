using Baun.CI;
using Baun.CI.AzureDevOps;
using Baun.CI.GitLab;
using Baun.CI.TeamCity;

namespace Baun.Tests;

/// <summary>
/// Sets environment variables, which are shared by the whole process, so it runs in the collection of
/// <see cref="GitHubActionsTests"/>, which doesn't run in parallel with other tests.
/// </summary>
[Collection(nameof(GitHubActionsTests))]
public sealed class CIProviderTests : IDisposable
{
    private static readonly string[] Variables = ["GITHUB_ACTIONS", "TF_BUILD", "GITLAB_CI", "TEAMCITY_VERSION", "CI"];

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
    [InlineData("GITHUB_ACTIONS", "true", nameof(CIProviderType.GitHubActions))]
    [InlineData("TF_BUILD", "True", nameof(CIProviderType.AzurePipelines))]
    [InlineData("GITLAB_CI", "true", nameof(CIProviderType.GitLab))]
    [InlineData("TEAMCITY_VERSION", "2025.07", nameof(CIProviderType.TeamCity))]
    [InlineData("GITHUB_ACTIONS", "false", nameof(CIProviderType.None))]
    [InlineData("CI", "true", nameof(CIProviderType.None))]
    public void Detects_the_CI_provider(string variable, string value, string expected)
    {
        System.Environment.SetEnvironmentVariable(variable, value);

        Assert.Equal(Enum.Parse<CIProviderType>(expected), CIProvider.Current);
    }

    [Fact]
    public void Detects_no_CI_provider_without_its_variables()
    {
        Assert.Equal(CIProviderType.None, CIProvider.Current);
        Assert.False(CIProvider.IsRunning);
    }

    [Theory]
    [InlineData("GITLAB_CI", "true")]
    [InlineData("CI", "true")]
    [InlineData("CI", "TRUE")]
    public void Runs_on_a_CI_provider_with_a_known_one_or_CI(string variable, string value)
    {
        System.Environment.SetEnvironmentVariable(variable, value);

        Assert.True(CIProvider.IsRunning);
    }

    [Fact]
    public void Does_not_run_on_a_CI_provider_when_CI_is_false()
    {
        System.Environment.SetEnvironmentVariable("CI", "false");

        Assert.False(CIProvider.IsRunning);
    }

    [Fact]
    public void Each_CI_provider_runs_only_with_its_variable()
    {
        Assert.False(AzurePipelines.IsRunning);
        Assert.False(GitLabCI.IsRunning);
        Assert.False(TeamCityBuild.IsRunning);

        System.Environment.SetEnvironmentVariable("TF_BUILD", "True");
        System.Environment.SetEnvironmentVariable("GITLAB_CI", "true");
        System.Environment.SetEnvironmentVariable("TEAMCITY_VERSION", "2025.07");

        Assert.True(AzurePipelines.IsRunning);
        Assert.True(GitLabCI.IsRunning);
        Assert.True(TeamCityBuild.IsRunning);
    }

    [Theory]
    [InlineData("TF_BUILD", "False")]
    [InlineData("GITLAB_CI", "false")]
    [InlineData("TEAMCITY_VERSION", "")]
    public void A_CI_provider_does_not_run_with_a_false_or_empty_variable(string variable, string value)
    {
        System.Environment.SetEnvironmentVariable(variable, value);

        Assert.False(AzurePipelines.IsRunning);
        Assert.False(GitLabCI.IsRunning);
        Assert.False(TeamCityBuild.IsRunning);
    }
}
