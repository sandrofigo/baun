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

    // The upload URL of a release isn't part of GitHubRelease, so the releases this class returned are kept to upload to.
    private readonly ConcurrentDictionary<long, Release> releases = new();

    public OctokitGitHubApi(string token, Uri? apiUrl)
    {
        var header = new ProductHeaderValue("baun");

        client = apiUrl is null ? new GitHubClient(header) : new GitHubClient(header, apiUrl);
        client.Credentials = new Credentials(token);
    }

    public async Task<GitHubRelease?> GetReleaseByTag(
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

    public async Task<GitHubRelease> CreateRelease(GitHubRepository repository, GitHubReleaseData data, CancellationToken cancellationToken) =>
        Map(await client.Repository.Release.Create(repository.Owner, repository.Name, new NewRelease(data.Tag)
        {
            Name = data.Name,
            Body = data.Notes,
            Draft = data.Draft,
            Prerelease = data.Prerelease,
        }));

    public async Task<GitHubRelease> UpdateRelease(GitHubRepository repository, long id, GitHubReleaseData data, CancellationToken cancellationToken) =>
        Map(await client.Repository.Release.Edit(repository.Owner, repository.Name, id, new ReleaseUpdate
        {
            Name = data.Name,
            Body = data.Notes,
            Draft = data.Draft,
            Prerelease = data.Prerelease,
        }));

    public async Task<GitHubReleaseAsset> UploadAsset(GitHubRepository repository, GitHubRelease release, AbsolutePath file, CancellationToken cancellationToken)
    {
        if (!releases.TryGetValue(release.Id, out Release? octokitRelease))
        {
            octokitRelease = await client.Repository.Release.Get(repository.Owner, repository.Name, release.Id);
        }

        await using FileStream stream = File.OpenRead(file);
        var upload = new ReleaseAssetUpload(file.Name, "application/octet-stream", stream, UploadTimeout);

        return Map(await client.Repository.Release.UploadAsset(octokitRelease, upload, cancellationToken));
    }

    public Task DeleteAsset(GitHubRepository repository, long id, CancellationToken cancellationToken) =>
        client.Repository.Release.DeleteAsset(repository.Owner, repository.Name, checked((int)id));

    private GitHubRelease Map(Release release)
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
