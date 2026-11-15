namespace Baun.CI;

/// <summary>
/// A release in a changelog, or its <c>[Unreleased]</c> section.
/// </summary>
/// <param name="Version">The version of the release, such as <c>1.2.3</c>, or <c>Unreleased</c> for the <c>[Unreleased]</c> section.</param>
/// <param name="Date">The date of the release, or <see langword="null"/> if it has none, such as the <c>[Unreleased]</c> section.</param>
/// <param name="IsYanked">Whether the release is marked as <c>[YANKED]</c>.</param>
/// <param name="Categories">
/// The categories of the release, in the order of Keep a Changelog: Added, Changed, Deprecated, Removed, Fixed and Security.
/// </param>
public sealed record ChangelogRelease(string Version, DateOnly? Date, bool IsYanked, IReadOnlyList<ChangelogCategory> Categories)
{
    /// <summary>
    /// Gets the categories and entries of the release as Markdown, without its <c>## [version] - date</c> heading, for
    /// example as the notes of a GitHub release, which shows its title instead. Categories without entries are left out,
    /// and lines end with <c>\n</c>.
    /// </summary>
    public string Notes => string.Join("\n\n", Categories.Where(category => category.Entries.Count > 0));
}
