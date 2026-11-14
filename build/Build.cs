// The build of baun itself.
//
// Run the tests:            baun
// Check the changelog:      baun ValidateChangelog
// Without the tool:         dotnet run --project build -- ValidateChangelog
//
// Releases run in the Release workflow when a tag such as v1.2.3 is pushed (see docs/releasing.md):
// the build job runs Pack, the publish job runs GitHubRelease.

using System.Text.RegularExpressions;
using KeepAChangelog.IO;

[Entry(nameof(Test))]
partial class Build
{
    const string Solution = "Baun.slnx";
    const string ChangelogFile = "CHANGELOG.md";
    const string ArtifactsDirectory = "artifacts";

    /// <summary>
    /// The tag being released, for example v1.2.3, or null when the build doesn't run for a tag.
    /// </summary>
    static readonly string? Tag = Environment.GetEnvironmentVariable("GITHUB_REF_TYPE") == "tag"
        ? Environment.GetEnvironmentVariable("GITHUB_REF_NAME")
        : null;

    Step ValidateChangelog() => _ => _
        .Executes(() =>
        {
            Changelog changelog = ReadChangelog();

            if (Tag is null)
            {
                Console.WriteLine($"{ChangelogFile} is valid.");
                return;
            }

            string version = GetVersion();
            Release release = GetRelease(changelog, version);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            if (release.ReleaseDate is not { } releaseDate)
            {
                throw new InvalidOperationException($"The section [{version}] in {ChangelogFile} has no date. Add today's date: ## [{version}] - {today:yyyy-MM-dd}");
            }

            if (releaseDate.ToDateOnly() != today)
            {
                throw new InvalidOperationException($"The section [{version}] in {ChangelogFile} is dated {releaseDate.ToDateOnly():yyyy-MM-dd}, but the release is made today ({today:yyyy-MM-dd}, UTC). Update the date and tag again.");
            }

            if (release.IsYanked)
            {
                throw new InvalidOperationException($"The section [{version}] in {ChangelogFile} is marked as [YANKED].");
            }

            if (changelog.GetLatestRelease()?.Version != version)
            {
                throw new InvalidOperationException($"The section [{version}] isn't the latest release in {ChangelogFile}. It must come right after [Unreleased].");
            }

            if (!changelog.VersionLinks.Any(link => link.Version == version))
            {
                throw new InvalidOperationException($"{ChangelogFile} has no comparison link for [{version}]. Add one at the end of the file, for example: [{version}]: https://github.com/sandrofigo/baun/compare/vPREVIOUS...{Tag}");
            }

            // Fails if the section has no entries.
            RenderReleaseNotes(release);

            Console.WriteLine($"{ChangelogFile} is valid and has a section for {version}, dated today.");
        });

    Step Test() => _ => _
        .Executes(async () =>
        {
            await Run("dotnet", "test", "--solution", Solution, "--configuration", "Release");
        });

    Step Pack() => _ => _
        .DependsOn(ValidateChangelog, Test)
        .Executes(async () =>
        {
            List<string> arguments = ["pack", Solution, "--configuration", "Release", "--output", ArtifactsDirectory];

            if (Tag is not null)
            {
                arguments.Add($"-p:Version={GetVersion()}");
                arguments.Add("-p:ContinuousIntegrationBuild=true");
            }

            await Run("dotnet", [.. arguments]);
        });

    /// <summary>
    /// Pushes the packages in the artifacts folder to nuget.org.
    /// </summary>
    /// <remarks>
    /// Doesn't depend on <see cref="ValidateChangelog"/>: the publish job can be approved on a later day than the
    /// packages were built, and the changelog's date was already checked then.
    /// </remarks>
    Step PushPackages() => _ => _
        .Executes(async () =>
        {
            string apiKey = GetRequiredEnvironmentVariable("NUGET_API_KEY");

            await Run(
                "dotnet", "nuget", "push", Path.Combine(ArtifactsDirectory, "*.nupkg"),
                "--api-key", apiKey,
                "--source", "https://api.nuget.org/v3/index.json",
                "--skip-duplicate");
        });

    /// <summary>
    /// Creates the GitHub release for the tag, with the changelog entries of the released version as its notes.
    /// </summary>
    Step GitHubRelease() => _ => _
        .DependsOn(PushPackages)
        .Executes(async () =>
        {
            string version = GetVersion();
            string notes = RenderReleaseNotes(GetRelease(ReadChangelog(), version));

            await CreateOrUpdateGitHubRelease(Tag!, notes, prerelease: version.Contains('-'));
        });

    static Changelog ReadChangelog()
    {
        try
        {
            return Changelog.FromFile(ChangelogFile);
        }
        catch (ChangelogParseException exception)
        {
            throw new InvalidOperationException($"{ChangelogFile} isn't a valid Keep a Changelog file. {exception.Message}", exception);
        }
    }

    static Release GetRelease(Changelog changelog, string version) =>
        changelog.Releases.FirstOrDefault(release => release.Version == version)
        ?? throw new InvalidOperationException($"{ChangelogFile} has no section for version {version}. Rename [Unreleased] to [{version}] with today's date and add a new, empty [Unreleased] section above it.");

    /// <summary>
    /// Gets the version from the tag, for example 1.2.3 from v1.2.3.
    /// </summary>
    static string GetVersion()
    {
        if (Tag is null)
        {
            throw new InvalidOperationException("This step only runs for a release tag (GITHUB_REF_TYPE=tag and GITHUB_REF_NAME=v1.2.3).");
        }

        if (!Regex.IsMatch(Tag, @"^v[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$"))
        {
            throw new InvalidOperationException($"The tag '{Tag}' is not a version such as v1.2.3 or v1.2.3-preview.1.");
        }

        return Tag[1..];
    }
}
