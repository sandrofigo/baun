[Entry(nameof(Test))]
partial class Build
{
    [Parameter("Solution to test and pack")]
    readonly Solution Solution;

    readonly AbsolutePath ArtifactsDirectory = RootDirectory / "artifacts";

    [Parameter("Configuration to test and pack")]
    readonly Configuration Configuration = Configuration.Release;

    [Parameter("API key for pushing the packages to nuget.org", Name = "nuget-api-key")]
    readonly string? NuGetApiKey;

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
        .Before(Restore)
        .Executes(async () =>
        {
            if (ArtifactsDirectory.Exists)
            {
                ArtifactsDirectory.Delete();
                Log.Information("Deleted {ArtifactsDirectory}", ArtifactsDirectory);
            }

            await DotNet.Clean(Solution).Configuration(Configuration.ToString()).RunAsync();
        });

    Step Restore() => _ => _
        .Executes(async () =>
        {
            await DotNet.Restore(Solution).RunAsync();
        });

    Step Compile() => _ => _
        .DependsOn(Restore)
        .Executes(async () =>
        {
            DotNetBuildCommand build = DotNet.Build(Solution).Configuration(Configuration.ToString()).NoRestore();

            if (await Git.GetCurrentTagAsync() is not null)
            {
                build = build.Version(await GetVersionAsync()).ContinuousIntegrationBuild();
            }

            await build.RunAsync();
        });

    Step Test() => _ => _
        .DependsOn(Compile)
        .Executes(async () =>
        {
            await DotNet.Test().Solution(Solution).Configuration(Configuration.ToString()).NoBuild().RunAsync();
        });

    Step Pack() => _ => _
        .DependsOn(ValidateChangelog, Clean, Compile, Test)
        .Executes(async () =>
        {
            DotNetPackCommand pack = DotNet.Pack(Solution).Configuration(Configuration.ToString()).Output(ArtifactsDirectory).NoBuild();

            if (await Git.GetCurrentTagAsync() is not null)
            {
                pack = pack.Version(await GetVersionAsync()).ContinuousIntegrationBuild();
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

            GitHubRelease release = await GitHubReleases.CreateOrUpdateAsync(new GitHubRelease
            {
                Tag = $"v{version}",
                Notes = notes,
                Prerelease = version.Contains('-'),
            });

            Log.Information("Released {Release}: {Url}", release.Name, release.Url);

            if (GitHubActions.StepSummary is { } summary)
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
