---
title: CI
description: GitHub Actions, GitHub releases, changelogs and log groups on CI servers.
---

## GitHub Actions

`GitHubActions` reads the [default environment variables](https://docs.github.com/en/actions/reference/variables-reference#default-environment-variables) of the GitHub Actions workflow run the build runs in, with a property for each, such as `GitHubActions.Tag`, `GitHubActions.RefName`, `GitHubActions.Sha` and `GitHubActions.Token`:

```csharp
Step Pack() => _ => _
    .Executes(async () =>
    {
        if (GitHubActions.Tag is { } tag)
        {
            // runs for a tag, such as v1.2.3
        }
    });
```

`GitHubActions.IsRunning` is true when the build runs in GitHub Actions. Properties with a path, such as `GitHubActions.Workspace`, return an `AbsolutePath`. A property is null, or false, when its variable isn't set or is empty, such as outside GitHub Actions. The variables are read each time a property is used, so you can set them yourself to try a run locally: `GITHUB_REF_TYPE=tag GITHUB_REF_NAME=v1.2.3 baun Pack`. To also accept a value on the command line, use the property as a parameter's initial value:

```csharp
[Parameter("Tag to release")]
string? Tag = GitHubActions.Tag;
```

## GitHub Releases

A `GitHubRelease` is a release you change in code, then save to GitHub with `GitHubReleases.CreateOrUpdateAsync`. Changing it doesn't call GitHub: the changes, also the added and removed assets, are sent when you save it.

Create a new release with its tag and save it:

```csharp
Step Release() => _ => _
    .Executes(async () =>
    {
        var release = new GitHubRelease
        {
            Tag = GitHubActions.Tag!,
            Notes = "- Fixed a bug",
            Prerelease = true,
        };

        await GitHubReleases.CreateOrUpdateAsync(release);
        Log.Information("Released {Url}", release.Url);
    });
```

If a release for the tag already exists, also as a draft, it's updated instead, so the step can be run again. After saving, the release has the values from GitHub, such as its `Id`, `Url` and `Assets`. Until then, `Id` and `Url` are null.

When a new `GitHubRelease` updates an existing release, `Draft` and `Prerelease` are set as they are on the object, so saving `new GitHubRelease { Tag = tag }` over a draft publishes it. Set `Draft = true` to keep it a draft, or [get the release](#changing-an-existing-release) first to change only some of its values.

A release has:

| Property | Default for a new release |
| --- | --- |
| `Tag` | Required. GitHub creates the tag, if it doesn't exist yet, when the release is published |
| `Name` | The tag. If it's null, an existing release keeps its title |
| `Notes` | No notes. Markdown, such as the notes from the [changelog](#changelog). If they're null, an existing release keeps its notes |
| `Draft` | `false`. A draft is only visible to people with write access to the repository. Setting it to `false` publishes a draft |
| `Prerelease` | `false` |
| `Repository` | `GitHubActions.Repository`. To release to another repository, set `new GitHubRepository("octocat", "Hello-World")` or `GitHubRepository.Parse("octocat/Hello-World")` |

### Changing an Existing Release

`GitHubReleases.GetAsync(tag)` gets an existing release, or null if there's none. It also finds drafts unless you pass `includeDrafts: false`, and takes a `repository` and a [token](#token). Change the release, then save it:

```csharp
GitHubRelease? release = await GitHubReleases.GetAsync("v1.2.3");

if (release is not null)
{
    release.Name = "Version 1.2.3";
    release.Draft = false;

    await GitHubReleases.CreateOrUpdateAsync(release);
}
```

A release you got or saved is updated by its ID, so you can also change its `Tag`.

### Deleting a Release

`GitHubReleases.DeleteAsync(release)` deletes a release you got or saved, and keeps its tag. Afterwards, the release is new again, so saving it creates it again. Deleting a release that was never saved throws an `InvalidOperationException`.

```csharp
if (await GitHubReleases.GetAsync("v1.2.3-rc.1") is { } candidate)
{
    await GitHubReleases.DeleteAsync(candidate);
}
```

### Release Assets

`AddAsset` and `AddAssets` add files to upload when the release is saved, each named after its file. They return the release, so you can chain them:

```csharp
Step Release() => _ => _
    .Executes(async () =>
    {
        AbsolutePath artifacts = RootDirectory / "artifacts";

        var release = new GitHubRelease { Tag = GitHubActions.Tag! }
            .AddAssets(artifacts.GlobFiles("*.nupkg", "*.zip"));

        await GitHubReleases.CreateOrUpdateAsync(release);
    });
```

An added file replaces an attached asset with the same name, and the other assets are kept, so the step can be run again. A release with added files that is to be published is kept a draft until the uploads are done, so it's never visible without them.

`RemoveAsset("app.zip")` removes an added file, or deletes an attached asset when the release is saved. It ignores case and returns whether there was an asset with the name:

```csharp
GitHubRelease? release = await GitHubReleases.GetAsync("v1.2.3");

release!.RemoveAsset("app-preview.zip");
await GitHubReleases.CreateOrUpdateAsync(release);
```

`Assets` has the files attached on GitHub, and `AddedAssets` and `RemovedAssets` the changes that aren't saved yet. Each asset has its `Id`, `Name`, `Size` in bytes and `DownloadUrl`.

Adding two files with the same name throws an `ArgumentException`, because GitHub names an asset after its file. An added file that doesn't exist throws a `FileNotFoundException` when you save, before anything is sent to GitHub.

### Token

The methods of `GitHubReleases` take a `token`, which defaults to `GitHubActions.Token`. The API URL is `GitHubActions.ApiUrl`. Without a token or a repository, they throw an `InvalidOperationException` that says which one is missing.

The token needs permission to write releases. GitHub Actions doesn't set `GITHUB_TOKEN` as an environment variable by itself, so pass the token of the run to the step that runs the build:

```yaml
permissions:
  contents: write

steps:
  - run: ./build.sh Release
    env:
      GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
```

## Changelog

`Changelog` reads a changelog in the [Keep a Changelog](https://keepachangelog.com/) format. `Changelog.Read()` reads CHANGELOG.md in the root directory. Pass a path to read another file, such as `Changelog.Read(RootDirectory / "docs" / "CHANGES.md")`, or use `Changelog.Parse` for text. An invalid file throws a `ChangelogFormatException` that names the line.

A changelog has:

- `Unreleased`: the Unreleased section
- `Releases`: the releases in the order of the file
- `LatestRelease`: the release with the highest version
- `GetRelease("1.2.3")`: a release by version, which throws if there's none. `TryGetRelease` doesn't throw.

Each release has its version, date and whether it's yanked, its link from the end of the file if it has one, such as the URL in `[1.2.3]: https://github.com/...`, and its categories, such as Added and Fixed, with their entries.

`GetReleaseNotes("1.2.3")`, or a release's `Notes`, returns the categories with entries as Markdown, in the order of Keep a Changelog and without the release heading, so they work as the notes of a GitHub release:

```csharp
Step Release() => _ => _
    .Executes(async () =>
    {
        string tag = GitHubActions.Tag!;
        string notes = Changelog.Read().GetReleaseNotes(tag.TrimStart('v'));

        await GitHubReleases.CreateOrUpdateAsync(new GitHubRelease { Tag = tag, Notes = notes });
    });
```

## Log Groups

On a CI server that supports log groups, baun writes each step in a collapsible group named after the step. The summary and the error of a failing step are written outside the groups, so they stay visible. baun detects the CI server from its environment variables:

| CI server | Detected by |
| --- | --- |
| GitHub Actions | `GITHUB_ACTIONS=true` |
| Azure Pipelines | `TF_BUILD=True` |
| GitLab CI/CD | `GITLAB_CI=true` |
| TeamCity | `TEAMCITY_VERSION` |

Outside CI, steps are written as plain lines.
