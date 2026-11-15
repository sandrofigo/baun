namespace Baun.CI;

internal static class GitHubReleases
{
    /// <summary>
    /// Creates the release for the tag of <paramref name="data"/> in <paramref name="repository"/>, or updates it if
    /// it already exists.
    /// </summary>
    public static async Task<GitHubRelease> CreateOrUpdate(
        IGitHubApi api, GitHubRepository repository, GitHubReleaseData data, CancellationToken cancellationToken)
    {
        GitHubRelease? existing = await api.GetReleaseByTag(repository, data.Tag, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        return existing is null
            ? await api.CreateRelease(repository, data, cancellationToken)
            : await api.UpdateRelease(repository, existing.Id, data, cancellationToken);
    }
}
