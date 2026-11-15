namespace Baun.CI;

/// <summary>
/// A release of a GitHub repository.
/// </summary>
/// <param name="Id">The ID of the release.</param>
/// <param name="Tag">The tag of the release, such as <c>v1.2.3</c>.</param>
/// <param name="Name">The title of the release.</param>
/// <param name="Notes">The release notes, in Markdown.</param>
/// <param name="Prerelease">Whether the release is marked as a prerelease.</param>
/// <param name="Url">The URL of the release page on GitHub.</param>
public sealed record GitHubRelease(long Id, string Tag, string Name, string? Notes, bool Prerelease, Uri Url);
