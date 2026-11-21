using System.Diagnostics.CodeAnalysis;
using KeepAChangelog.IO;
using ParsedChangelog = KeepAChangelog.IO.Changelog;

namespace Baun.CI;

/// <summary>
/// A changelog in the <see href="https://keepachangelog.com/">Keep a Changelog</see> format, such as <c>CHANGELOG.md</c>.
/// </summary>
public sealed class Changelog
{
    /// <summary>
    /// The name of the changelog file that <see cref="Read"/> reads by default.
    /// </summary>
    public const string DefaultFileName = "CHANGELOG.md";

    private Changelog(ChangelogRelease? unreleased, IReadOnlyList<ChangelogRelease> releases, ChangelogRelease? latestRelease)
    {
        Unreleased = unreleased;
        Releases = releases;
        LatestRelease = latestRelease;
    }

    /// <summary>
    /// Gets the <c>[Unreleased]</c> section, or <see langword="null"/> if the changelog has none.
    /// </summary>
    public ChangelogRelease? Unreleased { get; }

    /// <summary>
    /// Gets the releases of the changelog in the order of the file, without the <c>[Unreleased]</c> section.
    /// </summary>
    public IReadOnlyList<ChangelogRelease> Releases { get; }

    /// <summary>
    /// Gets the release with the highest version, or <see langword="null"/> if the changelog has no releases.
    /// </summary>
    public ChangelogRelease? LatestRelease { get; }

    /// <summary>
    /// Reads the changelog file at <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The path of the file. Defaults to <c>CHANGELOG.md</c> in <see cref="BaunBuild.RootDirectory"/>.</param>
    /// <exception cref="FileNotFoundException">The file doesn't exist.</exception>
    /// <exception cref="ChangelogFormatException">The file isn't in the Keep a Changelog format.</exception>
    public static Changelog Read(AbsolutePath? path = null)
    {
        path ??= BaunBuild.RootDirectory / DefaultFileName;

        if (!path.FileExists)
        {
            throw new FileNotFoundException($"The changelog {path} doesn't exist.", path);
        }

        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (ChangelogFormatException exception)
        {
            throw new ChangelogFormatException(
                $"{path} isn't a valid Keep a Changelog file. {exception.Message}", exception.LineNumber, exception);
        }
    }

    /// <summary>
    /// Parses a changelog from its text.
    /// </summary>
    /// <exception cref="ChangelogFormatException"><paramref name="text"/> isn't in the Keep a Changelog format.</exception>
    public static Changelog Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        ParsedChangelog parsed;

        try
        {
            parsed = ParsedChangelog.Parse(text);
        }
        catch (ChangelogParseException exception)
        {
            throw new ChangelogFormatException(exception.Message, exception.LineNumber, exception);
        }

        Release? unreleased = parsed.GetUnreleased();
        Release? latest = parsed.GetLatestRelease();

        List<Release> released = parsed.Releases.Where(release => release != unreleased).ToList();
        ChangelogRelease[] releases = released.Select(release => Map(release, parsed.VersionLinks)).ToArray();

        return new Changelog(
            unreleased is null ? null : Map(unreleased, parsed.VersionLinks),
            releases,
            latest is null ? null : releases[released.IndexOf(latest)]);
    }

    /// <summary>
    /// Gets the release with <paramref name="version"/>, such as <c>1.2.3</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The changelog has no release with <paramref name="version"/>.</exception>
    public ChangelogRelease GetRelease(string version) =>
        TryGetRelease(version, out ChangelogRelease? release)
            ? release
            : throw new InvalidOperationException(
                $"The changelog has no section for version {version}. Rename [Unreleased] to [{version}] with the release date and add a new, empty [Unreleased] section above it.");

    /// <summary>
    /// Gets the notes of the release with <paramref name="version"/>, such as <c>1.2.3</c>: its categories and entries
    /// as Markdown, without its <c>## [version] - date</c> heading, as <see cref="ChangelogRelease.Notes"/> returns them.
    /// </summary>
    /// <remarks>
    /// The notes fit a GitHub release, which shows its title instead of the heading:
    /// <c>GitHub.CreateOrUpdateRelease(tag, notes: changelog.GetReleaseNotes("1.2.3"))</c>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The changelog has no release with <paramref name="version"/>.</exception>
    public string GetReleaseNotes(string version) => GetRelease(version).Notes;

    /// <summary>
    /// Tries to get the release with <paramref name="version"/>, such as <c>1.2.3</c>.
    /// </summary>
    /// <returns><see langword="true"/> if the changelog has a release with <paramref name="version"/>.</returns>
    public bool TryGetRelease(string version, [NotNullWhen(true)] out ChangelogRelease? release)
    {
        ArgumentNullException.ThrowIfNull(version);

        release = Releases.FirstOrDefault(candidate => candidate.Version == version);
        return release is not null;
    }

    private static ChangelogRelease Map(Release release, List<VersionLink> links) =>
        new(
            release.Version,
            release.ReleaseDate?.ToDateOnly(),
            release.IsYanked,
            release.Categories
                .OrderBy(category => category.Type)
                .Select(category => new ChangelogCategory(
                    category.Type.ToString(), category.Entries.Select(entry => entry.Text.ReplaceLineEndings("\n")).ToArray()))
                .ToArray())
        {
            Link = links.FirstOrDefault(link => string.Equals(link.Version, release.Version, StringComparison.OrdinalIgnoreCase))?.Url,
        };
}
