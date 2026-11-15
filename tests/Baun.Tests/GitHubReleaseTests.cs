using Baun.CI;

namespace Baun.Tests;

public sealed class GitHubReleaseTests
{
    private static readonly GitHubRepository Repository = new("octocat", "Hello-World");
    private static readonly GitHubReleaseData Data = new("v1.2.3", "v1.2.3", "- Fixed a bug", Prerelease: true);

    [Fact]
    public async Task Creates_the_release_if_it_doesnt_exist()
    {
        var api = new FakeGitHubApi(existing: null);

        GitHubRelease release = await GitHubReleases.CreateOrUpdate(api, Repository, Data, TestContext.Current.CancellationToken);

        Assert.Equal((Repository, "v1.2.3"), api.Requested);
        Assert.Equal(Data, api.Created);
        Assert.Null(api.Updated);
        Assert.Equal("v1.2.3", release.Tag);
        Assert.Equal("- Fixed a bug", release.Notes);
        Assert.True(release.Prerelease);
    }

    [Fact]
    public async Task Updates_the_release_if_it_exists()
    {
        var existing = new GitHubRelease(42, "v1.2.3", "Old", "Old notes", false, new Uri("https://github.com/octocat/Hello-World/releases/tag/v1.2.3"));
        var api = new FakeGitHubApi(existing);

        GitHubRelease release = await GitHubReleases.CreateOrUpdate(api, Repository, Data, TestContext.Current.CancellationToken);

        Assert.Null(api.Created);
        Assert.Equal((42L, Data), api.Updated);
        Assert.Equal(42, release.Id);
        Assert.Equal("- Fixed a bug", release.Notes);
    }

    private sealed class FakeGitHubApi(GitHubRelease? existing) : IGitHubApi
    {
        public (GitHubRepository Repository, string Tag)? Requested { get; private set; }

        public GitHubReleaseData? Created { get; private set; }

        public (long Id, GitHubReleaseData Data)? Updated { get; private set; }

        public Task<GitHubRelease?> GetReleaseByTag(GitHubRepository repository, string tag, CancellationToken cancellationToken)
        {
            Requested = (repository, tag);
            return Task.FromResult(existing);
        }

        public Task<GitHubRelease> CreateRelease(GitHubRepository repository, GitHubReleaseData data, CancellationToken cancellationToken)
        {
            Created = data;
            return Task.FromResult(ToRelease(1, repository, data));
        }

        public Task<GitHubRelease> UpdateRelease(GitHubRepository repository, long id, GitHubReleaseData data, CancellationToken cancellationToken)
        {
            Updated = (id, data);
            return Task.FromResult(ToRelease(id, repository, data));
        }

        private static GitHubRelease ToRelease(long id, GitHubRepository repository, GitHubReleaseData data) =>
            new(id, data.Tag, data.Name, data.Notes, data.Prerelease, new Uri($"https://github.com/{repository}/releases/tag/{data.Tag}"));
    }
}
