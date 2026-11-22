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

`GitHub.CreateOrUpdateRelease` creates the GitHub release for a tag, or updates its title, notes, draft and prerelease flags if it already exists, so the step can be run again. GitHub creates the tag if it doesn't exist yet when the release is published. The returned release has its ID, tag, name, notes, draft and prerelease flags, URL and assets:

```csharp
Step Release() => _ => _
    .Executes(async () =>
    {
        GitHubRelease release = await GitHub.CreateOrUpdateRelease(GitHub.Tag!, notes: "- Fixed a bug");
        Log.Information("Released {Url}", release.Url);
    });
```

The other parameters are optional:

| Parameter | Default |
| --- | --- |
| `notes` | No notes. Markdown, such as the notes from the [changelog](#changelog) |
| `prerelease` | `false` |
| `draft` | `false`. A draft is only visible to people with write access to the repository. Passing `false` for an existing draft publishes it |
| `assets` | No assets. Files to attach to the release, see [Release Assets](#release-assets) |
| `name` | The tag |
| `repository` | `GitHub.Repository`. To release to another repository, pass `new GitHubRepository("octocat", "Hello-World")` or `GitHubRepository.Parse("octocat/Hello-World")` |
| `token` | `GitHub.Token` |

The API URL is `GitHub.ApiUrl`. Without a token or a repository, the method throws an `InvalidOperationException` that says which one is missing.

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

`assets` attaches files to the release, each named after its file:

```csharp
Step Release() => _ => _
    .Executes(async () =>
    {
        AbsolutePath artifacts = RootDirectory / "artifacts";

        await GitHub.CreateOrUpdateRelease(GitHub.Tag!, assets: artifacts.GlobFiles("*.nupkg", "*.zip"));
    });
```

When the step runs again, an attached asset with the same name is replaced and the other assets are kept. A new release with assets is created as a draft and published after the uploads, so it's never visible without them. Pass `draft: true` to keep it a draft, for example to check the release before you publish it on GitHub. A file that doesn't exist, or two files with the same name, throw before anything is sent to GitHub.

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

        await GitHub.CreateOrUpdateRelease(tag, notes: notes);
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
