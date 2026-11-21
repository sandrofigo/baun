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

Properties with a path, such as `GitHub.Workspace`, `GitHub.StepSummary` and `GitHub.RunnerTemp`, return an `AbsolutePath`. A property is `null` (or `false`) when its variable isn't set or is empty, such as outside GitHub Actions. The variables are read each time a property is used, so you can set them yourself to try a run locally: `GITHUB_REF_TYPE=tag GITHUB_REF_NAME=v1.2.3 baun Pack`. To also accept a value on the command line, use the property as a parameter's initial value:

```csharp
[Parameter("Tag to release")]
string? Tag = GitHub.Tag;
```

`GitHub.CreateOrUpdateRelease` creates the GitHub release for a tag, or updates its title, notes and prerelease flag if it already exists, so the step can be run again. The repository defaults to `GitHub.Repository`, the token to `GitHub.Token`, the title to the tag, and the API URL to `GitHub.ApiUrl`. Pass `token:` to use another token. To release to another repository, pass `repository: new GitHubRepository("octocat", "Hello-World")` or `GitHubRepository.Parse("octocat/Hello-World")`:

```csharp
Step Release() => _ => _
    .Executes(async () =>
    {
        GitHubRelease release = await GitHub.CreateOrUpdateRelease(GitHub.Tag!, notes: "- Fixed a bug");
        Log.Information("Released {Url}", release.Url);
    });
```

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

`Changelog` reads a changelog in the [Keep a Changelog](https://keepachangelog.com/) format. `Changelog.Read()` reads `CHANGELOG.md` in `RootDirectory`. Pass an `AbsolutePath` to read another file, such as `Changelog.Read(RootDirectory / "docs" / "CHANGES.md")`, or use `Changelog.Parse(text)` for text. An invalid file throws a `ChangelogFormatException` that names the line.

A changelog has the `Unreleased` section, its `Releases` in the order of the file, and the `LatestRelease` (the one with the highest version). `GetRelease("1.2.3")` returns a release and throws if there's none, while `TryGetRelease` doesn't throw. Each `ChangelogRelease` has its `Version`, `Date`, `IsYanked`, the URL of its `Link` from the end of the file (`https://github.com/...` from `[1.2.3]: https://github.com/...`), or `null` if it has none, and `Categories`, such as `Added` and `Fixed`, with their `Entries`. `GetReleaseNotes("1.2.3")` (or a release's `Notes`) returns the categories with entries as Markdown, in the order of Keep a Changelog and without the release heading, so they work as the notes of a GitHub release:

```csharp
Step Release() => _ => _
    .Executes(async () =>
    {
        string tag = GitHub.Tag!;
        string notes = Changelog.Read().GetReleaseNotes(tag.TrimStart('v'));

        await GitHub.CreateOrUpdateRelease(tag, notes: notes);
    });
```

## Log groups

On a CI server that supports log groups, baun writes each step in a collapsible group named after the step. The summary and the error of a failing step are written outside the groups, so they stay visible. baun detects the CI server from its environment variables:

| CI server | Detected by |
| --- | --- |
| GitHub Actions | `GITHUB_ACTIONS=true` |
| Azure Pipelines | `TF_BUILD=True` |
| GitLab CI/CD | `GITLAB_CI=true` |
| TeamCity | `TEAMCITY_VERSION` |

Outside CI, steps are written as plain lines.
