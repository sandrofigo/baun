namespace Baun.CI;

/// <summary>
/// A release of a GitHub repository.
/// </summary>
/// <param name="Id">The ID of the release.</param>
/// <param name="Tag">The tag of the release, such as <c>v1.2.3</c>.</param>
/// <param name="Name">The title of the release.</param>
/// <param name="Notes">The release notes, in Markdown.</param>
/// <param name="Draft">Whether the release is a draft, which only people with write access to the repository see.</param>
/// <param name="Prerelease">Whether the release is marked as a prerelease.</param>
/// <param name="Url">The URL of the release page on GitHub.</param>
/// <param name="Assets">The files attached to the release.</param>
public sealed record GitHubRelease(
    long Id, string Tag, string Name, string? Notes, bool Draft, bool Prerelease, Uri Url, IReadOnlyList<GitHubReleaseAsset> Assets);

/// <summary>
/// A file attached to a GitHub release.
/// </summary>
/// <param name="Id">The ID of the asset.</param>
/// <param name="Name">The file name of the asset.</param>
/// <param name="Size">The size of the asset, in bytes.</param>
/// <param name="DownloadUrl">The URL to download the asset from.</param>
public sealed record GitHubReleaseAsset(long Id, string Name, long Size, Uri DownloadUrl);
