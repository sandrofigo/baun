using Baun.CI;

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
    public async Task Creates_the_release_with_defaults_if_it_doesnt_exist()
    {
        var api = new FakeGitHubApi(existing: null);

        GitHubRelease release = await GitHub.Release("v1.2.3").CreateOrUpdateCoreAsync(api, Repository, Token);

        Assert.Equal((Repository, "v1.2.3", true), api.Requested);
        Assert.Equal(new NewGitHubRelease("v1.2.3", "v1.2.3", null, Draft: false, Prerelease: false), api.Created);
        Assert.Empty(api.Updated);
        Assert.Equal("v1.2.3", release.Tag);
        Assert.Equal("v1.2.3", release.Name);
        Assert.Equal(Repository, release.Repository);
    }

    [Fact]
    public async Task Creates_the_release_with_the_set_values()
    {
        var api = new FakeGitHubApi(existing: null);

        GitHubRelease release = await GitHub.Release("v1.2.3")
            .Name("Version 1.2.3")
            .Notes("- Fixed a bug")
            .Prerelease()
            .Draft()
            .CreateOrUpdateCoreAsync(api, Repository, Token);

        Assert.Equal(new NewGitHubRelease("v1.2.3", "Version 1.2.3", "- Fixed a bug", Draft: true, Prerelease: true), api.Created);
        Assert.True(release.Draft);
        Assert.True(release.Prerelease);
        Assert.Equal("- Fixed a bug", release.Notes);
    }

    [Fact]
    public async Task Updates_only_the_set_values_of_an_existing_release()
    {
        var api = new FakeGitHubApi(Existing(draft: false));

        GitHubRelease release = await GitHub.Release("v1.2.3").Notes("- Fixed a bug").CreateOrUpdateCoreAsync(api, Repository, Token);

        Assert.Null(api.Created);
        Assert.Equal([(42L, new GitHubReleaseChanges(Notes: "- Fixed a bug"))], api.Updated);
        Assert.Equal(42, release.Id);
        Assert.Equal("Old", release.Name);
        Assert.Equal("- Fixed a bug", release.Notes);
    }

    [Fact]
    public async Task Keeps_an_existing_draft_a_draft()
    {
        var api = new FakeGitHubApi(Existing(draft: true));

        GitHubRelease release = await GitHub.Release("v1.2.3").Assets(zip).CreateOrUpdateCoreAsync(api, Repository, Token);

        Assert.Equal(["update", "upload app.zip"], api.Calls);
        Assert.True(release.Draft);
    }

    [Fact]
    public async Task Publishes_an_existing_draft()
    {
        var api = new FakeGitHubApi(Existing(draft: true));

        GitHubRelease release = await GitHub.Release("v1.2.3").Draft(false).CreateOrUpdateCoreAsync(api, Repository, Token);

        Assert.Equal([(42L, new GitHubReleaseChanges(Draft: false))], api.Updated);
        Assert.False(release.Draft);
    }

    [Fact]
    public async Task Publishes_a_new_release_with_assets_after_the_uploads()
    {
        var api = new FakeGitHubApi(existing: null);

        GitHubRelease release = await GitHub.Release("v1.2.3").Assets(zip, checksums).CreateOrUpdateCoreAsync(api, Repository, Token);

        Assert.True(api.Created!.Draft);
        Assert.Equal(["create", "upload app.zip", "upload checksums.txt", "update"], api.Calls);
        Assert.Equal([(1L, new GitHubReleaseChanges(Draft: false))], api.Updated);
        Assert.False(release.Draft);
        Assert.Equal(["app.zip", "checksums.txt"], release.Assets.Select(asset => asset.Name));
    }

    [Fact]
    public async Task Publishes_an_existing_draft_with_assets_after_the_uploads()
    {
        var api = new FakeGitHubApi(Existing(draft: true));

        GitHubRelease release = await GitHub.Release("v1.2.3").Draft(false).Assets(zip).CreateOrUpdateCoreAsync(api, Repository, Token);

        Assert.Equal(["update", "upload app.zip", "update"], api.Calls);
        Assert.Equal([(42L, new GitHubReleaseChanges()), (42L, new GitHubReleaseChanges(Draft: false))], api.Updated);
        Assert.False(release.Draft);
    }

    [Fact]
    public async Task Keeps_a_published_release_published_while_uploading()
    {
        var api = new FakeGitHubApi(Existing(draft: false));

        await GitHub.Release("v1.2.3").Assets(zip).CreateOrUpdateCoreAsync(api, Repository, Token);

        Assert.Equal(["update", "upload app.zip"], api.Calls);
    }

    [Fact]
    public async Task Replaces_assets_with_the_same_name_and_keeps_the_others()
    {
        GitHubReleaseSnapshot existing = Existing(draft: false) with { Assets = [Asset(7, "app.zip"), Asset(8, "readme.txt")] };
        var api = new FakeGitHubApi(existing);

        GitHubRelease release = await GitHub.Release("v1.2.3").Assets(zip).CreateOrUpdateCoreAsync(api, Repository, Token);

        Assert.Equal(["update", "delete 7", "upload app.zip"], api.Calls);
        Assert.Equal(["readme.txt", "app.zip"], release.Assets.Select(asset => asset.Name));
        Assert.NotEqual(7, release.Assets.Single(asset => asset.Name == "app.zip").Id);
    }

    [Fact]
    public async Task Throws_for_a_missing_asset_before_calling_the_api()
    {
        var api = new FakeGitHubApi(existing: null);

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            GitHub.Release("v1.2.3").Assets(root / "missing.zip").CreateOrUpdateCoreAsync(api, Repository, Token));

        Assert.Null(api.Requested);
    }

    [Fact]
    public async Task Throws_for_assets_with_the_same_name_before_calling_the_api()
    {
        var api = new FakeGitHubApi(existing: null);
        AbsolutePath other = (root / "other").CreateDirectory() / "app.zip";
        File.WriteAllText(other, "zip");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            GitHub.Release("v1.2.3").Assets(zip).Assets(other).CreateOrUpdateCoreAsync(api, Repository, Token));

        Assert.Null(api.Requested);
    }

    [Fact]
    public void Builder_methods_return_a_new_builder()
    {
        GitHubReleaseBuilder builder = GitHub.Release("v1.2.3");
        GitHubReleaseBuilder withNotes = builder.Notes("- Fixed a bug");

        Assert.NotSame(builder, withNotes);
        Assert.Equal("v1.2.3", withNotes.Tag);
    }

    [Fact]
    public async Task Builder_methods_dont_change_the_original_builder()
    {
        var api = new FakeGitHubApi(existing: null);
        GitHubReleaseBuilder builder = GitHub.Release("v1.2.3");
        _ = builder.Notes("- Fixed a bug").Assets(zip);

        await builder.CreateOrUpdateCoreAsync(api, Repository, Token);

        Assert.Null(api.Created!.Notes);
        Assert.Equal(["create"], api.Calls);
    }

    [Fact]
    public async Task Gets_the_existing_release()
    {
        var api = new FakeGitHubApi(Existing(draft: true));

        GitHubRelease? release = await GitHubRelease.GetAsync(api, Repository, "v1.2.3", includeDrafts: false, Token);

        Assert.Equal((Repository, "v1.2.3", false), api.Requested);
        Assert.Equal(42, release?.Id);
    }

    [Fact]
    public async Task Gets_null_without_a_release()
    {
        var api = new FakeGitHubApi(existing: null);

        Assert.Null(await GitHubRelease.GetAsync(api, Repository, "v1.2.3", includeDrafts: true, Token));
    }

    [Fact]
    public async Task Updates_only_the_passed_values()
    {
        var api = new FakeGitHubApi(Existing(draft: true));
        var release = new GitHubRelease(api, Repository, Existing(draft: true));

        await release.UpdateAsync(name: "Version 1.2.3", cancellationToken: Token);

        Assert.Equal([(42L, new GitHubReleaseChanges(Name: "Version 1.2.3"))], api.Updated);
        Assert.Equal("Version 1.2.3", release.Name);
        Assert.Equal("Old notes", release.Notes);
        Assert.True(release.Draft);
    }

    [Fact]
    public async Task Updates_several_values_in_one_call()
    {
        var api = new FakeGitHubApi(Existing(draft: false));
        var release = new GitHubRelease(api, Repository, Existing(draft: false));

        await release.UpdateAsync(notes: "- Fixed a bug", prerelease: true, cancellationToken: Token);

        Assert.Equal([(42L, new GitHubReleaseChanges(Notes: "- Fixed a bug", Prerelease: true))], api.Updated);
        Assert.Equal("- Fixed a bug", release.Notes);
        Assert.True(release.Prerelease);
    }

    [Fact]
    public async Task Updating_nothing_does_nothing()
    {
        var api = new FakeGitHubApi(Existing(draft: false));
        var release = new GitHubRelease(api, Repository, Existing(draft: false));

        await release.UpdateAsync(cancellationToken: Token);

        Assert.Empty(api.Calls);
    }

    [Fact]
    public async Task Update_throws_for_an_empty_name()
    {
        var release = new GitHubRelease(new FakeGitHubApi(Existing(draft: false)), Repository, Existing(draft: false));

        await Assert.ThrowsAsync<ArgumentException>(() => release.UpdateAsync(name: "", cancellationToken: Token));
    }

    [Fact]
    public async Task Publishes_a_draft()
    {
        var api = new FakeGitHubApi(Existing(draft: true));
        var release = new GitHubRelease(api, Repository, Existing(draft: true));

        await release.PublishAsync(Token);

        Assert.Equal([(42L, new GitHubReleaseChanges(Draft: false))], api.Updated);
        Assert.False(release.Draft);
    }

    [Fact]
    public async Task Publishing_a_published_release_does_nothing()
    {
        var api = new FakeGitHubApi(Existing(draft: false));
        var release = new GitHubRelease(api, Repository, Existing(draft: false));

        await release.PublishAsync(Token);

        Assert.Empty(api.Calls);
    }

    [Fact]
    public async Task Uploads_an_asset()
    {
        var api = new FakeGitHubApi(Existing(draft: false));
        var release = new GitHubRelease(api, Repository, Existing(draft: false));

        GitHubReleaseAsset asset = await release.UploadAssetAsync(zip, Token);

        Assert.Equal("app.zip", asset.Name);
        Assert.Equal([asset], release.Assets);
    }

    [Fact]
    public async Task Deletes_an_asset_by_name_ignoring_case()
    {
        GitHubReleaseSnapshot snapshot = Existing(draft: false) with { Assets = [Asset(7, "app.zip"), Asset(8, "readme.txt")] };
        var api = new FakeGitHubApi(snapshot);
        var release = new GitHubRelease(api, Repository, snapshot);

        Assert.True(await release.DeleteAssetAsync("APP.zip", Token));
        Assert.False(await release.DeleteAssetAsync("missing.zip", Token));

        Assert.Equal(["delete 7"], api.Calls);
        Assert.Equal(["readme.txt"], release.Assets.Select(asset => asset.Name));
    }

    [Fact]
    public async Task Cant_be_used_after_it_is_deleted()
    {
        var api = new FakeGitHubApi(Existing(draft: false));
        var release = new GitHubRelease(api, Repository, Existing(draft: false));

        await release.DeleteAsync(Token);

        Assert.Equal(["delete release 42"], api.Calls);
        await Assert.ThrowsAsync<InvalidOperationException>(() => release.PublishAsync(Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => release.UploadAssetAsync(zip, Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => release.DeleteAsync(Token));
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
                1, release.Tag, release.Name, release.Notes, release.Draft, release.Prerelease, ReleaseUrl(repository, release.Tag), []);
            return Task.FromResult(existing);
        }

        public Task<GitHubReleaseSnapshot> UpdateRelease(GitHubRepository repository, long id, GitHubReleaseChanges changes, CancellationToken cancellationToken)
        {
            Calls.Add("update");
            Updated.Add((id, changes));
            existing = existing! with
            {
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

        private static Uri ReleaseUrl(GitHubRepository repository, string tag) => new($"https://github.com/{repository}/releases/tag/{tag}");
    }
}
