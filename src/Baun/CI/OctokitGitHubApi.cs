using System.Collections.Concurrent;
using Octokit;

namespace Baun.CI;

/// <summary>
/// Calls the GitHub API with Octokit.
/// </summary>
internal sealed class OctokitGitHubApi : IGitHubApi
{
    private static readonly TimeSpan UploadTimeout = TimeSpan.FromHours(1);

    private readonly GitHubClient client;

    // Octokit uploads to the upload URL of a release, so the releases this class returned are kept to upload to.
    private readonly ConcurrentDictionary<long, Release> releases = new();

    public OctokitGitHubApi(string token, Uri? apiUrl)
    {
        var header = new ProductHeaderValue("baun");

        client = apiUrl is null ? new GitHubClient(header) : new GitHubClient(header, apiUrl);
        client.Credentials = new Credentials(token);
    }

    public async Task<GitHubReleaseSnapshot?> GetReleaseByTag(
        GitHubRepository repository, string tag, bool includeDrafts = true, CancellationToken cancellationToken = default)
    {
        try
        {
            return Map(await client.Repository.Release.Get(repository.Owner, repository.Name, tag));
        }
        catch (NotFoundException) when (includeDrafts)
        {
        }
        catch (NotFoundException)
        {
            return null;
        }

        // Getting a release by its tag doesn't find drafts, which only the list of all releases has.
        const int pageSize = 100;

        for (int page = 1; ; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<Release> all = await client.Repository.Release.GetAll(
                repository.Owner, repository.Name, new ApiOptions { PageSize = pageSize, PageCount = 1, StartPage = page });

            if (all.FirstOrDefault(release => release.Draft && release.TagName == tag) is { } draft)
            {
                return Map(draft);
            }

            if (all.Count < pageSize)
            {
                return null;
            }
        }
    }

    public async Task<GitHubReleaseSnapshot> CreateRelease(GitHubRepository repository, NewGitHubRelease release, CancellationToken cancellationToken) =>
        Map(await client.Repository.Release.Create(repository.Owner, repository.Name, new NewRelease(release.Tag)
        {
            Name = release.Name,
            Body = release.Notes,
            Draft = release.Draft,
            Prerelease = release.Prerelease,
        }));

    // Octokit leaves out the properties that are null, so GitHub keeps their values.
    public async Task<GitHubReleaseSnapshot> UpdateRelease(GitHubRepository repository, long id, GitHubReleaseChanges changes, CancellationToken cancellationToken) =>
        Map(await client.Repository.Release.Edit(repository.Owner, repository.Name, id, new ReleaseUpdate
        {
            TagName = changes.Tag,
            Name = changes.Name,
            Body = changes.Notes,
            Draft = changes.Draft,
            Prerelease = changes.Prerelease,
        }));

    public async Task DeleteRelease(GitHubRepository repository, long id, CancellationToken cancellationToken)
    {
        await client.Repository.Release.Delete(repository.Owner, repository.Name, id);
        releases.TryRemove(id, out _);
    }

    public async Task<GitHubReleaseAsset> UploadAsset(GitHubRepository repository, long releaseId, AbsolutePath file, CancellationToken cancellationToken)
    {
        if (!releases.TryGetValue(releaseId, out Release? release))
        {
            Map(await client.Repository.Release.Get(repository.Owner, repository.Name, releaseId));
            release = releases[releaseId];
        }

        await using FileStream stream = File.OpenRead(file);
        var upload = new ReleaseAssetUpload(file.Name, "application/octet-stream", stream, UploadTimeout);

        return Map(await client.Repository.Release.UploadAsset(release, upload, cancellationToken));
    }

    public Task DeleteAsset(GitHubRepository repository, long id, CancellationToken cancellationToken) =>
        client.Repository.Release.DeleteAsset(repository.Owner, repository.Name, checked((int)id));

    private GitHubReleaseSnapshot Map(Release release)
    {
        releases[release.Id] = release;

        return new(
            release.Id,
            release.TagName,
            release.Name,
            release.Body,
            release.Draft,
            release.Prerelease,
            new Uri(release.HtmlUrl),
            [.. release.Assets.Select(Map)]);
    }

    private static GitHubReleaseAsset Map(ReleaseAsset asset) =>
        new(asset.Id, asset.Name, asset.Size, new Uri(asset.BrowserDownloadUrl));
}
