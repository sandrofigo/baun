namespace Baun.CI;

/// <summary>
/// The GitHub API calls that <see cref="GitHubRelease"/> and <see cref="GitHubReleaseBuilder"/> use, so the client
/// library behind them can be exchanged.
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
    Task<GitHubReleaseSnapshot?> GetReleaseByTag(
        GitHubRepository repository, string tag, bool includeDrafts = true, CancellationToken cancellationToken = default);

    Task<GitHubReleaseSnapshot> CreateRelease(GitHubRepository repository, NewGitHubRelease release, CancellationToken cancellationToken);

    /// <summary>
    /// Changes the values of <paramref name="changes"/> that aren't <see langword="null"/> and keeps the others.
    /// </summary>
    Task<GitHubReleaseSnapshot> UpdateRelease(GitHubRepository repository, long id, GitHubReleaseChanges changes, CancellationToken cancellationToken);

    Task DeleteRelease(GitHubRepository repository, long id, CancellationToken cancellationToken);

    /// <summary>
    /// Uploads <paramref name="file"/> as an asset of the release, named after the file.
    /// </summary>
    Task<GitHubReleaseAsset> UploadAsset(GitHubRepository repository, long releaseId, AbsolutePath file, CancellationToken cancellationToken);

    Task DeleteAsset(GitHubRepository repository, long id, CancellationToken cancellationToken);
}

/// <summary>
/// The state of a release as the API returned it.
/// </summary>
internal sealed record GitHubReleaseSnapshot(
    long Id, string Tag, string Name, string? Notes, bool Draft, bool Prerelease, Uri Url, IReadOnlyList<GitHubReleaseAsset> Assets);

/// <summary>
/// The values of a release to create.
/// </summary>
internal sealed record NewGitHubRelease(string Tag, string Name, string? Notes, bool Draft, bool Prerelease);

/// <summary>
/// The values of a release to change. <see langword="null"/> keeps a value.
/// </summary>
internal sealed record GitHubReleaseChanges(string? Name = null, string? Notes = null, bool? Draft = null, bool? Prerelease = null);
