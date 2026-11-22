---
title: CI
description: GitHub Actions, changelogs and log groups on CI servers.
---

## GitHub Actions

`GitHub` reads the [default environment variables](https://docs.github.com/en/actions/reference/variables-reference#default-environment-variables) of the GitHub Actions workflow run the build runs in, with a property for each, such as `GitHub.Tag`, `GitHub.RefName`, `GitHub.Sha` and `GitHub.Token`:

```csharp
Step Pack() => _ => _
    .Executes(async () =>
    {
        if (GitHub.Tag is { } tag)
        {
            // runs for a tag, such as v1.2.3
        }
    });
```

Properties with a path, such as `GitHub.Workspace`, return an `AbsolutePath`. A property is null, or false, when its variable isn't set or is empty, such as outside GitHub Actions. The variables are read each time a property is used, so you can set them yourself to try a run locally: `GITHUB_REF_TYPE=tag GITHUB_REF_NAME=v1.2.3 baun Pack`. To also accept a value on the command line, use the property as a parameter's initial value:

```csharp
[Parameter("Tag to release")]
string? Tag = GitHub.Tag;
```

## GitHub Releases

`GitHub.Release(tag)` describes the GitHub release for a tag. Set its values with the builder's methods, then call `CreateOrUpdateAsync` to create the release, or update it if it already exists, also as a draft, so the step can be run again:

```csharp
Step Release() => _ => _
    .Executes(async () =>
    {
        GitHubRelease release = await GitHub.Release(GitHub.Tag!)
            .Notes("- Fixed a bug")
            .Prerelease()
            .CreateOrUpdateAsync();

        Log.Information("Released {Url}", release.Url);
    });
```

All methods are optional:

| Method | Default for a new release |
| --- | --- |
| `Name("Version 1.2.3")` | The tag |
| `Notes(notes)` | No notes. Markdown, such as the notes from the [changelog](#changelog) |
| `Prerelease()` | Not a prerelease. `Prerelease(false)` unmarks one |
| `Draft()` | Not a draft. A draft is only visible to people with write access to the repository. `Draft(false)` publishes an existing draft |
| `Assets(files)` | No assets. Files to attach, see [Release Assets](#release-assets) |
| `Repository(repository)` | `GitHub.Repository`. To release to another repository, pass `new GitHubRepository("octocat", "Hello-World")` or `GitHubRepository.Parse("octocat/Hello-World")` |
| `Token(token)` | `GitHub.Token` |

An existing release keeps the values you don't set, so `GitHub.Release(tag).Notes(notes)` only changes the notes, and a draft stays a draft. GitHub creates the tag, if it doesn't exist yet, when the release is published.

Each method returns a new builder, so one builder can be the base of others. The API URL is `GitHub.ApiUrl`. Without a token or a repository, `CreateOrUpdateAsync` throws an `InvalidOperationException` that says which one is missing.

The token needs permission to write releases. GitHub Actions doesn't set `GITHUB_TOKEN` as an environment variable by itself, so pass the token of the run to the step that runs the build:

```yaml
permissions:
  contents: write

steps:
  - run: ./build.sh Release
    env:
      GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
```

### Release Assets

`Assets` attaches files to the release, each named after its file. Call it more than once to add more files:

```csharp
Step Release() => _ => _
    .Executes(async () =>
    {
        AbsolutePath artifacts = RootDirectory / "artifacts";

        await GitHub.Release(GitHub.Tag!)
            .Assets(artifacts.GlobFiles("*.nupkg", "*.zip"))
            .CreateOrUpdateAsync();
    });
```

When the step runs again, an attached asset with the same name is replaced and the other assets are kept. A release with assets that is to be published is kept a draft until the uploads are done, so it's never visible without them. A file that doesn't exist, or two files with the same name, throw before anything is sent to GitHub.

### Working With a Release

`CreateOrUpdateAsync` returns a `GitHubRelease` with its repository, ID, tag, name, notes, draft and prerelease flags, URL and assets. Its methods change the release on GitHub right away, and its properties follow the changes:

| Method | Does |
| --- | --- |
| `UploadAssetAsync(file)`, `UploadAssetsAsync(files)` | Uploads files and replaces attached assets with the same names |
| `DeleteAssetAsync("app.zip")` | Deletes an attached asset by name, and returns whether there was one |
| `PublishAsync()` | Publishes a draft |
| `DeleteAsync()` | Deletes the release and keeps the tag |

`GitHub.Release(tag).GetAsync()` returns the existing release, or null if there's none. It also finds drafts unless you pass `includeDrafts: false`. For example, create a draft, check it, then publish it:

```csharp
GitHubRelease release = await GitHub.Release(GitHub.Tag!)
    .Notes(notes)
    .Draft()
    .CreateOrUpdateAsync();

await release.UploadAssetAsync(RootDirectory / "artifacts" / "app.zip");

if (release.Assets.Count > 0)
{
    await release.PublishAsync();
}
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
        string tag = GitHub.Tag!;
        string notes = Changelog.Read().GetReleaseNotes(tag.TrimStart('v'));

        await GitHub.Release(tag).Notes(notes).CreateOrUpdateAsync();
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
