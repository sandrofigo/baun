namespace Baun.CI;

/// <summary>
/// The GitHub API calls that <see cref="GitHub"/> uses, so the client library behind them can be exchanged.
/// </summary>
internal interface IGitHubApi
{
    /// <summary>
    /// Gets the release for <paramref name="tag"/>, including a draft, or <see langword="null"/> if there's none.
    /// </summary>
    Task<GitHubRelease?> GetReleaseByTag(GitHubRepository repository, string tag, CancellationToken cancellationToken);

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
