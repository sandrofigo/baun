[Entry(nameof(Test))]
partial class Build
{
    [Parameter("Solution to test and pack")]
    Solution Solution;

    static AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";

    [Parameter("Configuration to test and pack")]
    string Configuration = "Release";

    [Parameter("API key for pushing the packages to nuget.org", Name = "nuget-api-key")]
    string? NuGetApiKey;

    Step ValidateChangelog() => _ => _
        .Executes(async () =>
        {
            Changelog changelog = Changelog.Read();

            if (await Git.GetCurrentTagAsync() is null)
            {
                int unreleased = changelog.Unreleased?.Categories.Sum(category => category.Entries.Count) ?? 0;
                Log.Information("{File} is valid with {Count} entries in [Unreleased]", Changelog.DefaultFileName, unreleased);
                return;
            }

            string version = await GetVersionAsync();
            ChangelogRelease release = changelog.GetRelease(version);

            DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);

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
                throw new InvalidOperationException($"{Changelog.DefaultFileName} has no comparison link for [{version}]. Add one at the end of the file, for example: [{version}]: https://github.com/sandrofigo/baun/compare/vPREVIOUS...v{version}");
            }

            if (release.Notes.Length == 0)
            {
                throw new InvalidOperationException($"The section [{version}] in {Changelog.DefaultFileName} has no entries.");
            }

            Log.Information("{File} is valid and has a section for {Version}, dated today", Changelog.DefaultFileName, version);
        });

    Step Clean() => _ => _
        .Executes(() =>
        {
            if (ArtifactsDirectory.Exists)
            {
                ArtifactsDirectory.Delete();
                Log.Information("Deleted {ArtifactsDirectory}", ArtifactsDirectory);
            }

            Log.Information("Clean step successful");
        });

    Step Test() => _ => _
        .DependsOn(Clean)
        .Executes(async () =>
        {
            await DotNet.Test().Solution(Solution).Configuration(Configuration).RunAsync();
        });

    Step Pack() => _ => _
        .DependsOn(Clean, ValidateChangelog, Test)
        .Executes(async () =>
        {
            DotNetPackCommand pack = DotNet.Pack(Solution).Configuration(Configuration).Output(ArtifactsDirectory);

            if (await Git.GetCurrentTagAsync() is not null)
            {
                pack = pack.Version(await GetVersionAsync()).Property("ContinuousIntegrationBuild", "true");
            }

            await pack.RunAsync();
        });

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
                await DotNet.NuGetPush(package)
                    .ApiKey(apiKey)
                    .Source("https://api.nuget.org/v3/index.json")
                    .SkipDuplicate()
                    .RunAsync();
            }
        });

    Step GitHubRelease() => _ => _
        .DependsOn(PushPackages)
        .Executes(async () =>
        {
            string version = await GetVersionAsync();
            string notes = Changelog.Read().GetReleaseNotes(version);

            GitHubRelease release = await GitHub.CreateOrUpdateRelease($"v{version}", notes: notes, prerelease: version.Contains('-'));

            Log.Information("Released {Release}: {Url}", release.Name, release.Url);

            if (GitHub.StepSummary is { } summary)
            {
                await File.AppendAllTextAsync(summary, $"## [{release.Name}]({release.Url})\n\n{notes}\n");
            }
        });

    Step FormatChangelog() => _ => _
        .Executes(() =>
        {
            AbsolutePath path = RootDirectory / Changelog.DefaultFileName;

            if (KeepAChangelog.IO.Changelog.IsFormattedFile(path))
            {
                Log.Information("{File} is already formatted", Changelog.DefaultFileName);
                return;
            }

            KeepAChangelog.IO.Changelog.FormatFile(path);
            Log.Information("Formatted {File}", Changelog.DefaultFileName);
        });
}
