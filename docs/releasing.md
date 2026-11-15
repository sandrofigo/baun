# Releasing baun

This guide describes how to publish a new version of the `Baun` and `Baun.Cli` packages to nuget.org.

A release starts when you push a version tag. The [`Release`](../.github/workflows/release.yml) workflow then runs the baun build in [`build/`](../build/Build.cs) with `build.sh`, which installs the .NET SDK if it's missing: it validates `CHANGELOG.md`, tests and packs both packages with the version from the tag, pushes them to nuget.org with [trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing), and creates the GitHub release with the version's changelog entries as its notes. No API key is stored in the repository.

> [!IMPORTANT]
> Versions published to nuget.org can't be deleted or replaced, only unlisted. Check everything before you push a release tag, and never reuse a version number.

## One-time setup

These settings are already in place. Check them if publishing fails, or when you set up a fork.

- **Trusted publishing policy on nuget.org** (your username → Trusted Publishing):
  - Repository Owner: `sandrofigo`
  - Repository: `baun`
  - Workflow File: `release.yml`
  - Environment: `release`
- **Repository secret `NUGET_USER`** (Settings → Secrets and variables → Actions): the nuget.org username (profile name, not the email address) of the account that owns the policy.
- **Environment `release`** (Settings → Environments):
  - Deployment branches and tags: a **tag** rule for `v*`. A branch rule doesn't match, because the workflow runs on the release tag.
  - Optional: required reviewers, to approve each release before it is pushed to nuget.org.

## 1. Choose the version

baun uses [semantic versioning](https://semver.org). The release tag is the version with a `v` in front:

| Release | Tag | Package version |
| --- | --- | --- |
| Stable | `v1.2.3` | `1.2.3` |
| Prerelease | `v1.2.3-preview.1` | `1.2.3-preview.1` |

The build rejects any other tag format. The tag is the only place the version is set: the build passes it to `dotnet pack`. Builds that don't run for a tag use the placeholder version `0.0.0-dev` from [`Directory.Build.props`](../Directory.Build.props), which never needs updating.

## 2. Update the changelog

[`CHANGELOG.md`](../CHANGELOG.md) follows the [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) format. Changes are collected under `## [Unreleased]` as they are made.

1. Check that `## [Unreleased]` lists every notable change since the last release, grouped under `### Added`, `### Changed`, `### Deprecated`, `### Removed`, `### Fixed` or `### Security`.
2. Rename it to the new version and today's date, and add an empty `## [Unreleased]` above it:

   ```markdown
   ## [Unreleased]

   ## [1.2.3] - 2026-11-13

   ### Added

   - ...
   ```

3. Update the comparison links at the end of the file, so `[Unreleased]` compares the new tag with `main` and the new version compares it with the previous tag:

   ```markdown
   [Unreleased]: https://github.com/sandrofigo/baun/compare/v1.2.3...HEAD
   [1.2.3]: https://github.com/sandrofigo/baun/compare/v1.2.2...v1.2.3
   ```

   For the first release, there is no previous tag to compare with:

   ```markdown
   [Unreleased]: https://github.com/sandrofigo/baun/compare/v0.1.0...HEAD
   [0.1.0]: https://github.com/sandrofigo/baun/releases/tag/v0.1.0
   ```

The build checks the changelog when the tag is pushed and fails before anything is published if:

- the file isn't valid Keep a Changelog (the error names the line),
- there is no section for the tag's version, or it isn't the latest release,
- the section's date isn't the day the tag is pushed (in UTC),
- the section has no entries, or the comparison link for the version is missing.

The GitHub release notes are that section of the changelog, as written in the file. Run `baun ValidateChangelog` (or `./build.sh ValidateChangelog`, `build ValidateChangelog` on Windows) to check that the file is valid; on pull requests, the `Test` workflow does the same.

## 3. Mark the analyzer rules as shipped

The source generator reports its own compiler errors (`BAUN001`, `BAUN002`, ...). Their IDs are tracked in two files in [`src/Baun.SourceGenerators`](../src/Baun.SourceGenerators):

- `AnalyzerReleases.Unshipped.md`: rules added or changed since the last release.
- `AnalyzerReleases.Shipped.md`: rules per published release.

If `AnalyzerReleases.Unshipped.md` lists any rules, move them to `AnalyzerReleases.Shipped.md` under a heading for the new version, and leave only the header comments in the unshipped file.

For example, for version `0.1.0`, `AnalyzerReleases.Shipped.md` becomes:

```markdown
; Shipped analyzer releases
; https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 0.1.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
BAUN001 | Baun | Error | StepGenerator
BAUN002 | Baun | Error | StepGenerator
BAUN004 | Baun | Error | StepGenerator
BAUN005 | Baun | Error | StepGenerator
BAUN006 | Baun | Error | StepGenerator
```

And `AnalyzerReleases.Unshipped.md` becomes:

```markdown
; Unshipped analyzer release
; https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md
```

Later releases add their own `## Release x.y.z` section below the earlier ones. Rules that were changed or removed go under `### Changed Rules` or `### Removed Rules` in that section, as described in the [release tracking documentation](https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md).

Build the solution afterwards. A rule that is missing from both files causes warning `RS2000`.

## 4. Prepare `main`

The release runs from the commit the tag points to, so everything that should be released must be on `main`.

1. Merge the changes from steps 1 to 3 into `main` with a pull request.
2. Make sure the `Test` workflow passed on that pull request.
3. Optionally, check the release locally from an up-to-date `main`. This runs the same checks as the release, including the changelog section for the version:

   ```shell
   GITHUB_REF_TYPE=tag GITHUB_REF_NAME=v1.2.3 baun Pack
   ```

## 5. Push the release tag

On the day of the date in the changelog, tag the up-to-date `main` and push the tag:

```shell
git switch main
git pull
git tag v1.2.3
git push origin v1.2.3
```

Pushing the tag starts the `Release` workflow. Don't create the GitHub release yourself; the build creates it after the packages are published.

## 6. Approve and follow the workflow

1. Open the **Actions** tab and select the `Release` run for the new tag.
2. The `build` job runs `./build.sh Pack`: it validates the changelog, tests and packs both packages. If it fails, nothing has been published.
3. The `publish` job waits for approval if the `release` environment has required reviewers. Click **Review deployments** and approve.
4. The `publish` job logs in to nuget.org and runs `./build.sh GitHubRelease`, which pushes both packages and then creates the GitHub release for the tag, marked as a pre-release for prerelease versions.

## 7. Check the published packages

1. Both packages appear on nuget.org within a few minutes. Search results and installs can take a bit longer while nuget.org indexes them.
   - https://www.nuget.org/packages/Baun
   - https://www.nuget.org/packages/Baun.Cli
2. Try the tool and a new build:

   ```shell
   dotnet tool update --global Baun.Cli --version 1.2.3
   baun init --single-file
   baun
   ```

   `baun init` references the Baun package with the same version as the tool, so this checks both packages together.
3. Optionally, update the `Baun` package in [`build/Build.csproj`](../build/Build.csproj) to the new version, so baun's own build uses it.

## If something goes wrong

- **The `build` job failed:** nothing was published. Fix the problem on `main`, delete the tag (`git tag -d v1.2.3` and `git push origin :refs/tags/v1.2.3`), and tag again.
- **The `publish` job failed before pushing,** for example at the nuget.org login: check the one-time setup above, then click **Re-run failed jobs**. The temporary API key is requested again on every run.
- **Only one package was pushed, or the GitHub release is missing:** re-run the failed jobs. The push uses `--skip-duplicate`, so packages that were already pushed are skipped, and an existing GitHub release for the tag is updated instead of created again.
- **A published version is broken:** unlist it on nuget.org (package page → Manage package → Listing) and release a new version with the fix. The version number can't be used again.
