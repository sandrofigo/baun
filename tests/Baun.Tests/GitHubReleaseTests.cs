using Baun.CI.GitHub;

namespace Baun.Tests;

public sealed class GitHubReleaseTests : IDisposable
{
    private static readonly GitHubRepository Repository = new("octocat", "Hello-World");

    private readonly AbsolutePath root = (AbsolutePath)Directory.CreateTempSubdirectory("baun-release-tests-").FullName;
    private readonly AbsolutePath zip;
    private readonly AbsolutePath checksums;

    public GitHubReleaseTests()
    {
        zip = root / "app.zip";
        checksums = root / "checksums.txt";
        File.WriteAllText(zip, "zip");
        File.WriteAllText(checksums, "checksums");
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Gets_an_existing_release()
    {
        var api = new FakeGitHubApi(Existing(draft: true));

        GitHubRelease? release = await GitHubReleases.Get(api, Repository, "v1.2.3", includeDrafts: false, Token);

        Assert.Equal((Repository, "v1.2.3", false), api.Requested);
        Assert.NotNull(release);
        Assert.Equal(42, release.Id);
        Assert.Equal("Old", release.Name);
        Assert.True(release.Draft);
        Assert.Equal(Repository, release.Repository);
    }

    [Fact]
    public async Task Gets_null_without_a_release()
    {
        var api = new FakeGitHubApi(existing: null);

        Assert.Null(await GitHubReleases.Get(api, Repository, "v1.2.3", includeDrafts: true, Token));
    }

    [Fact]
    public void A_new_release_isnt_saved()
    {
        var release = new GitHubRelease { Tag = "v1.2.3" };

        Assert.Null(release.Id);
        Assert.Null(release.Url);
        Assert.Empty(release.Assets);
        Assert.Equal("v1.2.3", release.ToString());
    }

    [Fact]
    public async Task Creates_a_new_release_with_defaults()
    {
        var api = new FakeGitHubApi(existing: null);
        var release = new GitHubRelease { Tag = "v1.2.3" };

        GitHubRelease saved = await GitHubReleases.CreateOrUpdate(api, Repository, release, Token);

        Assert.Same(release, saved);
        Assert.Equal((Repository, "v1.2.3", true), api.Requested);
        Assert.Equal(new NewGitHubRelease("v1.2.3", "v1.2.3", null, Draft: false, Prerelease: false), api.Created);
        Assert.Empty(api.Updated);
        Assert.Equal(1, release.Id);
        Assert.Equal("v1.2.3", release.Name);
        Assert.NotNull(release.Url);
        Assert.Equal(Repository, release.Repository);
    }

    [Fact]
    public async Task Creates_a_new_release_with_its_values()
    {
        var api = new FakeGitHubApi(existing: null);
        var release = new GitHubRelease { Tag = "v1.2.3", Name = "Version 1.2.3", Notes = "- Fixed a bug", Draft = true, Prerelease = true };

        await GitHubReleases.CreateOrUpdate(api, Repository, release, Token);

        Assert.Equal(new NewGitHubRelease("v1.2.3", "Version 1.2.3", "- Fixed a bug", Draft: true, Prerelease: true), api.Created);
        Assert.True(release.Draft);
    }

    [Fact]
    public async Task Updates_an_existing_release_with_the_same_tag()
    {
        var api = new FakeGitHubApi(Existing(draft: false));
        var release = new GitHubRelease { Tag = "v1.2.3", Notes = "- Fixed a bug" };

        await GitHubReleases.CreateOrUpdate(api, Repository, release, Token);

        Assert.Null(api.Created);
        Assert.Equal([(42L, new GitHubReleaseChanges("v1.2.3", null, "- Fixed a bug", Draft: false, Prerelease: false))], api.Updated);
        Assert.Equal(42, release.Id);
        Assert.Equal("Old", release.Name);
        Assert.Equal("- Fixed a bug", release.Notes);
    }

    [Fact]
    public async Task Updates_a_got_release_by_its_id_without_looking_it_up()
    {
        var api = new FakeGitHubApi(Existing(draft: false));
        var release = new GitHubRelease(Repository, Existing(draft: false));

        release.Name = "Version 1.2.3";
        release.Tag = "v1.2.4";
        await GitHubReleases.CreateOrUpdate(api, Repository, release, Token);

        Assert.Null(api.Requested);
        Assert.Equal([(42L, new GitHubReleaseChanges("v1.2.4", "Version 1.2.3", "Old notes", Draft: false, Prerelease: false))], api.Updated);
        Assert.Equal("Version 1.2.3", release.Name);
        Assert.Equal("v1.2.4", release.Tag);
    }

    [Fact]
    public async Task Publishes_a_draft()
    {
        var api = new FakeGitHubApi(Existing(draft: true));
        var release = new GitHubRelease(Repository, Existing(draft: true)) { Draft = false };

        await GitHubReleases.CreateOrUpdate(api, Repository, release, Token);

        Assert.False(api.Updated.Single().Changes.Draft);
        Assert.False(release.Draft);
    }

    [Fact]
    public async Task Keeps_a_draft_a_draft()
    {
        var api = new FakeGitHubApi(Existing(draft: true));
        var release = new GitHubRelease(Repository, Existing(draft: true));

        release.AddAsset(zip);
        await GitHubReleases.CreateOrUpdate(api, Repository, release, Token);

        Assert.Equal(["update", "upload app.zip"], api.Calls);
        Assert.True(release.Draft);
    }

    [Fact]
    public async Task Publishes_a_new_release_with_assets_after_the_uploads()
    {
        var api = new FakeGitHubApi(existing: null);
        var release = new GitHubRelease { Tag = "v1.2.3" }.AddAsset(zip).AddAsset(checksums);

        await GitHubReleases.CreateOrUpdate(api, Repository, release, Token);

        Assert.True(api.Created!.Draft);
        Assert.Equal(["create", "upload app.zip", "upload checksums.txt", "update"], api.Calls);
        Assert.Equal(new GitHubReleaseChanges(Draft: false), api.Updated.Single().Changes);
        Assert.False(release.Draft);
        Assert.Equal(["app.zip", "checksums.txt"], release.Assets.Select(asset => asset.Name));
        Assert.Empty(release.AddedAssets);
    }

    [Fact]
    public async Task Publishes_a_draft_with_assets_after_the_uploads()
    {
        var api = new FakeGitHubApi(Existing(draft: true));
        var release = new GitHubRelease(Repository, Existing(draft: true)) { Draft = false }.AddAsset(zip);

        await GitHubReleases.CreateOrUpdate(api, Repository, release, Token);

        Assert.Equal(["update", "upload app.zip", "update"], api.Calls);
        Assert.Equal([true, false], api.Updated.Select(update => update.Changes.Draft));
        Assert.False(release.Draft);
    }

    [Fact]
    public async Task Keeps_a_published_release_published_while_uploading()
    {
        var api = new FakeGitHubApi(Existing(draft: false));
        var release = new GitHubRelease(Repository, Existing(draft: false)).AddAsset(zip);

        await GitHubReleases.CreateOrUpdate(api, Repository, release, Token);

        Assert.Equal(["update", "upload app.zip"], api.Calls);
        Assert.False(api.Updated.Single().Changes.Draft);
    }

    [Fact]
    public async Task Replaces_attached_assets_with_the_same_name_and_keeps_the_others()
    {
        GitHubReleaseSnapshot existing = Existing(draft: false) with { Assets = [Asset(7, "app.zip"), Asset(8, "readme.txt")] };
        var api = new FakeGitHubApi(existing);
        var release = new GitHubRelease { Tag = "v1.2.3" }.AddAsset(zip);

        await GitHubReleases.CreateOrUpdate(api, Repository, release, Token);

        Assert.Equal(["update", "delete 7", "upload app.zip"], api.Calls);
        Assert.Equal(["readme.txt", "app.zip"], release.Assets.Select(asset => asset.Name));
        Assert.NotEqual(7, release.Assets.Single(asset => asset.Name == "app.zip").Id);
    }

    [Fact]
    public async Task Deletes_removed_assets()
    {
        GitHubReleaseSnapshot existing = Existing(draft: false) with { Assets = [Asset(7, "app.zip"), Asset(8, "readme.txt")] };
        var api = new FakeGitHubApi(existing);
        var release = new GitHubRelease(Repository, existing);

        Assert.True(release.RemoveAsset("APP.zip"));
        Assert.Equal([7L], release.RemovedAssets.Select(asset => asset.Id));
        Assert.Equal(2, release.Assets.Count);

        await GitHubReleases.CreateOrUpdate(api, Repository, release, Token);

        Assert.Equal(["update", "delete 7"], api.Calls);
        Assert.Equal(["readme.txt"], release.Assets.Select(asset => asset.Name));
        Assert.Empty(release.RemovedAssets);
    }

    [Fact]
    public void Removes_an_added_asset()
    {
        var release = new GitHubRelease { Tag = "v1.2.3" }.AddAssets([zip, checksums]);

        Assert.True(release.RemoveAsset("app.zip"));
        Assert.False(release.RemoveAsset("missing.zip"));

        Assert.Equal([checksums], release.AddedAssets);
    }

    [Fact]
    public void Throws_for_an_added_asset_with_the_same_name()
    {
        AbsolutePath other = (root / "other").CreateDirectory() / "APP.zip";
        var release = new GitHubRelease { Tag = "v1.2.3" }.AddAsset(zip);

        Assert.Throws<ArgumentException>(() => release.AddAsset(other));
    }

    [Fact]
    public async Task Throws_for_a_missing_asset_before_calling_the_api()
    {
        var api = new FakeGitHubApi(existing: null);
        var release = new GitHubRelease { Tag = "v1.2.3" }.AddAsset(root / "missing.zip");

        await Assert.ThrowsAsync<FileNotFoundException>(() => GitHubReleases.CreateOrUpdate(api, Repository, release, Token));

        Assert.Null(api.Requested);
    }

    [Fact]
    public async Task Throws_for_an_empty_tag()
    {
        var api = new FakeGitHubApi(existing: null);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            GitHubReleases.CreateOrUpdate(api, Repository, new GitHubRelease { Tag = "" }, Token));
    }

    [Fact]
    public async Task Deleting_makes_the_release_new_again()
    {
        var api = new FakeGitHubApi(Existing(draft: false));
        var release = new GitHubRelease(Repository, Existing(draft: false) with { Assets = [Asset(7, "app.zip")] });

        await GitHubReleases.Delete(api, Repository, release, Token);

        Assert.Equal(["delete release 42"], api.Calls);
        Assert.Null(release.Id);
        Assert.Null(release.Url);
        Assert.Empty(release.Assets);
        Assert.Equal("v1.2.3", release.Tag);
    }

    private static GitHubReleaseSnapshot Existing(bool draft) =>
        new(42, "v1.2.3", "Old", "Old notes", draft, false, new Uri("https://github.com/octocat/Hello-World/releases/tag/v1.2.3"), []);

    private static GitHubReleaseAsset Asset(long id, string name) =>
        new(id, name, 100, new Uri($"https://github.com/octocat/Hello-World/releases/download/v1.2.3/{name}"));

    private sealed class FakeGitHubApi(GitHubReleaseSnapshot? existing) : IGitHubApi
    {
        private long nextAssetId = 100;

        public (GitHubRepository Repository, string Tag, bool IncludeDrafts)? Requested { get; private set; }

        public NewGitHubRelease? Created { get; private set; }

        public List<(long Id, GitHubReleaseChanges Changes)> Updated { get; } = [];

        public List<string> Calls { get; } = [];

        public Task<GitHubReleaseSnapshot?> GetReleaseByTag(
            GitHubRepository repository, string tag, bool includeDrafts = true, CancellationToken cancellationToken = default)
        {
            Requested = (repository, tag, includeDrafts);
            return Task.FromResult(existing);
        }

        public Task<GitHubReleaseSnapshot> CreateRelease(GitHubRepository repository, NewGitHubRelease release, CancellationToken cancellationToken)
        {
            Calls.Add("create");
            Created = release;
            existing = new GitHubReleaseSnapshot(
                1, release.Tag, release.Name, release.Notes, release.Draft, release.Prerelease, new Uri($"https://github.com/{repository}/releases/tag/{release.Tag}"), []);
            return Task.FromResult(existing);
        }

        public Task<GitHubReleaseSnapshot> UpdateRelease(GitHubRepository repository, long id, GitHubReleaseChanges changes, CancellationToken cancellationToken)
        {
            Calls.Add("update");
            Updated.Add((id, changes));
            existing = existing! with
            {
                Tag = changes.Tag ?? existing.Tag,
                Name = changes.Name ?? existing.Name,
                Notes = changes.Notes ?? existing.Notes,
                Draft = changes.Draft ?? existing.Draft,
                Prerelease = changes.Prerelease ?? existing.Prerelease,
            };
            return Task.FromResult(existing);
        }

        public Task DeleteRelease(GitHubRepository repository, long id, CancellationToken cancellationToken)
        {
            Calls.Add($"delete release {id}");
            return Task.CompletedTask;
        }

        public Task<GitHubReleaseAsset> UploadAsset(GitHubRepository repository, long releaseId, AbsolutePath file, CancellationToken cancellationToken)
        {
            Calls.Add($"upload {file.Name}");
            GitHubReleaseAsset asset = Asset(nextAssetId++, file.Name);
            existing = existing! with { Assets = [.. existing.Assets, asset] };
            return Task.FromResult(asset);
        }

        public Task DeleteAsset(GitHubRepository repository, long id, CancellationToken cancellationToken)
        {
            Calls.Add($"delete {id}");
            existing = existing! with { Assets = [.. existing.Assets.Where(asset => asset.Id != id)] };
            return Task.CompletedTask;
        }
    }
}
