using Baun.CI;

namespace Baun.Tests;

public sealed class ChangelogTests
{
    private const string Text =
        """
        # Changelog

        ## [Unreleased]

        ### Added

        - Something new

        ## [1.1.0] - 2026-02-03

        ### Fixed

        - A bug

        ### Added

        - A feature
          over two lines
        - Another feature

        ### Removed

        ## [1.0.0] - 2026-01-02 [YANKED]

        ### Added

        - The first version

        [1.1.0]: https://github.com/octocat/Hello-World/compare/v1.0.0...v1.1.0
        [1.0.0]: https://github.com/octocat/Hello-World/releases/tag/v1.0.0
        """;

    [Fact]
    public void Reads_unreleased_and_releases()
    {
        Changelog changelog = Changelog.Parse(Text);

        Assert.NotNull(changelog.Unreleased);
        Assert.Null(changelog.Unreleased.Date);
        Assert.Equal(["Something new"], changelog.Unreleased.Categories.Single().Entries);

        Assert.Equal(["1.1.0", "1.0.0"], changelog.Releases.Select(release => release.Version));
        Assert.Equal("1.1.0", changelog.LatestRelease?.Version);
    }

    [Fact]
    public void Reads_date_and_yanked()
    {
        Changelog changelog = Changelog.Parse(Text);

        ChangelogRelease first = changelog.GetRelease("1.0.0");
        ChangelogRelease second = changelog.GetRelease("1.1.0");

        Assert.Equal(new DateOnly(2026, 1, 2), first.Date);
        Assert.True(first.IsYanked);
        Assert.Equal(new DateOnly(2026, 2, 3), second.Date);
        Assert.False(second.IsYanked);
    }

    [Fact]
    public void Orders_categories_as_keep_a_changelog()
    {
        ChangelogRelease release = Changelog.Parse(Text).GetRelease("1.1.0");

        Assert.Equal(["Added", "Removed", "Fixed"], release.Categories.Select(category => category.Name));
        Assert.Equal(["A feature\nover two lines", "Another feature"], release.Categories[0].Entries);
    }

    [Fact]
    public void Notes_are_the_categories_with_entries_without_the_heading()
    {
        ChangelogRelease release = Changelog.Parse(Text).GetRelease("1.1.0");

        Assert.Equal(
            "### Added\n\n- A feature\n  over two lines\n- Another feature\n\n### Fixed\n\n- A bug",
            release.Notes);
    }

    [Fact]
    public void GetReleaseNotes_returns_the_notes_of_the_release()
    {
        Changelog changelog = Changelog.Parse(Text);

        Assert.Equal(changelog.GetRelease("1.1.0").Notes, changelog.GetReleaseNotes("1.1.0"));
        Assert.Throws<InvalidOperationException>(() => changelog.GetReleaseNotes("2.0.0"));
    }

    [Theory]
    [InlineData("1.1.0")]
    [InlineData("1.0.0")]
    public void GetReleaseNotes_formats_like_keep_a_changelog(string version)
    {
        KeepAChangelog.IO.Release release = KeepAChangelog.IO.Changelog.Parse(Text).Releases.Single(release => release.Version == version);

        // The formatting of the release notes in build/Build.Helpers.cs, with \n line endings.
        string expected = string.Join(
            "\n\n",
            release.Categories
                .Where(category => category.Entries.Count > 0)
                .OrderBy(category => category.Type)
                .Select(category => category.ToString())).ReplaceLineEndings("\n");

        Assert.Equal(expected, Changelog.Parse(Text).GetReleaseNotes(version));
    }

    [Fact]
    public void Reads_the_links_of_releases()
    {
        Changelog changelog = Changelog.Parse(
            Text.Replace("[1.1.0]: ", "[Unreleased]: https://github.com/octocat/Hello-World/compare/v1.1.0...HEAD\n[1.1.0]: "));

        Assert.Equal("https://github.com/octocat/Hello-World/compare/v1.1.0...HEAD", changelog.Unreleased?.Link);
        Assert.Equal("https://github.com/octocat/Hello-World/compare/v1.0.0...v1.1.0", changelog.GetRelease("1.1.0").Link);
        Assert.Equal("https://github.com/octocat/Hello-World/releases/tag/v1.0.0", changelog.GetRelease("1.0.0").Link);
    }

    [Fact]
    public void A_release_without_a_link_has_no_link()
    {
        Changelog changelog = Changelog.Parse(Text.Replace("[1.0.0]: https://github.com/octocat/Hello-World/releases/tag/v1.0.0", ""));

        Assert.Null(changelog.Unreleased?.Link);
        Assert.Null(changelog.GetRelease("1.0.0").Link);
    }

    [Fact]
    public void GetRelease_throws_for_a_missing_version()
    {
        Changelog changelog = Changelog.Parse(Text);

        var exception = Assert.Throws<InvalidOperationException>(() => changelog.GetRelease("2.0.0"));

        Assert.Contains("[2.0.0]", exception.Message);
    }

    [Fact]
    public void TryGetRelease_finds_releases_but_not_unreleased()
    {
        Changelog changelog = Changelog.Parse(Text);

        Assert.True(changelog.TryGetRelease("1.1.0", out ChangelogRelease? release));
        Assert.Equal("1.1.0", release.Version);
        Assert.False(changelog.TryGetRelease("2.0.0", out _));
        Assert.False(changelog.TryGetRelease("Unreleased", out _));
    }

    [Fact]
    public void A_changelog_without_releases_has_no_latest_release()
    {
        Changelog changelog = Changelog.Parse("# Changelog\n\n## [Unreleased]\n");

        Assert.NotNull(changelog.Unreleased);
        Assert.Empty(changelog.Releases);
        Assert.Null(changelog.LatestRelease);
    }

    [Fact]
    public void Throws_with_the_line_for_an_invalid_changelog()
    {
        var exception = Assert.Throws<ChangelogFormatException>(
            () => Changelog.Parse("# Changelog\n\n## [Unreleased]\n\n### Foo\n"));

        Assert.Equal(5, exception.LineNumber);
        Assert.Contains("Foo", exception.Message);
    }

    [Fact]
    public void Reads_a_file()
    {
        var path = (AbsolutePath)Path.GetTempFileName();

        try
        {
            File.WriteAllText(path, Text);

            Assert.Equal("1.1.0", Changelog.Read(path).LatestRelease?.Version);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Read_names_the_file_of_an_invalid_changelog()
    {
        var path = (AbsolutePath)Path.GetTempFileName();

        try
        {
            File.WriteAllText(path, "# Changelog\n\n## [Unreleased]\n\n### Foo\n");

            var exception = Assert.Throws<ChangelogFormatException>(() => Changelog.Read(path));

            Assert.Contains(path, exception.Message);
            Assert.Equal(5, exception.LineNumber);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Read_throws_for_a_missing_file()
    {
        var path = (AbsolutePath)Path.GetTempPath() / Guid.NewGuid().ToString("N") / "CHANGELOG.md";

        Assert.Throws<FileNotFoundException>(() => Changelog.Read(path));
    }
}
