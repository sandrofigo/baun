[Entry(nameof(Test))]
partial class Build
{
    [Parameter("Solution to test and pack")]
    Solution Solution;

    static AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";

    [Parameter("Configuration to test and pack")]
    string Configuration = "Release";

    /// <summary>
    /// The tag being released, for example v1.2.3, or null when the build doesn't run for a tag. On GitHub Actions,
    /// it's the tag the workflow runs for.
    /// </summary>
    [Parameter("Tag to release, such as v1.2.3. Defaults to the tag the GitHub Actions run is for")]
    string? Tag = GitHub.Tag;

    [Parameter("API key for pushing the packages to nuget.org", Name = "nuget-api-key")]
    string? NuGetApiKey;

    Step ValidateChangelog() => _ => _
        .Executes(() =>
        {
            Changelog changelog = Changelog.Read();

            if (Tag is null)
            {
                int unreleased = changelog.Unreleased?.Categories.Sum(category => category.Entries.Count) ?? 0;
                Console.WriteLine($"{Changelog.DefaultFileName} is valid. [Unreleased] has {unreleased} entries.");
                return;
            }

            string version = GetVersion();
            ChangelogRelease release = changelog.GetRelease(version);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            if (release.Date is not { } date)
            {
                throw new InvalidOperationException($"The section [{version}] in {Changelog.DefaultFileName} has no date. Add today's date: ## [{version}] - {today:yyyy-MM-dd}");
            }

            if (date != today)
            {
                throw new InvalidOperationException($"The section [{version}] in {Changelog.DefaultFileName} is dated {date:yyyy-MM-dd}, but the release is made today ({today:yyyy-MM-dd}, UTC). Update the date and tag again.");
            }

            if (release.IsYanked)
            {
                throw new InvalidOperationException($"The section [{version}] in {Changelog.DefaultFileName} is marked as [YANKED].");
            }

            if (changelog.LatestRelease?.Version != version)
            {
                throw new InvalidOperationException($"The section [{version}] isn't the latest release in {Changelog.DefaultFileName}. It must come right after [Unreleased].");
            }

            if (release.Link is null)
            {
                throw new InvalidOperationException($"{Changelog.DefaultFileName} has no comparison link for [{version}]. Add one at the end of the file, for example: [{version}]: https://github.com/sandrofigo/baun/compare/vPREVIOUS...{Tag}");
            }

            if (release.Notes.Length == 0)
            {
                throw new InvalidOperationException($"The section [{version}] in {Changelog.DefaultFileName} has no entries.");
            }

            Console.WriteLine($"{Changelog.DefaultFileName} is valid and has a section for {version}, dated today.");
        });

    Step FormatChangelog() => _ => _
        .Executes(() =>
        {
            AbsolutePath path = RootDirectory / Changelog.DefaultFileName;

            if (KeepAChangelog.IO.Changelog.IsFormattedFile(path))
            {
                Console.WriteLine($"{Changelog.DefaultFileName} is already formatted.");
                return;
            }

            KeepAChangelog.IO.Changelog.FormatFile(path);
            Console.WriteLine($"Formatted {Changelog.DefaultFileName}.");
        });

    Step Clean() => _ => _
        .Executes(() =>
        {
            if (ArtifactsDirectory.Exists)
            {
                ArtifactsDirectory.Delete();
                Console.WriteLine($"Deleted {ArtifactsDirectory}");
            }
        });

    Step Test() => _ => _
        .DependsOn(Clean)
        .Executes(async () =>
        {
            await Run("dotnet", "test", "--solution", Solution, "--configuration", Configuration);
        });

    Step Pack() => _ => _
        .DependsOn(Clean, ValidateChangelog, Test)
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
        .Requires(() => NuGetApiKey)
        .Executes(async () =>
        {
            string apiKey = NuGetApiKey!;
            IReadOnlyList<AbsolutePath> packages = ArtifactsDirectory.GlobFiles("*.nupkg");

            if (packages.Count == 0)
            {
                throw new InvalidOperationException($"{ArtifactsDirectory} has no packages to push. Run Pack first.");
            }

            foreach (AbsolutePath package in packages)
            {
                await Run(
                    "dotnet", "nuget", "push", package,
                    "--api-key", apiKey,
                    "--source", "https://api.nuget.org/v3/index.json",
                    "--skip-duplicate");
            }
        });

    /// <summary>
    /// Creates the GitHub release for the tag, with the changelog entries of the released version as its notes.
    /// </summary>
    Step GitHubRelease() => _ => _
        .DependsOn(PushPackages)
        .Executes(async () =>
        {
            string version = GetVersion();
            string notes = Changelog.Read().GetReleaseNotes(version);

            GitHubRelease release = await GitHub.CreateOrUpdateRelease(Tag!, notes: notes, prerelease: version.Contains('-'));

            Console.WriteLine($"Released {release.Name}: {release.Url}");

            if (GitHub.StepSummary is { } summary)
            {
                await File.AppendAllTextAsync(summary, $"## [{release.Name}]({release.Url})\n\n{notes}\n");
            }
        });
}
