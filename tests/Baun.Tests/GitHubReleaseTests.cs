using Baun.CI;

namespace Baun.Tests;

public sealed class GitHubReleaseTests
{
    private static readonly GitHubRepository Repository = new("octocat", "Hello-World");
    private static readonly GitHubReleaseData Data = new("v1.2.3", "v1.2.3", "- Fixed a bug", Draft: false, Prerelease: true);
    private static readonly AbsolutePath Zip = (AbsolutePath)Path.Combine(Path.GetTempPath(), "app.zip");
    private static readonly AbsolutePath Checksums = (AbsolutePath)Path.Combine(Path.GetTempPath(), "checksums.txt");

    [Fact]
    public async Task Creates_the_release_if_it_doesnt_exist()
    {
        var api = new FakeGitHubApi(existing: null);

        GitHubRelease release = await GitHubReleases.CreateOrUpdate(api, Repository, Data, [], TestContext.Current.CancellationToken);

        Assert.Equal((Repository, "v1.2.3", true), api.Requested);
        Assert.Equal(Data, api.Created);
        Assert.Empty(api.Updated);
        Assert.Equal("v1.2.3", release.Tag);
        Assert.Equal("- Fixed a bug", release.Notes);
        Assert.True(release.Prerelease);
        Assert.False(release.Draft);
    }

    [Fact]
    public async Task Updates_the_release_if_it_exists()
    {
        var api = new FakeGitHubApi(Existing(draft: false));

        GitHubRelease release = await GitHubReleases.CreateOrUpdate(api, Repository, Data, [], TestContext.Current.CancellationToken);

        Assert.Null(api.Created);
        Assert.Equal([(42L, Data)], api.Updated);
        Assert.Equal(42, release.Id);
        Assert.Equal("- Fixed a bug", release.Notes);
    }

    [Fact]
    public async Task Creates_a_draft()
    {
        var api = new FakeGitHubApi(existing: null);
        GitHubReleaseData draft = Data with { Draft = true };

        GitHubRelease release = await GitHubReleases.CreateOrUpdate(api, Repository, draft, [Zip], TestContext.Current.CancellationToken);

        Assert.Equal(draft, api.Created);
        Assert.Empty(api.Updated);
        Assert.True(release.Draft);
        Assert.Equal(["app.zip"], release.Assets.Select(asset => asset.Name));
    }

    [Fact]
    public async Task Publishes_an_existing_draft()
    {
        var api = new FakeGitHubApi(Existing(draft: true));

        GitHubRelease release = await GitHubReleases.CreateOrUpdate(api, Repository, Data, [], TestContext.Current.CancellationToken);

        Assert.Equal([(42L, Data)], api.Updated);
        Assert.False(release.Draft);
    }

    [Fact]
    public async Task Publishes_a_new_release_with_assets_after_the_uploads()
    {
        var api = new FakeGitHubApi(existing: null);

        GitHubRelease release = await GitHubReleases.CreateOrUpdate(api, Repository, Data, [Zip, Checksums], TestContext.Current.CancellationToken);

        Assert.Equal(Data with { Draft = true }, api.Created);
        Assert.Equal(["create", "upload app.zip", "upload checksums.txt", "update"], api.Calls);
        Assert.Equal([(1L, Data)], api.Updated);
        Assert.False(release.Draft);
        Assert.Equal(["app.zip", "checksums.txt"], release.Assets.Select(asset => asset.Name));
    }

    [Fact]
    public async Task Keeps_a_published_release_published_while_uploading()
    {
        var api = new FakeGitHubApi(Existing(draft: false));

        await GitHubReleases.CreateOrUpdate(api, Repository, Data, [Zip], TestContext.Current.CancellationToken);

        Assert.Equal([(42L, Data)], api.Updated);
        Assert.Equal(["update", "upload app.zip"], api.Calls);
    }

    [Fact]
    public async Task Replaces_assets_with_the_same_name_and_keeps_the_others()
    {
        GitHubRelease existing = Existing(draft: false) with
        {
            Assets = [Asset(7, "app.zip"), Asset(8, "readme.txt")],
        };
        var api = new FakeGitHubApi(existing);

        GitHubRelease release = await GitHubReleases.CreateOrUpdate(api, Repository, Data, [Zip], TestContext.Current.CancellationToken);

        Assert.Equal(["update", "delete 7", "upload app.zip"], api.Calls);
        Assert.Equal(["readme.txt", "app.zip"], release.Assets.Select(asset => asset.Name));
        Assert.NotEqual(7, release.Assets.Single(asset => asset.Name == "app.zip").Id);
    }

    private static GitHubRelease Existing(bool draft) =>
        new(42, "v1.2.3", "Old", "Old notes", draft, false, new Uri("https://github.com/octocat/Hello-World/releases/tag/v1.2.3"), []);

    private static GitHubReleaseAsset Asset(long id, string name) =>
        new(id, name, 100, new Uri($"https://github.com/octocat/Hello-World/releases/download/v1.2.3/{name}"));

    private sealed class FakeGitHubApi(GitHubRelease? existing) : IGitHubApi
    {
        private long nextAssetId = 100;

        public (GitHubRepository Repository, string Tag, bool IncludeDrafts)? Requested { get; private set; }

        public GitHubReleaseData? Created { get; private set; }

        public List<(long Id, GitHubReleaseData Data)> Updated { get; } = [];

        public List<string> Calls { get; } = [];

        public Task<GitHubRelease?> GetReleaseByTag(
            GitHubRepository repository, string tag, bool includeDrafts = true, CancellationToken cancellationToken = default)
        {
            Requested = (repository, tag, includeDrafts);
            return Task.FromResult(existing);
        }

        public Task<GitHubRelease> CreateRelease(GitHubRepository repository, GitHubReleaseData data, CancellationToken cancellationToken)
        {
            Calls.Add("create");
            Created = data;
            return Task.FromResult(ToRelease(1, repository, data, []));
        }

        public Task<GitHubRelease> UpdateRelease(GitHubRepository repository, long id, GitHubReleaseData data, CancellationToken cancellationToken)
        {
            Calls.Add("update");
            Updated.Add((id, data));
            return Task.FromResult(ToRelease(id, repository, data, existing?.Assets ?? []));
        }

        public Task<GitHubReleaseAsset> UploadAsset(GitHubRepository repository, GitHubRelease release, AbsolutePath file, CancellationToken cancellationToken)
        {
            Calls.Add($"upload {file.Name}");
            return Task.FromResult(Asset(nextAssetId++, file.Name));
        }

        public Task DeleteAsset(GitHubRepository repository, long id, CancellationToken cancellationToken)
        {
            Calls.Add($"delete {id}");
            return Task.CompletedTask;
        }

        private static GitHubRelease ToRelease(long id, GitHubRepository repository, GitHubReleaseData data, IReadOnlyList<GitHubReleaseAsset> assets) =>
            new(id, data.Tag, data.Name, data.Notes, data.Draft, data.Prerelease, new Uri($"https://github.com/{repository}/releases/tag/{data.Tag}"), assets);
    }
}
