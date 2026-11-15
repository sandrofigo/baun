using Baun.CI;

namespace Baun.Tests;

/// <summary>
/// Sets environment variables, which are shared by the whole process, so it doesn't run in parallel with other tests.
/// </summary>
[CollectionDefinition(nameof(GitHubTests), DisableParallelization = true)]
public sealed class GitHubTestsCollection;

[Collection(nameof(GitHubTests))]
public sealed class GitHubTests : IDisposable
{
    private static readonly string[] Variables =
    [
        "GITHUB_ACTIONS", "GITHUB_REF", "GITHUB_REF_NAME", "GITHUB_REF_TYPE", "GITHUB_REF_PROTECTED", "GITHUB_REPOSITORY",
        "GITHUB_RUN_ID", "RUNNER_DEBUG",
    ];

    private readonly Dictionary<string, string?> original = Variables.ToDictionary(name => name, System.Environment.GetEnvironmentVariable);

    public GitHubTests()
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

    [Fact]
    public void Is_empty_outside_GitHub_Actions()
    {
        Assert.False(GitHub.IsActions);
        Assert.Null(GitHub.Ref);
        Assert.Null(GitHub.RefName);
        Assert.Null(GitHub.RefType);
        Assert.False(GitHub.IsTag);
        Assert.False(GitHub.IsBranch);
        Assert.Null(GitHub.Tag);
        Assert.Null(GitHub.RunId);
        Assert.False(GitHub.IsRefProtected);
        Assert.False(GitHub.IsDebug);
    }

    [Fact]
    public void Reads_flags()
    {
        System.Environment.SetEnvironmentVariable("GITHUB_REF_PROTECTED", "true");
        System.Environment.SetEnvironmentVariable("RUNNER_DEBUG", "1");

        Assert.True(GitHub.IsRefProtected);
        Assert.True(GitHub.IsDebug);
    }

    [Fact]
    public void Reads_a_tag()
    {
        System.Environment.SetEnvironmentVariable("GITHUB_ACTIONS", "true");
        System.Environment.SetEnvironmentVariable("GITHUB_REF", "refs/tags/v1.2.3");
        System.Environment.SetEnvironmentVariable("GITHUB_REF_NAME", "v1.2.3");
        System.Environment.SetEnvironmentVariable("GITHUB_REF_TYPE", "tag");

        Assert.True(GitHub.IsActions);
        Assert.Equal("refs/tags/v1.2.3", GitHub.Ref);
        Assert.Equal(GitHubRefType.Tag, GitHub.RefType);
        Assert.True(GitHub.IsTag);
        Assert.False(GitHub.IsBranch);
        Assert.Equal("v1.2.3", GitHub.Tag);
    }

    [Fact]
    public void Has_no_tag_for_a_branch()
    {
        System.Environment.SetEnvironmentVariable("GITHUB_REF_NAME", "main");
        System.Environment.SetEnvironmentVariable("GITHUB_REF_TYPE", "branch");

        Assert.Equal(GitHubRefType.Branch, GitHub.RefType);
        Assert.True(GitHub.IsBranch);
        Assert.Equal("main", GitHub.RefName);
        Assert.Null(GitHub.Tag);
    }

    [Theory]
    [InlineData("")]
    [InlineData("release")]
    [InlineData("1")]
    public void Has_no_ref_type_for_an_unknown_value(string value)
    {
        System.Environment.SetEnvironmentVariable("GITHUB_REF_TYPE", value);

        Assert.Null(GitHub.RefType);
    }

    [Fact]
    public void Treats_empty_variables_as_not_set()
    {
        System.Environment.SetEnvironmentVariable("GITHUB_REPOSITORY", "");

        Assert.Null(GitHub.Repository);
    }

    [Theory]
    [InlineData("1234567890", 1234567890L)]
    [InlineData("abc", null)]
    [InlineData("-1", null)]
    public void Reads_the_run_id(string value, long? expected)
    {
        System.Environment.SetEnvironmentVariable("GITHUB_RUN_ID", value);

        Assert.Equal(expected, GitHub.RunId);
    }
}
