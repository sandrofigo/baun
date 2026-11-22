namespace Baun.CI;

/// <summary>
/// The GitHub API calls that <see cref="GitHub"/> uses, so the client library behind them can be exchanged.
/// </summary>
internal interface IGitHubApi
{
    /// <summary>
    /// Gets the release for <paramref name="tag"/>, or <see langword="null"/> if there's none.
    /// </summary>
    /// <param name="repository">The repository of the release.</param>
    /// <param name="tag">The tag of the release, such as <c>v1.2.3</c>.</param>
    /// <param name="includeDrafts">
    /// Whether to also find a draft release for <paramref name="tag"/>, which needs to list the releases because getting
    /// a release by its tag doesn't find drafts.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task<GitHubRelease?> GetReleaseByTag(
        GitHubRepository repository, string tag, bool includeDrafts = true, CancellationToken cancellationToken = default);

    Task<GitHubRelease> CreateRelease(GitHubRepository repository, GitHubReleaseData data, CancellationToken cancellationToken);

    Task<GitHubRelease> UpdateRelease(GitHubRepository repository, long id, GitHubReleaseData data, CancellationToken cancellationToken);

    /// <summary>
    /// Uploads <paramref name="file"/> as an asset of <paramref name="release"/>, named after the file.
    /// </summary>
    Task<GitHubReleaseAsset> UploadAsset(GitHubRepository repository, GitHubRelease release, AbsolutePath file, CancellationToken cancellationToken);

    Task DeleteAsset(GitHubRepository repository, long id, CancellationToken cancellationToken);
}

/// <summary>
/// The values of a release to create or update.
/// </summary>
internal sealed record GitHubReleaseData(string Tag, string Name, string? Notes, bool Draft, bool Prerelease);
