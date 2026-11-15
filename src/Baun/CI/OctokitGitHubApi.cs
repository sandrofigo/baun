using Octokit;

namespace Baun.CI;

/// <summary>
/// Calls the GitHub API with Octokit.
/// </summary>
internal sealed class OctokitGitHubApi : IGitHubApi
{
    private readonly GitHubClient client;

    public OctokitGitHubApi(string token, Uri? apiUrl)
    {
        var header = new ProductHeaderValue("baun");

        client = apiUrl is null ? new GitHubClient(header) : new GitHubClient(header, apiUrl);
        client.Credentials = new Credentials(token);
    }

    public async Task<GitHubRelease?> GetReleaseByTag(GitHubRepository repository, string tag, CancellationToken cancellationToken)
    {
        try
        {
            return Map(await client.Repository.Release.Get(repository.Owner, repository.Name, tag));
        }
        catch (NotFoundException)
        {
            return null;
        }
    }

    public async Task<GitHubRelease> CreateRelease(GitHubRepository repository, GitHubReleaseData data, CancellationToken cancellationToken) =>
        Map(await client.Repository.Release.Create(repository.Owner, repository.Name, new NewRelease(data.Tag)
        {
            Name = data.Name,
            Body = data.Notes,
            Prerelease = data.Prerelease,
        }));

    public async Task<GitHubRelease> UpdateRelease(GitHubRepository repository, long id, GitHubReleaseData data, CancellationToken cancellationToken) =>
        Map(await client.Repository.Release.Edit(repository.Owner, repository.Name, id, new ReleaseUpdate
        {
            Name = data.Name,
            Body = data.Notes,
            Prerelease = data.Prerelease,
        }));

    private static GitHubRelease Map(Release release) =>
        new(release.Id, release.TagName, release.Name, release.Body, release.Prerelease, new Uri(release.HtmlUrl));
}
