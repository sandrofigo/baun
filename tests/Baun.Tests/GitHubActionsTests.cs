using Baun.CI.GitHub;

namespace Baun.Tests;

/// <summary>
/// Sets environment variables, which are shared by the whole process, so it doesn't run in parallel with other tests.
/// </summary>
[CollectionDefinition(nameof(GitHubActionsTests), DisableParallelization = true)]
public sealed class GitHubActionsTestsCollection;

[Collection(nameof(GitHubActionsTests))]
public sealed class GitHubActionsTests : IDisposable
{
    private static readonly string[] Variables =
    [
        "GITHUB_ACTIONS", "GITHUB_REF", "GITHUB_REF_NAME", "GITHUB_REF_TYPE", "GITHUB_REF_PROTECTED", "GITHUB_REPOSITORY",
        "GITHUB_RUN_ID", "RUNNER_DEBUG", "GITHUB_TOKEN",
    ];

    private readonly Dictionary<string, string?> original = Variables.ToDictionary(name => name, System.Environment.GetEnvironmentVariable);

    public GitHubActionsTests()
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
        Assert.False(GitHubActions.IsRunning);
        Assert.Null(GitHubActions.Ref);
        Assert.Null(GitHubActions.RefName);
        Assert.Null(GitHubActions.RefType);
        Assert.False(GitHubActions.IsTag);
        Assert.False(GitHubActions.IsBranch);
        Assert.Null(GitHubActions.Tag);
        Assert.Null(GitHubActions.RunId);
        Assert.False(GitHubActions.IsRefProtected);
        Assert.False(GitHubActions.IsDebug);
        Assert.Null(GitHubActions.Token);
    }

    [Fact]
    public void Reads_flags()
    {
        System.Environment.SetEnvironmentVariable("GITHUB_REF_PROTECTED", "true");
        System.Environment.SetEnvironmentVariable("RUNNER_DEBUG", "1");

        Assert.True(GitHubActions.IsRefProtected);
        Assert.True(GitHubActions.IsDebug);
    }

    [Fact]
    public void Reads_a_tag()
    {
        System.Environment.SetEnvironmentVariable("GITHUB_ACTIONS", "true");
        System.Environment.SetEnvironmentVariable("GITHUB_REF", "refs/tags/v1.2.3");
        System.Environment.SetEnvironmentVariable("GITHUB_REF_NAME", "v1.2.3");
        System.Environment.SetEnvironmentVariable("GITHUB_REF_TYPE", "tag");

        Assert.True(GitHubActions.IsRunning);
        Assert.Equal("refs/tags/v1.2.3", GitHubActions.Ref);
        Assert.Equal(GitHubRefType.Tag, GitHubActions.RefType);
        Assert.True(GitHubActions.IsTag);
        Assert.False(GitHubActions.IsBranch);
        Assert.Equal("v1.2.3", GitHubActions.Tag);
    }

    [Fact]
    public void Has_no_tag_for_a_branch()
    {
        System.Environment.SetEnvironmentVariable("GITHUB_REF_NAME", "main");
        System.Environment.SetEnvironmentVariable("GITHUB_REF_TYPE", "branch");

        Assert.Equal(GitHubRefType.Branch, GitHubActions.RefType);
        Assert.True(GitHubActions.IsBranch);
        Assert.Equal("main", GitHubActions.RefName);
        Assert.Null(GitHubActions.Tag);
    }

    [Theory]
    [InlineData("")]
    [InlineData("release")]
    [InlineData("1")]
    public void Has_no_ref_type_for_an_unknown_value(string value)
    {
        System.Environment.SetEnvironmentVariable("GITHUB_REF_TYPE", value);

        Assert.Null(GitHubActions.RefType);
    }

    [Fact]
    public void Treats_empty_variables_as_not_set()
    {
        System.Environment.SetEnvironmentVariable("GITHUB_REPOSITORY", "");

        Assert.Null(GitHubActions.Repository);
    }

    [Fact]
    public void Reads_the_repository()
    {
        System.Environment.SetEnvironmentVariable("GITHUB_REPOSITORY", "octocat/Hello-World");

        Assert.Equal(new GitHubRepository("octocat", "Hello-World"), GitHubActions.Repository);
    }

    [Fact]
    public void Has_no_repository_for_an_invalid_value()
    {
        System.Environment.SetEnvironmentVariable("GITHUB_REPOSITORY", "noslash");

        Assert.Null(GitHubActions.Repository);
    }

    [Theory]
    [InlineData("1234567890", 1234567890L)]
    [InlineData("abc", null)]
    [InlineData("1.5", null)]
    public void Reads_the_run_id(string value, long? expected)
    {
        System.Environment.SetEnvironmentVariable("GITHUB_RUN_ID", value);

        Assert.Equal(expected, GitHubActions.RunId);
    }

    [Fact]
    public async Task Release_needs_a_repository()
    {
        var release = new GitHubRelease { Tag = "v1.2.3" };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => GitHubReleases.CreateOrUpdateAsync(release, token: "token", TestContext.Current.CancellationToken));

        Assert.Contains("GITHUB_REPOSITORY", exception.Message);
    }

    [Fact]
    public async Task Release_needs_a_valid_repository()
    {
        System.Environment.SetEnvironmentVariable("GITHUB_REPOSITORY", "noslash");
        System.Environment.SetEnvironmentVariable("GITHUB_TOKEN", "token");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => GitHubReleases.GetAsync("v1.2.3", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("GITHUB_REPOSITORY", exception.Message);
    }

    [Fact]
    public async Task Release_needs_a_token()
    {
        System.Environment.SetEnvironmentVariable("GITHUB_REPOSITORY", "octocat/Hello-World");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => GitHubReleases.GetAsync("v1.2.3", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("GITHUB_TOKEN", exception.Message);
    }

    [Theory]
    [InlineData("v1.2.3", "")]
    [InlineData("", "token")]
    public async Task Getting_a_release_throws_for_an_empty_tag_or_token(string tag, string token)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => GitHubReleases.GetAsync(
            tag, repository: new GitHubRepository("octocat", "Hello-World"), token: token, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Saving_a_release_throws_for_an_empty_token()
    {
        var release = new GitHubRelease { Tag = "v1.2.3", Repository = new GitHubRepository("octocat", "Hello-World") };

        await Assert.ThrowsAsync<ArgumentException>(() => GitHubReleases.CreateOrUpdateAsync(release, token: "", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Deleting_a_release_that_isnt_saved_throws()
    {
        var release = new GitHubRelease { Tag = "v1.2.3" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => GitHubReleases.DeleteAsync(release, token: "token", TestContext.Current.CancellationToken));
    }
}
