using System.Text.RegularExpressions;
using KeepAChangelog.IO;

[Entry(nameof(Test))]
partial class Build
{
    const string ChangelogFile = "CHANGELOG.md";

    static string Solution => Path.Combine(RootDirectory, "Baun.slnx");
    static string ChangelogPath => Path.Combine(RootDirectory, ChangelogFile);
    static string ArtifactsDirectory => Path.Combine(RootDirectory, "artifacts");

    [Parameter("Configuration to test and pack")]
    string Configuration = "Release";

    [Parameter("Type of the ref the build runs for, set by GitHub Actions: branch or tag", Name = "github-ref-type")]
    string? GitHubRefType;

    [Parameter("Name of the ref the build runs for, set by GitHub Actions, such as v1.2.3", Name = "github-ref-name")]
    string? GitHubRefName;

    [Parameter("API key for pushing the packages to nuget.org", Name = "nuget-api-key")]
    string? NuGetApiKey;

    [Parameter("Token for creating the GitHub release", Name = "github-token")]
    string? GitHubToken;

    [Parameter("Repository of the GitHub release, set by GitHub Actions, such as sandrofigo/baun", Name = "github-repository")]
    string? GitHubRepository;

    /// <summary>
    /// The tag being released, for example v1.2.3, or null when the build doesn't run for a tag.
    /// </summary>
    string? Tag => GitHubRefType == "tag" ? GitHubRefName : null;

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

            if (!release.Categories.Any(category => category.Entries.Count > 0))
            {
                throw new InvalidOperationException($"The section [{version}] in {ChangelogFile} has no entries.");
            }

            Console.WriteLine($"{ChangelogFile} is valid and has a section for {version}, dated today.");
        });

    Step Test() => _ => _
        .Executes(async () =>
        {
            await Run("dotnet", "test", "--solution", Solution, "--configuration", Configuration);
        });

    Step Pack() => _ => _
        .DependsOn(ValidateChangelog, Test)
        .Executes(async () =>
        {
            List<string> arguments = ["pack", Solution, "--configuration", Configuration, "--output", ArtifactsDirectory];

            if (Tag is not null)
            {
                arguments.Add($"-p:Version={GetVersion()}");
                arguments.Add("-p:ContinuousIntegrationBuild=true");
            }

            await Run("dotnet", [.. arguments]);
        });

    /// <summary>
    /// Pushes the packages in the artifacts folder to nuget.org, along with their symbol packages (.snupkg).
    /// </summary>
    /// <remarks>
    /// Doesn't depend on <see cref="ValidateChangelog"/>: the publish job can be approved on a later day than the
    /// packages were built, and the changelog's date was already checked then.
    /// </remarks>
    Step PushPackages() => _ => _
        .Executes(async () =>
        {
            string apiKey = Required(NuGetApiKey, "nuget-api-key");

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
            string notes = GetReleaseNotes(GetRelease(ReadChangelog(), version));

            await CreateOrUpdateGitHubRelease(
                Required(GitHubToken, "github-token"),
                Required(GitHubRepository, "github-repository"),
                Tag!,
                notes,
                prerelease: version.Contains('-'));
        });

    static Changelog ReadChangelog()
    {
        try
        {
            return Changelog.FromFile(ChangelogPath);
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
    string GetVersion()
    {
        if (Tag is null)
        {
            throw new InvalidOperationException("This step only runs for a release tag (--github-ref-type tag --github-ref-name v1.2.3).");
        }

        if (!Regex.IsMatch(Tag, @"^v[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$"))
        {
            throw new InvalidOperationException($"The tag '{Tag}' is not a version such as v1.2.3 or v1.2.3-preview.1.");
        }

        return Tag[1..];
    }
}
